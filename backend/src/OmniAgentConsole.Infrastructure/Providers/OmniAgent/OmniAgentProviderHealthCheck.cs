using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OmniAgentConsole.Application.Configuration;
using OmniAgentConsole.Application.Providers;
using OmniAgentConsole.Application.Secrets;
using OmniAgentConsole.Application.Settings;

namespace OmniAgentConsole.Infrastructure.Providers.OmniAgent;

public sealed class OmniAgentProviderHealthCheck : IProviderHealthCheck
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly IProviderSecretResolver secretResolver;
    private readonly OmniAgentProviderOptions options;

    public OmniAgentProviderHealthCheck(
        HttpClient httpClient,
        IProviderSecretResolver secretResolver,
        IOptions<OmniAgentProviderOptions> options)
    {
        this.httpClient = httpClient;
        this.secretResolver = secretResolver;
        this.options = options.Value;
        this.httpClient.Timeout = TimeSpan.FromSeconds(Math.Min(this.options.TimeoutSeconds, 30));
    }

    public async Task<ProviderHealthStatusDto> CheckOmniAgentAsync(CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var apiKey = await secretResolver.GetOmniAgentApiKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ProviderHealthStatusDto(
                "OmniAgent",
                options.DefaultModel,
                false,
                false,
                "NotConfigured",
                "OMNIAGENT API key is not configured.",
                0,
                checkedAt);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUri());
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = JsonContent.Create(new
            {
                model = options.DefaultModel,
                messages = new[]
                {
                    new { role = "user", content = "Health check. Reply with OK only." }
                },
                temperature = 0,
                max_tokens = 4
            }, options: JsonOptions);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new ProviderHealthStatusDto(
                    "OmniAgent",
                    options.DefaultModel,
                    true,
                    true,
                    "Healthy",
                    "OMNIAGENT API key and model call succeeded.",
                    stopwatch.ElapsedMilliseconds,
                    checkedAt);
            }

            var status = MapStatus(response.StatusCode);

            return new ProviderHealthStatusDto(
                "OmniAgent",
                options.DefaultModel,
                true,
                false,
                status,
                BuildFailureMessage(response.StatusCode, status),
                stopwatch.ElapsedMilliseconds,
                checkedAt);
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            return new ProviderHealthStatusDto(
                "OmniAgent",
                options.DefaultModel,
                true,
                false,
                "Timeout",
                "OMNIAGENT health check timed out.",
                stopwatch.ElapsedMilliseconds,
                checkedAt);
        }
        catch (HttpRequestException exception)
        {
            stopwatch.Stop();
            return new ProviderHealthStatusDto(
                "OmniAgent",
                options.DefaultModel,
                true,
                false,
                "ProviderUnavailable",
                $"OMNIAGENT endpoint could not be reached: {exception.Message}",
                stopwatch.ElapsedMilliseconds,
                checkedAt);
        }
    }

    private Uri BuildChatCompletionsUri()
    {
        var baseUrl = options.BaseUrl.TrimEnd('/');
        return new Uri($"{baseUrl}/chat/completions");
    }

    private static string MapStatus(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Unauthorized",
            HttpStatusCode.TooManyRequests => "RateLimit",
            HttpStatusCode.NotFound => "InvalidModelOrEndpoint",
            HttpStatusCode.BadRequest => "InvalidRequest",
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout => "ProviderUnavailable",
            _ => "Failed"
        };
    }

    private static string BuildFailureMessage(HttpStatusCode statusCode, string status)
    {
        return status switch
        {
            "Unauthorized" => "OMNIAGENT rejected the API key.",
            "RateLimit" => "OMNIAGENT rate limit was reached.",
            "InvalidModelOrEndpoint" => "OMNIAGENT endpoint or configured model was not found.",
            "InvalidRequest" => "OMNIAGENT rejected the health check request.",
            "ProviderUnavailable" => "OMNIAGENT provider is temporarily unavailable.",
            _ => $"OMNIAGENT health check failed with HTTP {(int)statusCode}."
        };
    }
}
