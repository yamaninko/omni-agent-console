using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

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
}

public sealed record ProjectLayout(
    string FullRoot,
    string RelativeRoot,
    bool HasDockerfile,
    bool HasCompose,
    string? ComposeFileName);
