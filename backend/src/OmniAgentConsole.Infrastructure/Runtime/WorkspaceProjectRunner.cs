using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniAgentConsole.Application.Configuration;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Application.Workspace;

namespace OmniAgentConsole.Infrastructure.Runtime;

public sealed class WorkspaceProjectRunner : IWorkspaceProjectRunner
{
    private readonly WorkspaceRunnerOptions options;
    private readonly ILogger<WorkspaceProjectRunner> logger;
    private readonly ConcurrentDictionary<string, byte> activeProjects = new(StringComparer.Ordinal);

    public WorkspaceProjectRunner(
        IOptions<WorkspaceRunnerOptions> options,
        ILogger<WorkspaceProjectRunner> logger)
    {
        this.options = options.Value;
        this.logger = logger;
    }

    public ProjectDetectResponse Detect(string effectiveRoot, string? path, string? sessionId)
    {
        var layout = WorkspaceProjectDetector.Detect(effectiveRoot, path);
        if (layout is null)
        {
            return new ProjectDetectResponse(
                ProjectRoot: path ?? ".",
                HasDockerfile: false,
                HasCompose: false,
                Runnable: false,
                SuggestedHostPort: options.PortRangeStart,
                ComposeProjectName: "",
                HealthUrl: "",
                UpCommand: "",
                DownCommand: "",
                StatusCommand: "",
                Message: "No Dockerfile or docker-compose.yml found near this path. Enable the Dockerized Service skill and re-run the task.",
                ProjectKind: "unknown",
                BaseUrl: "",
                OpenUrl: "",
                SuggestedRoutes: []);
        }

        var projectName = WorkspaceProjectDetector.ComposeProjectName(layout.RelativeRoot, sessionId);
        var port = WorkspaceProjectDetector.SuggestHostPort(
            layout.RelativeRoot,
            options.PortRangeStart,
            options.PortRangeSize);
        var baseUrl = $"http://localhost:{port}";
        var healthUrl = $"{baseUrl}/health";
        var kind = WorkspaceProjectDetector.ClassifyProjectKind(layout.FullRoot);
        var routes = WorkspaceProjectDetector.SuggestRoutes(layout.FullRoot, kind);

        return new ProjectDetectResponse(
            ProjectRoot: layout.RelativeRoot,
            HasDockerfile: layout.HasDockerfile,
            HasCompose: layout.HasCompose,
            Runnable: layout.HasDockerfile || layout.HasCompose,
            SuggestedHostPort: port,
            ComposeProjectName: projectName,
            HealthUrl: healthUrl,
            UpCommand: WorkspaceProjectDetector.BuildUpCommand(layout, projectName, port),
            DownCommand: WorkspaceProjectDetector.BuildDownCommand(layout, projectName),
            StatusCommand: WorkspaceProjectDetector.BuildStatusCommand(projectName, layout.HasCompose),
            Message: layout.HasCompose
                ? null
                : "Dockerfile found without compose — runner will use docker build/run. Prefer docker-compose.yml for multi-service projects.",
            ProjectKind: kind,
            BaseUrl: baseUrl,
            OpenUrl: baseUrl + "/",
            SuggestedRoutes: routes);
    }

    public async Task<ProjectRunActionResponse> UpAsync(
        string effectiveRoot,
        string? path,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return new ProjectRunActionResponse(false, "disabled", "Workspace runner is disabled (WorkspaceRunner:Enabled=false).", null);
        }

        var detect = Detect(effectiveRoot, path, sessionId);
        if (!detect.Runnable)
        {
            return new ProjectRunActionResponse(false, "not_runnable", detect.Message ?? "Project is not runnable.", null);
        }

        if (activeProjects.Count >= options.MaxConcurrent
            && !activeProjects.ContainsKey(detect.ComposeProjectName))
        {
            return new ProjectRunActionResponse(
                false,
                "busy",
                $"Max concurrent projects ({options.MaxConcurrent}) reached. Stop another project first.",
                null);
        }

