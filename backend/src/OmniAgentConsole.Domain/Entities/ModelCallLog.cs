using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Domain.Entities;

public sealed class ModelCallLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskRunId { get; set; }
    public Guid AgentRunId { get; set; }
    public ProviderType Provider { get; set; }
    public string Model { get; set; } = string.Empty;
    public ModelRequestType RequestType { get; set; } = ModelRequestType.ChatCompletion;
    public string PromptHash { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }
    public long LatencyMs { get; set; }
    public ModelCallStatus Status { get; set; } = ModelCallStatus.Started;
    public ProviderErrorCode ErrorCode { get; set; } = ProviderErrorCode.None;
    public string? ErrorMessage { get; set; }
    public decimal? EstimatedCost { get; set; }
    public string? RawMetadataJson { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public TaskRun? TaskRun { get; set; }
    public AgentRun? AgentRun { get; set; }
}
