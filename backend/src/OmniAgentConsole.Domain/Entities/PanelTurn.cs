using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Domain.Entities;

public sealed class PanelTurn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid MemberId { get; set; }
    public string MemberDisplayName { get; set; } = string.Empty;
    public int TurnOrder { get; set; }
    public string? Output { get; set; }
    public PanelTurnStatus Status { get; set; } = PanelTurnStatus.Pending;
    public string? ModelUsed { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }
    public long LatencyMs { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public PanelSession? Session { get; set; }
    public AgentGroupMember? Member { get; set; }
}