        var layout = WorkspaceProjectDetector.Detect(effectiveRoot, path)!;
        activeProjects[detect.ComposeProjectName] = 0;

        // If already up, treat Start as rebuild + recreate (user expectation on
        // second Start click). --force-recreate replaces containers; --build
        // picks up code changes under /workspace.
        var wasRunning = await IsProjectRunningAsync(layout, detect.ComposeProjectName, cancellationToken);

        try
        {
            ProcessResult result;
            if (layout.HasCompose)
            {
                result = await RunDockerAsync(
                    layout.FullRoot,
                    [
                        "compose", "-p", detect.ComposeProjectName,
                        "up", "-d", "--build", "--force-recreate", "--remove-orphans"
                    ],
                    new Dictionary<string, string> { ["HOST_PORT"] = detect.SuggestedHostPort.ToString() },
                    cancellationToken);
            }
            else
            {
                var build = await RunDockerAsync(
                    layout.FullRoot,
                    ["build", "-t", detect.ComposeProjectName, "."],
                    null,
                    cancellationToken);
                if (build.ExitCode != 0)
                {
                    return new ProjectRunActionResponse(false, "failed", "docker build failed.", Truncate(build.Output));
                }

                // Always replace any previous container with the same name.
                await RunDockerAsync(layout.FullRoot, ["rm", "-f", detect.ComposeProjectName], null, cancellationToken);
                var containerPort = WorkspaceProjectDetector.GuessContainerPort(layout.FullRoot);
                result = await RunDockerAsync(
                    layout.FullRoot,
                    [
                        "run", "-d", "--rm",
                        "--name", detect.ComposeProjectName,
                        "-p", $"{detect.SuggestedHostPort}:{containerPort}",
                        detect.ComposeProjectName
                    ],
                    null,
                    cancellationToken);
            }

            var ok = result.ExitCode == 0;
            var verb = wasRunning ? "recreated (build + force-recreate)" : "starting";
            return new ProjectRunActionResponse(
                ok,
                ok ? "starting" : "failed",
                ok
                    ? $"Project {verb}. Health: {detect.HealthUrl}"
                    : "docker command failed.",
                Truncate(result.Output));
        }
        finally
        {
            // Keep slot until down, or drop on failure after a short hold via status checks.
        }
    }

    public async Task<ProjectRunActionResponse> DownAsync(
        string effectiveRoot,
        string? path,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return new ProjectRunActionResponse(false, "disabled", "Workspace runner is disabled.", null);
        }

        var detect = Detect(effectiveRoot, path, sessionId);
        if (!detect.Runnable)
        {
            return new ProjectRunActionResponse(false, "not_runnable", detect.Message ?? "Project is not runnable.", null);
        }

        var layout = WorkspaceProjectDetector.Detect(effectiveRoot, path)!;
        ProcessResult result;
        if (layout.HasCompose)
        {
            result = await RunDockerAsync(
                layout.FullRoot,
                ["compose", "-p", detect.ComposeProjectName, "down"],
                null,
                cancellationToken);
        }
        else
        {
            result = await RunDockerAsync(
                layout.FullRoot,
                ["rm", "-f", detect.ComposeProjectName],
                null,
                cancellationToken);
        }

        activeProjects.TryRemove(detect.ComposeProjectName, out _);
        var ok = result.ExitCode == 0;
        return new ProjectRunActionResponse(
            ok,
            ok ? "stopped" : "failed",
            ok ? "Project stopped." : "Stop command failed.",
            Truncate(result.Output));
    }

    public async Task<ProjectRunStatusResponse> StatusAsync(
        string effectiveRoot,
        string? path,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        var detect = Detect(effectiveRoot, path, sessionId);
        if (!detect.Runnable)
        {
            return new ProjectRunStatusResponse(
                detect.ProjectRoot,
                detect.ComposeProjectName,
                detect.SuggestedHostPort,
                "not_runnable",
                options.Enabled,
                null,
                null,
                detect.Message,
                null);
        }

        if (!options.Enabled)
        {
            return new ProjectRunStatusResponse(
                detect.ProjectRoot,
                detect.ComposeProjectName,
                detect.SuggestedHostPort,
                "disabled",
                false,
                detect.HealthUrl,
                null,
                "Runner disabled — use the copy-paste command on the host.",
                null);
        }

        var layout = WorkspaceProjectDetector.Detect(effectiveRoot, path)!;
        ProcessResult ps;
        if (layout.HasCompose)
        {
            ps = await RunDockerAsync(
                layout.FullRoot,
                ["compose", "-p", detect.ComposeProjectName, "ps", "--format", "json"],
                null,
                cancellationToken);
        }
        else
        {
            ps = await RunDockerAsync(
                layout.FullRoot,
                ["ps", "--filter", $"name=^{detect.ComposeProjectName}$", "--format", "{{.Status}}"],
                null,
                cancellationToken);
        }

        var running = ps.ExitCode == 0
            && !string.IsNullOrWhiteSpace(ps.Output)
            && (ps.Output.Contains("running", StringComparison.OrdinalIgnoreCase)
                || ps.Output.Contains("Up", StringComparison.OrdinalIgnoreCase)
                || ps.Output.Contains("\"State\"", StringComparison.Ordinal));

        string? healthStatus = null;
        if (running)
        {
            var probeHost = string.IsNullOrWhiteSpace(options.HealthProbeHost)
                ? "localhost"
                : options.HealthProbeHost;
            var probeUrl = $"http://{probeHost}:{detect.SuggestedHostPort}/health";
            healthStatus = await ProbeHealthAsync(probeUrl, cancellationToken);
        }

        if (!running)
        {
            activeProjects.TryRemove(detect.ComposeProjectName, out _);
        }

        return new ProjectRunStatusResponse(
            detect.ProjectRoot,
            detect.ComposeProjectName,
            detect.SuggestedHostPort,
            running ? "running" : "stopped",
            options.Enabled,
            detect.HealthUrl,
            healthStatus,
            Truncate(ps.Output),
            null);
    }

    public async Task<ProjectProxyResponse> ProxyAsync(
        string effectiveRoot,
        ProjectProxyRequest request,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        var detect = Detect(effectiveRoot, request.ProjectPath, sessionId);
        if (!detect.Runnable || string.IsNullOrWhiteSpace(detect.BaseUrl))
        {
            return new ProjectProxyResponse(
                false, 0, 0, null, "", new Dictionary<string, string>(),
                detect.Message ?? "Project is not runnable.");
        }

        var method = string.IsNullOrWhiteSpace(request.Method) ? "GET" : request.Method.Trim().ToUpperInvariant();
        if (method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS"))
        {
            return new ProjectProxyResponse(false, 0, 0, null, "", new Dictionary<string, string>(), "Unsupported HTTP method.");
        }

        var path = request.Path ?? "/";
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        // Build target on localhost + assigned port only (never trust a full URL from the client).
        if (!Uri.TryCreate(detect.BaseUrl.TrimEnd('/') + path, UriKind.Absolute, out var target)
            || !WorkspaceProjectDetector.IsAllowedProxyTarget(target, options.PortRangeStart, options.PortRangeSize))
        {
            return new ProjectProxyResponse(
                false, 0, 0, null, "", new Dictionary<string, string>(),
                "Proxy target rejected. Only localhost ports in the workspace runner range are allowed.");
        }

        // From inside the API container, hit host-published ports via HealthProbeHost.
        var probeHost = string.IsNullOrWhiteSpace(options.HealthProbeHost) ? "localhost" : options.HealthProbeHost;
        var builder = new UriBuilder(target) { Host = probeHost };
        var requestUri = builder.Uri;

        if (request.Body is { Length: > 1_000_000 })
        {
            return new ProjectProxyResponse(false, 0, 0, null, "", new Dictionary<string, string>(), "Body exceeds 1 MB limit.");
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var httpRequest = new HttpRequestMessage(new HttpMethod(method), requestUri);

        if (request.Headers is not null)
        {
            foreach (var (key, value) in request.Headers)
            {
                if (string.IsNullOrWhiteSpace(key) || value is null)
                {
                    continue;
                }

                // Host is forced; hop-by-hop headers skipped.
                if (key.Equals("Host", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!httpRequest.Headers.TryAddWithoutValidation(key, value)
                    && httpRequest.Content is null
                    && method is "POST" or "PUT" or "PATCH")
                {
                    // content headers applied after body is set
                }
            }
        }

        if (method is "POST" or "PUT" or "PATCH" || !string.IsNullOrEmpty(request.Body))
        {
            httpRequest.Content = new StringContent(request.Body ?? "", Encoding.UTF8);
            if (request.Headers is not null
                && request.Headers.TryGetValue("Content-Type", out var ct)
                && !string.IsNullOrWhiteSpace(ct))
            {
                httpRequest.Content.Headers.Remove("Content-Type");
                httpRequest.Content.Headers.TryAddWithoutValidation("Content-Type", ct);
            }
            else if (httpRequest.Content.Headers.ContentType is null)
            {
                httpRequest.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
            }
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(httpRequest, cancellationToken);
            sw.Stop();
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length > 500_000)
            {
                body = body[..500_000] + "\n…[truncated]";
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in response.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }

            foreach (var header in response.Content.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }

            return new ProjectProxyResponse(
                true,
                (int)response.StatusCode,
                sw.ElapsedMilliseconds,
                response.Content.Headers.ContentType?.ToString(),
                body,
                headers,
                null);
        }
        catch (Exception exception)
        {
            sw.Stop();
            logger.LogWarning(exception, "Workspace project proxy failed for {Uri}", requestUri);
            return new ProjectProxyResponse(
                false, 0, sw.ElapsedMilliseconds, null, "", new Dictionary<string, string>(),
                exception.Message);
        }
    }

    private async Task<bool> IsProjectRunningAsync(
        ProjectLayout layout,
        string composeProjectName,
        CancellationToken cancellationToken)
    {
        ProcessResult ps;
        if (layout.HasCompose)
        {
            ps = await RunDockerAsync(
                layout.FullRoot,
                ["compose", "-p", composeProjectName, "ps", "--status", "running", "-q"],
                null,
                cancellationToken);
        }
        else
        {
            ps = await RunDockerAsync(
                layout.FullRoot,
                ["ps", "-q", "--filter", $"name=^{composeProjectName}$"],
                null,
                cancellationToken);
        }

        return ps.ExitCode == 0 && !string.IsNullOrWhiteSpace(ps.Output);
    }

    private async Task<ProcessResult> RunDockerAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        Dictionary<string, string>? extraEnv,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = options.DockerBinary,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        if (extraEnv is not null)
        {
            foreach (var (key, value) in extraEnv)
            {
                psi.Environment[key] = value;
            }
        }

        // Compose files often use ${HOST_PORT:-8000} — also set for child.
        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.AppendLine(e.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                return new ProcessResult(-1, "Failed to start docker process.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(30, options.BuildTimeoutSeconds)));

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignore
                }

                return new ProcessResult(-1, output + "\n[timeout]");
            }

            return new ProcessResult(process.ExitCode, output.ToString());
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Docker invocation failed: {Args}", string.Join(' ', args));
            return new ProcessResult(-1, exception.Message);
        }
    }

    private static async Task<string> ProbeHealthAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var response = await client.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode ? "healthy" : $"http_{(int)response.StatusCode}";
        }
        catch
        {
            return "unreachable";
        }
    }

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.Length <= 4000 ? value : value[^4000..];
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
