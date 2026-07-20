using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OmniAgentConsole.Application.Agents;
using OmniAgentConsole.Application.Providers;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Infrastructure.Runtime;

/// <summary>
/// Console-event payload builders, cost estimation, prompt hashing and storage
/// trimming shared by the run components. Pure serialization — no I/O.
/// </summary>
internal static class RunTelemetry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string BuildErrorPayload(Exception exception)
    {
        return JsonSerializer.Serialize(new
        {
            error = exception.Message,
            errorCode = exception is ProviderException providerException
                ? providerException.ErrorCode.ToString()
                : ProviderErrorCode.UnknownError.ToString()
        }, JsonOptions);
    }

    public static string ComputePromptHash(IReadOnlyList<ChatMessage> messages)
    {
        var prompt = string.Join('\n', messages.Select(message => $"{message.Role}:{message.Content}"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(prompt));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string TrimForStorage(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "\n[truncated]");
    }

    public static string BuildAgentConfigPayload(AgentDefinition agentDefinition, ModelRoute route)
    {
        return JsonSerializer.Serialize(new
        {
            agentDefinition.Name,
            Type = agentDefinition.Type.ToString(),
            Provider = route.Provider.ToString(),
            route.Model,
            agentDefinition.MaxTokens,
            agentDefinition.Temperature,
            agentDefinition.TimeoutSeconds,
            agentDefinition.RetryCount
        }, JsonOptions);
    }

    public static string BuildAgentPayload(AgentDefinition agentDefinition, ModelRoute route)
    {
        return JsonSerializer.Serialize(new
        {
            agentDefinition.Id,
            agentDefinition.Name,
            Type = agentDefinition.Type.ToString(),
            Provider = route.Provider.ToString(),
            route.Model
        }, JsonOptions);
    }

    public static string BuildModelCallPayload(ModelCallLog modelCall)
    {
        return JsonSerializer.Serialize(new
        {
            modelCall.Id,
            Provider = modelCall.Provider.ToString(),
            modelCall.Model,
            Status = modelCall.Status.ToString()
        }, JsonOptions);
    }

    public static string BuildUsagePayload(ModelCallLog modelCall)
    {
        return JsonSerializer.Serialize(new
        {
            modelCall.Id,
            Provider = modelCall.Provider.ToString(),
            modelCall.Model,
            modelCall.InputTokens,
            modelCall.OutputTokens,
            modelCall.TotalTokens,
            modelCall.LatencyMs,
            Status = modelCall.Status.ToString()
        }, JsonOptions);
    }

    public static string BuildAgentResultPayload(AgentRun agentRun)
    {
        return JsonSerializer.Serialize(new
        {
            agentRun.Id,
            agentRun.AgentName,
            AgentType = agentRun.AgentType.ToString(),
            Status = agentRun.Status.ToString(),
            agentRun.LatencyMs,
            agentRun.Output
        }, JsonOptions);
    }

    public static string BuildTaskPayload(TaskRun taskRun)
    {
        return JsonSerializer.Serialize(new
        {
            taskRun.Id,
            Status = taskRun.Status.ToString(),
            taskRun.TotalInputTokens,
            taskRun.TotalOutputTokens,
            taskRun.TotalTokens,
            taskRun.TotalLatencyMs
        }, JsonOptions);
    }

    public static decimal CalculateEstimatedCost(string model, int inputTokens, int outputTokens)
    {
        var pricing = new Dictionary<string, (decimal Input, decimal Output)>(StringComparer.OrdinalIgnoreCase)
        {
            ["meta/llama-3.1-8b-instruct"] = (0.05m, 0.05m),
            ["meta/llama-3.1-70b-instruct"] = (0.52m, 0.52m),
            ["meta/llama-3.1-405b-instruct"] = (2.66m, 2.66m),
            ["omniagent/nemotron-4-340b-instruct"] = (0.50m, 0.50m),
            ["mistralai/mixtral-8x22b-instruct-v0.1"] = (0.30m, 0.30m)
        };

        var (inputCostPerMillion, outputCostPerMillion) = pricing.TryGetValue(model, out var cost)
            ? cost
            : (0.05m, 0.05m);

        decimal inputCost = (inputTokens / 1_000_000m) * inputCostPerMillion;
        decimal outputCost = (outputTokens / 1_000_000m) * outputCostPerMillion;

        return inputCost + outputCost;
    }
}
