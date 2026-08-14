namespace OmniAgentConsole.Application.Configuration;

/// <summary>
/// Optional host directory the API may copy projects from into /workspace
/// (bind-mounted read-only as <see cref="HostRoot"/> inside the container).
/// </summary>
public sealed class WorkspaceImportOptions
{
    public const string SectionName = "WorkspaceImport";

    /// <summary>Container path of the read-only host import mount (e.g. /host-import).</summary>
    public string HostRoot { get; set; } = "/host-import";

    /// <summary>When false, host-copy endpoints return empty / 404.</summary>
    public bool Enabled { get; set; } = true;
}
