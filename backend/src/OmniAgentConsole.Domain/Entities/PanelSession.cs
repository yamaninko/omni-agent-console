using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Domain.Entities;

/// <summary>
/// One moderated single-round panel discussion for a group + topic.
/// </summary>
public sealed class PanelSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    /// <summary>How many full roster passes to run (1–3). Continue sessions use 1 extra pass.</summary>
    public int MaxRounds { get; set; } = 1;
    public PanelSessionStatus Status { get; set; } = PanelSessionStatus.Pending;
    public string? OwnerSessionId { get; set; }
    public Guid? CurrentMemberId { get; set; }
    public DateTimeOffset? FloorDeadline { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long TotalLatencyMs { get; set; }
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public int TotalTokens { get; set; }
    public string? ErrorMessage { get; set; }

    public AgentGroup? Group { get; set; }
    public ICollection<PanelTurn> Turns { get; set; } = new List<PanelTurn>();
    public ICollection<PanelConsoleEvent> ConsoleEvents { get; set; } = new List<PanelConsoleEvent>();
}
