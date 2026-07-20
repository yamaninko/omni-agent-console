namespace OmniAgentConsole.Application.Workspace;

public sealed record ProjectRouteHint(string Method, string Path, string Label);

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
    string? Message,
    /// <summary>api | web | hybrid | unknown</summary>
    string ProjectKind,
    string BaseUrl,
    string OpenUrl,
    IReadOnlyList<ProjectRouteHint> SuggestedRoutes);

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

public sealed record ProjectProxyRequest(
    string? ProjectPath,
    string Method,
    string Path,
    Dictionary<string, string>? Headers,
    string? Body);

public sealed record ProjectProxyResponse(
    bool Ok,
    int StatusCode,
    long LatencyMs,
    string? ContentType,
    string Body,
    IReadOnlyDictionary<string, string> Headers,
    string? Error);
