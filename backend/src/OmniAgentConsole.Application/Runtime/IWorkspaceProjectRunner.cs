using OmniAgentConsole.Application.Workspace;

namespace OmniAgentConsole.Application.Runtime;

public interface IWorkspaceProjectRunner
{
    ProjectDetectResponse Detect(string effectiveRoot, string? path, string? sessionId);

    Task<ProjectRunActionResponse> UpAsync(
        string effectiveRoot,
        string? path,
        string? sessionId,
        CancellationToken cancellationToken);

    Task<ProjectRunActionResponse> DownAsync(
        string effectiveRoot,
        string? path,
        string? sessionId,
        CancellationToken cancellationToken);

    Task<ProjectRunStatusResponse> StatusAsync(
        string effectiveRoot,
        string? path,
        string? sessionId,
        CancellationToken cancellationToken);

    Task<ProjectProxyResponse> ProxyAsync(
        string effectiveRoot,
        ProjectProxyRequest request,
        string? sessionId,
        CancellationToken cancellationToken);
}
