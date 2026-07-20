using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OmniAgentConsole.Application.Workspace;

namespace OmniAgentConsole.Application.Runtime;

/// <summary>
/// Locates a runnable project root under the workspace and builds host-side
/// docker compose commands. Pure filesystem inspection + port hashing.
/// </summary>
public static partial class WorkspaceProjectDetector
{
    private static readonly string[] ComposeNames = ["docker-compose.yml", "docker-compose.yaml", "compose.yml", "compose.yaml"];

    public static ProjectLayout? Detect(string effectiveRoot, string? userPath)
    {
        if (string.IsNullOrWhiteSpace(userPath))
        {
            userPath = ".";
        }

        if (!WorkspacePathGuard.TryResolve(effectiveRoot, userPath, out var startPath))
        {
            return null;
        }

        // If a file was selected, start from its directory.
        if (File.Exists(startPath))
        {
            startPath = Path.GetDirectoryName(startPath) ?? startPath;
        }

        var rootFull = WorkspacePathGuard.NormalizeRoot(effectiveRoot);
        var current = startPath;
        while (true)
        {
            var hasCompose = ComposeNames.Any(name => File.Exists(Path.Combine(current, name)));
            var hasDockerfile = File.Exists(Path.Combine(current, "Dockerfile"));
            if (hasCompose || hasDockerfile)
            {
                var relative = Path.GetRelativePath(rootFull, current).Replace('\\', '/');
                if (relative is "." or "")
                {
                    relative = ".";
                }

                return new ProjectLayout(current, relative, hasDockerfile, hasCompose, FindComposeFileName(current));
            }

            if (string.Equals(current, rootFull, StringComparison.Ordinal))
            {
                break;
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || parent.Length < rootFull.Length)
            {
                break;
            }

            current = parent;
        }

        return null;
    }

