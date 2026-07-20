using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OmniAgentConsole.Application.Configuration;
using OmniAgentConsole.Application.Providers;
using OmniAgentConsole.Application.Secrets;
using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Infrastructure.Providers.OmniAgent;

public sealed class OmniAgentModelProvider : IModelProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly IProviderSecretResolver secretResolver;
    private readonly IApiCredentialKeyResolver credentialKeys;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly OmniAgentProviderOptions options;

    public OmniAgentModelProvider(
        HttpClient httpClient,
        IProviderSecretResolver secretResolver,
        IApiCredentialKeyResolver credentialKeys,
        IServiceScopeFactory scopeFactory,
        IOptions<OmniAgentProviderOptions> options)
    {
        this.httpClient = httpClient;
        this.secretResolver = secretResolver;
        this.credentialKeys = credentialKeys;
        this.scopeFactory = scopeFactory;
        this.options = options.Value;
        this.httpClient.BaseAddress = new Uri(this.options.BaseUrl);
        this.httpClient.Timeout = TimeSpan.FromSeconds(this.options.TimeoutSeconds);
    }

    public ProviderType ProviderType => ProviderType.OmniAgent;

    public bool Supports(string model)
    {
        return !string.IsNullOrWhiteSpace(model);
    }

    public async Task<ModelResponse> CreateChatCompletionAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var baseUrl = options.BaseUrl;
        var apiKey = await secretResolver.GetOmniAgentApiKeyAsync(cancellationToken);

        if (request.Metadata != null)
        {
            if (request.Metadata.TryGetValue("customApiUrl", out var customUrl) && !string.IsNullOrWhiteSpace(customUrl))
            {
                baseUrl = customUrl;
            }

            // Prefer credential secret-ref resolution (Vault / dual-read). Never
            // require raw keys in metadata. Fallback: agent-level CustomApiKey
            // (legacy) loaded by agentDefinitionId, then obsolete customApiKey field.
            string? resolved = null;
            if (request.Metadata.TryGetValue("apiCredentialId", out var credentialIdText)
                && Guid.TryParse(credentialIdText, out var credentialId)
                && credentialId != Guid.Empty)
            {
                resolved = await credentialKeys.ResolveByIdAsync(credentialId, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(resolved)
                && request.Metadata.TryGetValue("agentDefinitionId", out var agentIdText)
                && Guid.TryParse(agentIdText, out var agentId)
                && agentId != Guid.Empty)
            {
                resolved = await ResolveAgentLegacyCustomKeyAsync(agentId, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(resolved)
                && request.Metadata.TryGetValue("customApiKey", out var customKey)
                && !string.IsNullOrWhiteSpace(customKey))
            {
                resolved = customKey;
            }

            if (!string.IsNullOrWhiteSpace(resolved))
            {
                apiKey = resolved;
            }
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ProviderException(
                ProviderErrorCode.Unauthorized,
                "API key is not configured.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, request.TimeoutSeconds)));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUri(baseUrl));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            httpRequest.Content = JsonContent.Create(BuildRequestPayload(request), options: JsonOptions);

            using var response = await httpClient.SendAsync(httpRequest, timeout.Token);
            var responseBody = await response.Content.ReadAsStringAsync(timeout.Token);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderException(
                    MapErrorCode(response.StatusCode, responseBody),
                    BuildFailureMessage(response.StatusCode, responseBody));
            }

            return ParseResponse(request, responseBody);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            throw new ProviderException(
                ProviderErrorCode.Timeout,
                "OMNIAGENT model call timed out.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            stopwatch.Stop();
            throw new ProviderException(
                ProviderErrorCode.ProviderUnavailable,
                $"OMNIAGENT endpoint could not be reached: {exception.Message}",
                exception);
        }
        catch (JsonException exception)
        {
            stopwatch.Stop();
            throw new ProviderException(
                ProviderErrorCode.UnknownError,
                "OMNIAGENT response could not be parsed.",
                exception);
        }
    }

    private async Task<string?> ResolveAgentLegacyCustomKeyAsync(Guid agentDefinitionId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentConsoleDbContext>();
        var customKey = await db.AgentDefinitions
            .AsNoTracking()
            .Where(a => a.Id == agentDefinitionId)
            .Select(a => a.CustomApiKey)
            .FirstOrDefaultAsync(cancellationToken);
        return ApiCredentialSecretPolicy.IsRealKey(customKey) ? customKey : null;
    }

    private Uri BuildChatCompletionsUri(string baseUrl)
    {
        var url = baseUrl.TrimEnd('/');
        return new Uri($"{url}/chat/completions");
    }

    private static Dictionary<string, object?> BuildRequestPayload(ModelRequest request)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = request.Messages.Select(BuildMessagePayload).ToList(),
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxTokens
        };

        if (request.Tools is { Count: > 0 })
        {
            payload["tools"] = request.Tools.Select(tool => new
            {
                type = "function",
                function = new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = JsonSerializer.Deserialize<JsonElement>(tool.ParametersJsonSchema)
                }
            }).ToList<object>();
            payload["tool_choice"] = "auto";
        }

        return payload;
    }

    private static Dictionary<string, object?> BuildMessagePayload(ChatMessage message)
    {
        var payload = new Dictionary<string, object?>
        {
            ["role"] = message.Role,
            ["content"] = message.Content
        };

        if (message.ToolCalls is { Count: > 0 })
        {
            payload["tool_calls"] = message.ToolCalls.Select(call => new
            {
                id = call.Id,
                type = "function",
                function = new { name = call.Name, arguments = call.ArgumentsJson }
            }).ToList<object>();
        }

        if (!string.IsNullOrEmpty(message.ToolCallId))
        {
            payload["tool_call_id"] = message.ToolCallId;
        }

        return payload;
    }

    private static ModelResponse ParseResponse(ModelRequest request, string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        var firstChoice = root.GetProperty("choices")[0];
        var message = firstChoice.GetProperty("message");
        var content = ReadContent(message);
        var finishReason = firstChoice.TryGetProperty("finish_reason", out var finishReasonElement)
            ? finishReasonElement.GetString()
            : null;

        int? inputTokens = null;
        int? outputTokens = null;
        int? totalTokens = null;
        if (root.TryGetProperty("usage", out var usage))
        {
            inputTokens = ReadInt(usage, "prompt_tokens");
            outputTokens = ReadInt(usage, "completion_tokens");
            totalTokens = ReadInt(usage, "total_tokens");
        }

        return new ModelResponse(
            request.Provider,
            request.Model,
            content,
            finishReason,
            inputTokens,
            outputTokens,
            totalTokens,
            responseBody,
            ReadToolCalls(message));
    }

    private static IReadOnlyList<ChatToolCall>? ReadToolCalls(JsonElement message)
    {
        if (!message.TryGetProperty("tool_calls", out var toolCalls) || toolCalls.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var calls = new List<ChatToolCall>();
        foreach (var item in toolCalls.EnumerateArray())
        {
            if (!item.TryGetProperty("function", out var function))
            {
                continue;
            }

            var name = function.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var arguments = function.TryGetProperty("arguments", out var argsElement)
                ? argsElement.ValueKind == JsonValueKind.String ? argsElement.GetString() ?? "{}" : argsElement.GetRawText()
                : "{}";

            calls.Add(new ChatToolCall(id ?? $"call_{calls.Count}", name, arguments));
        }

        return calls.Count > 0 ? calls : null;
    }

    private static string ReadContent(JsonElement message)
    {
        var content = ReadContentProperty(message);

        // Some reasoning models (nemotron-super, nemotron-nano...) put their whole
        // answer into a reasoning field and leave content empty. Falling back keeps
        // those models usable instead of failing the run with an "empty response".
        if (string.IsNullOrWhiteSpace(content))
        {
            foreach (var fallbackProperty in new[] { "reasoning_content", "reasoning" })
            {
                if (message.TryGetProperty(fallbackProperty, out var reasoning)
                    && reasoning.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(reasoning.GetString()))
                {
                    return reasoning.GetString()!;
                }
            }
        }

        return content;
    }

    private static string ReadContentProperty(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return content.ToString();
        }

        var parts = new List<string>();
        foreach (var item in content.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                parts.Add(item.GetString() ?? string.Empty);
                continue;
            }

            if (item.TryGetProperty("text", out var text))
            {
                parts.Add(text.GetString() ?? string.Empty);
            }
        }

        return string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;
    }

    private static ProviderErrorCode MapErrorCode(HttpStatusCode statusCode, string responseBody)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return ProviderErrorCode.Unauthorized;
        }

        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return ProviderErrorCode.RateLimit;
        }

        // 404: model id unknown; 410: model was removed from the catalog.
        if (statusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return ProviderErrorCode.InvalidModel;
        }

        if (statusCode == HttpStatusCode.BadRequest)
        {
            return responseBody.Contains("model", StringComparison.OrdinalIgnoreCase)
                ? ProviderErrorCode.InvalidModel
                : ProviderErrorCode.InvalidRequest;
        }

        if (statusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
        {
            return ProviderErrorCode.ProviderUnavailable;
        }

        return ProviderErrorCode.UnknownError;
    }

    private static string BuildFailureMessage(HttpStatusCode statusCode, string responseBody)
    {
        var errorCode = MapErrorCode(statusCode, responseBody);

        return errorCode switch
        {
            ProviderErrorCode.Unauthorized => "Provider rejected the API key.",
            ProviderErrorCode.RateLimit => "Provider rate limit was reached.",
            ProviderErrorCode.InvalidModel => "Provider rejected the configured model.",
            ProviderErrorCode.InvalidRequest => "Provider rejected the model request.",
            ProviderErrorCode.ProviderUnavailable => "Provider is temporarily unavailable.",
            _ => $"Model call failed with HTTP {(int)statusCode}."
        };
    }
}
