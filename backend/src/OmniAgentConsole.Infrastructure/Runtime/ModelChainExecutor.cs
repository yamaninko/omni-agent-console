using OmniAgentConsole.Application.Agents;
using OmniAgentConsole.Application.Providers;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Infrastructure.Runtime;

/// <summary>
/// Executes one logical model call against an agent's primary → fallback chain:
/// per-model transient retries with backoff, advancement on every error except
/// auth, empty-response detection, and "falling back" console events. Keeps the
/// <see cref="ModelCallLog"/> pointed at the model actually used.
/// </summary>
public sealed class ModelChainExecutor
{
    private readonly AgentConsoleDbContext dbContext;
    private readonly IConsoleEventService consoleEvents;
    private readonly IModelProvider modelProvider;

    public ModelChainExecutor(
        AgentConsoleDbContext dbContext,
        IConsoleEventService consoleEvents,
        IModelProvider modelProvider)
    {
        this.dbContext = dbContext;
        this.consoleEvents = consoleEvents;
        this.modelProvider = modelProvider;
    }

    /// <summary>
    /// Walks the agent's primary → fallback model chain until one model returns a
    /// usable answer (text content or tool calls). Updates <paramref name="modelCall"/>
    /// to the model actually used so usage tracking reflects fallbacks.
    /// </summary>
    public async Task<ModelResponse> ExecuteAsync(
        ModelRequest modelRequest,
        AgentDefinition agentDefinition,
        ModelCallLog modelCall,
        Guid taskRunId,
        Guid agentRunId,
        CancellationToken cancellationToken)
    {
        var modelChain = BuildModelChain(modelRequest.Model, agentDefinition.FallbackModels);
        ModelResponse response = null!;

        for (var chainIndex = 0; chainIndex < modelChain.Count; chainIndex++)
        {
            var chainModel = modelChain[chainIndex];

            if (chainIndex > 0)
            {
                modelCall.Model = chainModel;
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }

            try
            {
                response = await ExecuteWithRetryAsync(
                    modelRequest with { Model = chainModel },
                    agentDefinition.RetryCount,
                    taskRunId,
                    agentRunId,
                    cancellationToken);

                // Congested free-tier endpoints occasionally return HTTP 200 with
                // zero completion tokens; no text and no tool calls is a failure.
                if (string.IsNullOrWhiteSpace(response.Content) && response.ToolCalls is not { Count: > 0 })
                {
                    throw new ProviderException(
                        ProviderErrorCode.UnknownError,
                        $"Model {chainModel} returned an empty response (0 completion tokens).");
                }

                break;
            }
            catch (ProviderException exception) when (
                chainIndex < modelChain.Count - 1 && ShouldFallbackToNextModel(exception.ErrorCode))
            {
                await consoleEvents.WriteAsync(
                    taskRunId,
                    agentRunId,
                    ConsoleEventType.Warning,
                    $"Model {chainModel} failed ({exception.ErrorCode}: {exception.Message}); falling back to {modelChain[chainIndex + 1]}.",
                    RunTelemetry.BuildErrorPayload(exception),
                    CancellationToken.None);
            }
        }

        return response;
    }

    private async Task<ModelResponse> ExecuteWithRetryAsync(
        ModelRequest request,
        int retryCount,
        Guid taskRunId,
        Guid agentRunId,
        CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(0, retryCount);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await modelProvider.CreateChatCompletionAsync(request, cancellationToken);
            }
            catch (ProviderException exception) when (attempt < maxRetries && IsTransient(exception.ErrorCode))
            {
                var delay = TimeSpan.FromMilliseconds(Math.Min(4000, 500 * Math.Pow(2, attempt)));
                await consoleEvents.WriteAsync(
                    taskRunId,
                    agentRunId,
                    ConsoleEventType.Warning,
                    $"Provider call retry {attempt + 1}/{maxRetries} after {exception.ErrorCode}.",
                    RunTelemetry.BuildErrorPayload(exception),
                    cancellationToken);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Primary model first, then the comma-separated fallbacks in order,
    /// de-duplicated case-insensitively. Public for unit testing.
    /// </summary>
    public static IReadOnlyList<string> BuildModelChain(string primaryModel, string? fallbackModels)
    {
        var chain = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? model)
        {
            var trimmed = model?.Trim();
            if (!string.IsNullOrEmpty(trimmed) && seen.Add(trimmed))
            {
                chain.Add(trimmed);
            }
        }

        Add(primaryModel);
        foreach (var fallback in (fallbackModels ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            Add(fallback);
        }

        return chain;
    }

    /// <summary>
    /// A different model is worth trying for everything except auth failures —
    /// the same key is used for the whole chain, so 401 would fail everywhere.
    /// </summary>
    public static bool ShouldFallbackToNextModel(ProviderErrorCode errorCode) =>
        errorCode != ProviderErrorCode.Unauthorized;

    private static bool IsTransient(ProviderErrorCode errorCode)
    {
        return errorCode is ProviderErrorCode.RateLimit
            or ProviderErrorCode.Timeout
            or ProviderErrorCode.ProviderUnavailable
            or ProviderErrorCode.UnknownError;
    }
}
