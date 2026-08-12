namespace OmniAgentConsole.Domain.Entities;

/// <summary>
/// Named collection of panel personas (guests). Orthogonal to pipeline AgentDefinitions.
/// </summary>
public sealed class AgentGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<AgentGroupMember> Members { get; set; } = new List<AgentGroupMember>();
    public ICollection<PanelSession> PanelSessions { get; set; } = new List<PanelSession>();
}
