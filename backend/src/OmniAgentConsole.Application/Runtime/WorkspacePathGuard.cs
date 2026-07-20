using System;
using System.IO;

namespace OmniAgentConsole.Application.Runtime;

/// <summary>
/// Confines user- or model-supplied paths to a fixed workspace root.
/// Both the API workspace browser and the worker's code export must route every
/// path through <see cref="TryResolve"/> before touching the filesystem.
/// </summary>
public static class WorkspacePathGuard
{
    public const string DefaultRoot = "/workspace";

    public static string NormalizeRoot(string root) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    /// <summary>
    /// Resolves <paramref name="userPath"/> (absolute or relative) against
    /// <paramref name="root"/>. Returns false when the result would land outside
    /// the root — via "..", an absolute path elsewhere, a sibling directory that
    /// merely shares the root's name prefix, or a symlinked component inside the
    /// workspace that points elsewhere.
    /// </summary>
    public static bool TryResolve(string root, string? userPath, out string fullPath)
    {
        fullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(userPath))
        {
            return false;
        }

        string rootFull;
        string candidate;
        try
        {
            rootFull = NormalizeRoot(root);

            // Backslashes are not separators on Linux, so "..\" would survive
            // Path.GetFullPath untouched; treat them as separators before resolving.
            var normalized = userPath.Replace('\\', '/').Trim();
            if (normalized.Length == 0)
            {
                return false;
            }

            candidate = Path.IsPathRooted(normalized)
                ? Path.GetFullPath(normalized)
                : Path.GetFullPath(Path.Combine(rootFull, normalized));

            candidate = Path.TrimEndingDirectorySeparator(candidate);
        }
        catch (Exception)
        {
            // Invalid characters, overlong paths etc. — treat as unresolvable.
            return false;
        }

        var isInsideRoot = candidate.Equals(rootFull, StringComparison.Ordinal)
            || candidate.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal);

        if (!isInsideRoot || ContainsSymlinkedComponent(rootFull, candidate))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    // The root itself may legitimately be a symlink (e.g. a bind mount), but any
    // symlinked component below it could redirect I/O outside the workspace.
    private static bool ContainsSymlinkedComponent(string rootFull, string candidate)
    {
        if (candidate.Length <= rootFull.Length)
        {
            return false;
        }

        var current = rootFull;
        var relative = candidate[(rootFull.Length + 1)..];

        foreach (var part in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);

            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);

            // LinkTarget first: a dangling symlink reports Exists == false yet still
            // redirects writes to its target.
            if (info.LinkTarget is not null)
            {
                return true;
            }

            if (!info.Exists)
            {
                // Nothing beyond this point exists yet, so nothing can be a symlink.
                return false;
            }
        }

        return false;
    }
}
