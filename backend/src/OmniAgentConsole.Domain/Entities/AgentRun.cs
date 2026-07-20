using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Domain.Entities;

public sealed class AgentRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskRunId { get; set; }
    public Guid? AgentDefinitionId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public AgentType AgentType { get; set; }
    public AgentRunStatus Status { get; set; } = AgentRunStatus.Pending;
    public string? Input { get; set; }
    public string? Output { get; set; }
    public string? ConfigSnapshotJson { get; set; }
    public int ExecutionOrder { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long LatencyMs { get; set; }
    public string? ErrorMessage { get; set; }

    public TaskRun? TaskRun { get; set; }
    public AgentDefinition? AgentDefinition { get; set; }
    public ICollection<ModelCallLog> ModelCallLogs { get; set; } = new List<ModelCallLog>();
    public ICollection<ConsoleEvent> ConsoleEvents { get; set; } = new List<ConsoleEvent>();
    public ICollection<AgentExecutionStep> ExecutionSteps { get; set; } = new List<AgentExecutionStep>();
}
