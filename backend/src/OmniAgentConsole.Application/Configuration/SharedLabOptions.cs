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

    /// <summary>Max concurrent Pending+Running tasks per student session (0 = unlimited).</summary>
    public int MaxConcurrentTasksPerSession { get; set; } = 2;

    /// <summary>Max tasks created per UTC day per student session (0 = unlimited).</summary>
    public int MaxTasksPerDayPerSession { get; set; } = 30;

    /// <summary>Soft daily token budget per session across model calls (0 = unlimited).</summary>
    public int MaxDailyTokensPerSession { get; set; } = 500_000;
}
