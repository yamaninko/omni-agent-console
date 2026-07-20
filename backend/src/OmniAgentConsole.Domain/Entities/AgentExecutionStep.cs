using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Domain.Entities;

public sealed class AgentExecutionStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskRunId { get; set; }
    public Guid AgentRunId { get; set; }
    public string StepName { get; set; } = string.Empty;
    public AgentRunStatus Status { get; set; } = AgentRunStatus.Pending;
    public string? Message { get; set; }
    public string? PayloadJson { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long LatencyMs { get; set; }

    public TaskRun? TaskRun { get; set; }
    public AgentRun? AgentRun { get; set; }
}
