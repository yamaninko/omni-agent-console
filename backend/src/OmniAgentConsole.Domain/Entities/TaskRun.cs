using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Domain.Entities;

public sealed class TaskRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string InputPrompt { get; set; } = string.Empty;
    public string? InputContextJson { get; set; }
    public TaskRunStatus Status { get; set; } = TaskRunStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public int TotalTokens { get; set; }
    public long TotalLatencyMs { get; set; }
    public string? ErrorMessage { get; set; }

    public ICollection<AgentRun> AgentRuns { get; set; } = new List<AgentRun>();
    public ICollection<ModelCallLog> ModelCallLogs { get; set; } = new List<ModelCallLog>();
    public ICollection<ConsoleEvent> ConsoleEvents { get; set; } = new List<ConsoleEvent>();
}
