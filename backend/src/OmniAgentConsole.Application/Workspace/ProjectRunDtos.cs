namespace OmniAgentConsole.Application.Workspace;

public sealed record ProjectDetectResponse(
    string ProjectRoot,
    bool HasDockerfile,
    bool HasCompose,
    bool Runnable,
    int SuggestedHostPort,
    string ComposeProjectName,
    string HealthUrl,
    string UpCommand,
    string DownCommand,
    string StatusCommand,
    string? Message);

public sealed record ProjectRunStatusResponse(
    string ProjectRoot,
    string ComposeProjectName,
    int HostPort,
    string State,
    bool RunnerEnabled,
    string? HealthUrl,
    string? HealthStatus,
    string? Detail,
    string? LogsTail);

public sealed record ProjectRunActionResponse(
    bool Ok,
    string State,
    string Message,
    string? LogsTail);