    public static int SuggestHostPort(string projectRelativeRoot, int portRangeStart, int portRangeSize)
    {
        portRangeSize = Math.Max(1, portRangeSize);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(projectRelativeRoot.ToLowerInvariant()));
        var value = BitConverter.ToUInt32(hash, 0);
        return portRangeStart + (int)(value % (uint)portRangeSize);
    }

    public static string ComposeProjectName(string projectRelativeRoot, string? sessionId = null)
    {
        var slug = ProjectSlug().Replace(projectRelativeRoot.Replace('\\', '/'), "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slug) || slug == ".")
        {
            slug = "root";
        }

        if (slug.Length > 32)
        {
            slug = slug[..32].Trim('-');
        }

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var sessionSlug = ProjectSlug().Replace(sessionId, "-").Trim('-');
            if (sessionSlug.Length > 12)
            {
                sessionSlug = sessionSlug[..12];
            }

            return $"omni-{sessionSlug}-{slug}".ToLowerInvariant();
        }

        return $"omni-{slug}".ToLowerInvariant();
    }

    public static string BuildUpCommand(ProjectLayout layout, string composeProjectName, int hostPort)
    {
        if (layout.HasCompose)
        {
            return $"cd {ShellQuote(layout.RelativeRoot == "." ? "." : layout.RelativeRoot)} && HOST_PORT={hostPort} docker compose -p {composeProjectName} up -d --build";
        }

        // Dockerfile-only fallback.
        return $"cd {ShellQuote(layout.RelativeRoot == "." ? "." : layout.RelativeRoot)} && docker build -t {composeProjectName} . && docker run -d --rm --name {composeProjectName} -p {hostPort}:{GuessContainerPort(layout.FullRoot)} {composeProjectName}";
    }

    public static string BuildDownCommand(ProjectLayout layout, string composeProjectName)
    {
        if (layout.HasCompose)
        {
            return $"cd {ShellQuote(layout.RelativeRoot == "." ? "." : layout.RelativeRoot)} && docker compose -p {composeProjectName} down";
        }

        return $"docker rm -f {composeProjectName}";
    }

    public static string BuildStatusCommand(string composeProjectName, bool hasCompose) =>
        hasCompose
            ? $"docker compose -p {composeProjectName} ps"
            : $"docker ps --filter name={composeProjectName}";

    public static int GuessContainerPort(string projectFullRoot)
    {
        var dockerfile = Path.Combine(projectFullRoot, "Dockerfile");
        if (!File.Exists(dockerfile))
        {
            return 8000;
        }

        try
        {
            foreach (var line in File.ReadLines(dockerfile))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("EXPOSE", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[1].Split('/')[0], out var port) && port is > 0 and < 65536)
                    {
                        return port;
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        return 8000;
    }

    /// <summary>
    /// Classifies a project as api / web / hybrid / unknown from filesystem cues.
    /// </summary>
    public static string ClassifyProjectKind(string projectFullRoot)
    {
        var web = LooksLikeWeb(projectFullRoot);
        var api = LooksLikeApi(projectFullRoot);

        if (web && api)
        {
            return "hybrid";
        }

        if (web)
        {
            return "web";
        }

        if (api)
        {
            return "api";
        }

        // Runnable backend containers without strong frontend markers → API default.
        if (File.Exists(Path.Combine(projectFullRoot, "Dockerfile"))
            || File.Exists(Path.Combine(projectFullRoot, "docker-compose.yml"))
            || File.Exists(Path.Combine(projectFullRoot, "compose.yml")))
        {
            return "api";
        }

        return "unknown";
    }

    public static IReadOnlyList<ProjectRouteHint> SuggestRoutes(string projectFullRoot, string projectKind)
    {
        var routes = new List<ProjectRouteHint>
        {
            new("GET", "/health", "Health check")
        };

        if (projectKind is "web")
        {
            routes.Add(new ProjectRouteHint("GET", "/", "App root"));
            return routes;
        }

        // Prefer OpenAPI/Swagger on disk (from Swagger skill) — includes example bodies.
        var fromOpenApi = TryLoadRoutesFromOpenApi(projectFullRoot);
        if (fromOpenApi.Count > 0)
        {
            foreach (var route in fromOpenApi)
            {
                if (routes.Count >= 16)
                {
                    break;
                }

                if (routes.Any(r =>
                        r.Method.Equals(route.Method, StringComparison.OrdinalIgnoreCase)
                        && r.Path.Equals(route.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                routes.Add(route);
            }

            return routes;
        }

        // Lightweight content scan for common REST paths (best-effort).
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/health" };
        try
        {
            foreach (var file in Directory.EnumerateFiles(projectFullRoot, "*.*", SearchOption.AllDirectories)
                         .Where(f => IsSourceFile(f))
                         .Take(80))
            {
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch
                {
                    continue;
                }

                if (text.Length > 200_000)
                {
                    continue;
                }

                foreach (Match match in RouteLiteral().Matches(text))
                {
                    var path = match.Groups[1].Value;
                    if (path.Length is < 2 or > 80 || !found.Add(path))
                    {
                        continue;
                    }

                    var method = GuessMethodForPath(path, text);
                    routes.Add(new ProjectRouteHint(method, path, $"{method} {path}"));
                    if (routes.Count >= 12)
                    {
                        return routes;
                    }
                }
            }
        }
        catch
        {
            // ignore scan errors
        }

        // Sensible defaults for notebook-style APIs when nothing was found.
        if (routes.Count == 1)
        {
            routes.Add(new ProjectRouteHint("GET", "/notes", "List notes"));
            routes.Add(new ProjectRouteHint(
                "POST",
                "/notes",
                "Create note",
                """{"title":"demo","body":"hello from workspace tester"}"""));
        }

        return routes;
    }

    /// <summary>
    /// Loads operations from openapi.json / swagger.json written into the project
    /// (Swagger / OpenAPI skill). Best-effort; invalid files are ignored.
    /// </summary>
    public static IReadOnlyList<ProjectRouteHint> TryLoadRoutesFromOpenApi(string projectFullRoot)
    {
        var candidates = new[]
        {
            Path.Combine(projectFullRoot, "openapi.json"),
            Path.Combine(projectFullRoot, "swagger.json"),
            Path.Combine(projectFullRoot, "docs", "openapi.json"),
            Path.Combine(projectFullRoot, "swagger", "v1", "swagger.json")
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("paths", out var paths)
                    || paths.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var routes = new List<ProjectRouteHint>();
                foreach (var pathProp in paths.EnumerateObject())
                {
                    var routePath = pathProp.Name;
                    if (!routePath.StartsWith('/'))
                    {
                        routePath = "/" + routePath;
                    }

                    foreach (var op in pathProp.Value.EnumerateObject())
                    {
                        var method = op.Name.ToUpperInvariant();
                        if (method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE"))
                        {
                            continue;
                        }

                        var summary = op.Value.TryGetProperty("summary", out var sum)
                            && sum.ValueKind == JsonValueKind.String
                                ? sum.GetString()
                                : null;
                        var label = string.IsNullOrWhiteSpace(summary)
                            ? $"{method} {routePath}"
                            : summary!;
                        var example = ExtractOpenApiExampleBody(op.Value);
                        routes.Add(new ProjectRouteHint(method, routePath, label, example));
                        if (routes.Count >= 20)
                        {
                            return routes;
                        }
                    }
                }

                if (routes.Count > 0)
                {
                    return routes;
                }
            }
            catch
            {
                // try next candidate
            }
        }

        return [];
    }

    private static string? ExtractOpenApiExampleBody(JsonElement operation)
    {
        try
        {
            if (!operation.TryGetProperty("requestBody", out var body)
                || !body.TryGetProperty("content", out var content))
            {
                return null;
            }

            foreach (var media in content.EnumerateObject())
            {
                if (!media.Name.Contains("json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (media.Value.TryGetProperty("example", out var example))
                {
                    return example.GetRawText();
                }

                if (media.Value.TryGetProperty("examples", out var examples)
                    && examples.ValueKind == JsonValueKind.Object)
                {
                    foreach (var ex in examples.EnumerateObject())
                    {
                        if (ex.Value.TryGetProperty("value", out var value))
                        {
                            return value.GetRawText();
                        }
                    }
                }

                if (media.Value.TryGetProperty("schema", out var schema)
                    && schema.TryGetProperty("example", out var schemaExample))
                {
                    return schemaExample.GetRawText();
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public static bool IsAllowedProxyTarget(Uri uri, int portRangeStart, int portRangeSize)
    {
        if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.Host;
        if (!string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            && host is not "127.0.0.1" and not "::1")
        {
            return false;
        }

        var port = uri.IsDefaultPort
            ? (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80)
            : uri.Port;

        return port >= portRangeStart && port < portRangeStart + Math.Max(1, portRangeSize);
    }

    private static bool LooksLikeWeb(string root)
    {
        if (File.Exists(Path.Combine(root, "index.html"))
            || File.Exists(Path.Combine(root, "public", "index.html"))
            || File.Exists(Path.Combine(root, "src", "index.html")))
        {
            return true;
        }

        var packageJson = Path.Combine(root, "package.json");
        if (File.Exists(packageJson))
        {
            try
            {
                var text = File.ReadAllText(packageJson);
                if (text.Contains("\"vite\"", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("\"react\"", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("\"@angular/core\"", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("\"next\"", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("\"vue\"", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // ignore
            }
        }

        return Directory.Exists(Path.Combine(root, "src", "app"))
            && File.Exists(Path.Combine(root, "angular.json"));
    }

    private static bool LooksLikeApi(string root)
    {
        var markers = new[]
        {
            Path.Combine(root, "app", "main.py"),
            Path.Combine(root, "main.py"),
            Path.Combine(root, "main.go"),
            Path.Combine(root, "cmd", "server", "main.go"),
            Path.Combine(root, "openapi.json"),
            Path.Combine(root, "swagger.json")
        };

        if (markers.Any(File.Exists))
        {
            return true;
        }

        // FastAPI / Express / Gin cues in requirements or go.mod / package.json
        foreach (var name in new[] { "requirements.txt", "go.mod", "package.json", "Program.cs" })
        {
            var path = Path.Combine(root, name);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var text = File.ReadAllText(path);
                if (text.Contains("fastapi", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("express", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("gin-gonic", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("uvicorn", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // ignore
            }
        }

        return false;
    }

    private static bool IsSourceFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".py" or ".go" or ".ts" or ".js" or ".cs" or ".java" or ".rb" or ".rs";
    }

    private static string GuessMethodForPath(string path, string surrounding)
    {
        var lower = surrounding.ToLowerInvariant();
        var idx = lower.IndexOf(path.ToLowerInvariant(), StringComparison.Ordinal);
        var window = idx >= 0
            ? lower[Math.Max(0, idx - 80)..Math.Min(lower.Length, idx + path.Length + 40)]
            : lower;

        if (window.Contains("post") || window.Contains("create") || window.Contains("@app.post"))
        {
            return "POST";
        }

        if (window.Contains("put") || window.Contains("@app.put"))
        {
            return "PUT";
        }

        if (window.Contains("delete") || window.Contains("@app.delete"))
        {
            return "DELETE";
        }

        if (window.Contains("patch") || window.Contains("@app.patch"))
        {
            return "PATCH";
        }

        return "GET";
    }

    private static string? FindComposeFileName(string dir)
    {
        foreach (var name in ComposeNames)
        {
            if (File.Exists(Path.Combine(dir, name)))
            {
                return name;
            }
        }

        return null;
    }

    private static string ShellQuote(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;

    [GeneratedRegex(@"[^A-Za-z0-9]+")]
    private static partial Regex ProjectSlug();

    // Matches "/foo", "/foo/{id}", "/api/v1/items" style literals in source.
    [GeneratedRegex(@"""(/(?:[A-Za-z0-9_\-{}]+/?)+)""")]
    private static partial Regex RouteLiteral();
}

public sealed record ProjectLayout(
    string FullRoot,
    string RelativeRoot,
    bool HasDockerfile,
    bool HasCompose,
    string? ComposeFileName);
