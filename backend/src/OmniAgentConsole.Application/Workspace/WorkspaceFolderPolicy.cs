using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OmniAgentConsole.Application.Runtime;

namespace OmniAgentConsole.Application.Workspace;

/// <summary>
/// Validates project folder names and relative import paths for
/// POST /api/workspace/folders and /import (browser folder upload).
/// Pure rules — no I/O.
/// </summary>
public static class WorkspaceFolderPolicy
{
    public const int MaxProjectNameLength = 64;
    public const int MaxFilesPerImport = 500;
    public const long MaxBytesPerFile = 2 * 1024 * 1024; // 2 MiB
    public const long MaxTotalBytes = 50 * 1024 * 1024; // 50 MiB
    public const int MaxRelativePathLength = 400;

    private static readonly Regex ProjectNamePattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> SkippedTopLevelNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules",
        ".git",
        ".svn",
        ".hg",
        "bin",
        "obj",
        "dist",
        "build",
        "out",
        "target",
        "vendor",
        "__pycache__",
        ".venv",
        "venv",
        ".tox",
        ".mypy_cache",
        ".pytest_cache",
        ".next",
        ".nuxt",
        ".angular",
        "coverage",
        ".idea",
        ".vs",
        ".vscode"
    };

    public static bool TryNormalizeProjectName(string? raw, out string name, out string error)
    {
        name = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Project name is required.";
            return false;
        }

        var trimmed = raw.Trim().Replace('\\', '/').Trim('/');
        if (trimmed.Contains('/') || trimmed.Contains("..", StringComparison.Ordinal))
        {
            error = "Project name cannot contain path separators or '..'.";
            return false;
        }

        if (trimmed.Length > MaxProjectNameLength || !ProjectNamePattern.IsMatch(trimmed))
        {
            error = "Project name must be 1–64 chars: letters, digits, '.', '_' or '-' (start with letter/digit).";
            return false;
        }

        name = trimmed;
        return true;
    }

    /// <summary>
    /// Normalizes a browser relative path (e.g. from webkitRelativePath) to a path
    /// under the project root (no leading project folder segment required).
    /// Returns false when the path should be rejected or skipped as junk.
    /// </summary>
    public static bool TryNormalizeImportRelativePath(
        string? rawRelativePath,
        string projectName,
        out string relativeUnderProject,
        out string? skipReason)
    {
        relativeUnderProject = string.Empty;
        skipReason = null;

        if (string.IsNullOrWhiteSpace(rawRelativePath))
        {
            skipReason = "empty path";
            return false;
        }

        var raw = rawRelativePath.Replace('\\', '/').Trim();
        if (raw.Length == 0 || raw.Length > MaxRelativePathLength)
        {
            skipReason = "invalid path length";
            return false;
        }

        // Reject absolute / rooted paths before stripping leading slashes.
        if (Path.IsPathRooted(raw) || raw.StartsWith('/') || raw.Contains(':', StringComparison.Ordinal))
        {
            skipReason = "path escape";
            return false;
        }

        var normalized = raw.TrimStart('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            skipReason = "empty segments";
            return false;
        }

        // webkitRelativePath is usually "FolderName/src/file.ts" — drop the root folder
        // when it matches the project name (case-insensitive).
        if (segments.Length > 1
            && segments[0].Equals(projectName, StringComparison.OrdinalIgnoreCase))
        {
            segments = segments.Skip(1).ToArray();
        }

        if (segments.Length == 0)
        {
            skipReason = "project root only";
            return false;
        }

        foreach (var seg in segments)
        {
            if (seg is "." or ".." || seg.Length == 0 || seg.Contains("..", StringComparison.Ordinal))
            {
                skipReason = "bad segment";
                return false;
            }

            if (SkippedTopLevelNames.Contains(seg))
            {
                skipReason = $"skipped directory '{seg}'";
                return false;
            }
        }

        relativeUnderProject = string.Join('/', segments);
        return true;
    }

    public static bool ShouldRejectFileSize(long length, out string error)
    {
        if (length < 0 || length > MaxBytesPerFile)
        {
            error = $"Each file must be ≤ {MaxBytesPerFile / (1024 * 1024)} MiB.";
            return true;
        }

        error = string.Empty;
        return false;
    }

    public static bool IsSkippedDirectoryName(string name) =>
        !string.IsNullOrEmpty(name) && SkippedTopLevelNames.Contains(name);

    /// <summary>
    /// Copies <paramref name="sourceDir"/> into <paramref name="destDir"/> (must not exist yet),
    /// skipping junk directories. Confines destination under <paramref name="workspaceRoot"/>.
    /// </summary>
    public static (int filesWritten, long bytesWritten, int dirsSkipped) CopyDirectoryIntoWorkspace(
        string workspaceRoot,
        string projectName,
        string sourceDir,
        string destDir)
    {
        if (!Directory.Exists(sourceDir))
        {
            throw new DirectoryNotFoundException($"Source not found: {sourceDir}");
        }

        if (!WorkspacePathGuard.TryResolve(workspaceRoot, projectName, out var resolvedDest)
            || !string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(destDir)),
                Path.TrimEndingDirectorySeparator(resolvedDest),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Destination is outside workspace.");
        }

        if (Directory.Exists(destDir) || File.Exists(destDir))
        {
            throw new InvalidOperationException($"Destination already exists: {projectName}");
        }

        Directory.CreateDirectory(destDir);

        var filesWritten = 0;
        long bytesWritten = 0;
        var dirsSkipped = 0;

        void Walk(string src, string dstRel)
        {
            foreach (var dir in Directory.GetDirectories(src))
            {
                var name = Path.GetFileName(dir);
                if (IsSkippedDirectoryName(name))
                {
                    dirsSkipped++;
                    continue;
                }

                var childRel = string.IsNullOrEmpty(dstRel) ? name : $"{dstRel}/{name}";
                if (!WorkspacePathGuard.TryResolve(workspaceRoot, $"{projectName}/{childRel}", out var childFull))
                {
                    continue;
                }

                Directory.CreateDirectory(childFull);
                Walk(dir, childRel);
            }

            foreach (var file in Directory.GetFiles(src))
            {
                var name = Path.GetFileName(file);
                var childRel = string.IsNullOrEmpty(dstRel) ? name : $"{dstRel}/{name}";
                if (!WorkspacePathGuard.TryResolve(workspaceRoot, $"{projectName}/{childRel}", out var childFull))
                {
                    continue;
                }

                var info = new FileInfo(file);
                if (info.Length > MaxBytesPerFile)
                {
                    // Skip oversized files rather than aborting the whole tree.
                    continue;
                }

                if (bytesWritten + info.Length > MaxTotalBytes)
                {
                    throw new InvalidOperationException(
                        $"Copy exceeds {MaxTotalBytes / (1024 * 1024)} MiB total under project '{projectName}'.");
                }

                var parent = Path.GetDirectoryName(childFull);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                File.Copy(file, childFull, overwrite: false);
                filesWritten++;
                bytesWritten += info.Length;

                if (filesWritten > MaxFilesPerImport)
                {
                    throw new InvalidOperationException(
                        $"Copy exceeds {MaxFilesPerImport} files. Exclude node_modules/build artifacts.");
                }
            }
        }

        try
        {
            Walk(sourceDir, "");
        }
        catch
        {
            try
            {
                if (Directory.Exists(destDir))
                {
                    Directory.Delete(destDir, true);
                }
            }
            catch
            {
                // best effort cleanup
            }

            throw;
        }

        if (filesWritten == 0)
        {
            try
            {
                if (Directory.Exists(destDir))
                {
                    Directory.Delete(destDir, true);
                }
            }
            catch
            {
                // best effort
            }

            throw new InvalidOperationException("No files copied (empty source or only skipped folders).");
        }

        return (filesWritten, bytesWritten, dirsSkipped);
    }
}
