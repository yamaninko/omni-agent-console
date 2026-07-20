namespace OmniAgentConsole.Application.Configuration;

/// <summary>
/// Deployment profile switch. Disabled (default) = single-user laptop profile,
/// today's behavior. Enabled = shared-lab profile: session-scoped tasks and
/// workspaces, admin-gated settings. See docs/ROADMAP.md §1 for the contract.
/// </summary>
public sealed class SharedLabOptions
{
    public const string SectionName = "SharedLab";

    public bool Enabled { get; set; }
}
