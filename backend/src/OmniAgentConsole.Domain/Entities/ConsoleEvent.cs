using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Domain.Entities;

public sealed class ConsoleEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskRunId { get; set; }
    public Guid? AgentRunId { get; set; }
    public ConsoleEventType EventType { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public TaskRun? TaskRun { get; set; }
    public AgentRun? AgentRun { get; set; }
}
