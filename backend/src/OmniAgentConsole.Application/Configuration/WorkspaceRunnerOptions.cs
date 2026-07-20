namespace OmniAgentConsole.Application.Configuration;

public sealed class WorkspaceRunnerOptions
{
    public const string SectionName = "WorkspaceRunner";

    /// <summary>When false, detect/commands still work; up/down return 503.</summary>
    public bool Enabled { get; set; } = true;

    public int PortRangeStart { get; set; } = 18000;
    public int PortRangeSize { get; set; } = 1000;
    public int MaxConcurrent { get; set; } = 3;
    public int BuildTimeoutSeconds { get; set; } = 300;
    public string DockerBinary { get; set; } = "docker";

    /// <summary>
    /// Host used when the API probes project /health from inside Docker.
    /// Browser-facing URLs still use localhost. In compose set host.docker.internal.
    /// </summary>
    public string HealthProbeHost { get; set; } = "localhost";
}
