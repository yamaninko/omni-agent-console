using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Domain.Entities;

public sealed class UsageMetric
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
    public ProviderType? Provider { get; set; }
    public string? Model { get; set; }
    public AgentType? AgentType { get; set; }
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public int TotalTokens { get; set; }
    public long TotalLatencyMs { get; set; }
    public decimal? EstimatedCost { get; set; }
}
