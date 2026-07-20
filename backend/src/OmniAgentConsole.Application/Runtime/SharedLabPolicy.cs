using System;
using System.Linq;

namespace OmniAgentConsole.Application.Runtime;

/// <summary>
/// Pure rules for the shared-lab deployment profile: session id validation,
/// session workspace mapping, admin-gated endpoints, and the startup guard.
/// Kept free of ASP.NET dependencies so every rule is unit-testable.
/// </summary>
public static class SharedLabPolicy
{
    public const string SessionHeaderName = "X-Studio-Session-Id";
    public const string SessionQueryName = "session_id";
    public const string SessionsFolder = "sessions";

    /// <summary>
    /// Session ids become workspace folder names, so the charset is restricted
    /// to path-safe characters — no separators, dots, or whitespace.
    /// </summary>
    public static bool IsValidSessionId(string? sessionId)
    {
        return !string.IsNullOrWhiteSpace(sessionId)
            && sessionId.Length is >= 8 and <= 64
            && sessionId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    }

    public static string SessionRoot(string workspaceRoot, string sessionId)
    {
        return $"{workspaceRoot.TrimEnd('/')}/{SessionsFolder}/{sessionId}";
    }

    /// <summary>
    /// Maps a user-supplied workspace path into the caller's session subtree:
    /// "/workspace/foo" and "foo" both become "/workspace/sessions/{sid}/foo".
    /// Already-mapped paths pass through unchanged (idempotent).
    /// </summary>
    public static string MapWorkspacePath(string workspaceRoot, string sessionId, string requestedPath)
    {
        var root = workspaceRoot.TrimEnd('/');
        var relative = requestedPath.Replace('\\', '/').Trim();

        if (relative.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
        {
            relative = relative[(root.Length + 1)..];
        }
        else if (string.Equals(relative, root, StringComparison.OrdinalIgnoreCase))
        {
            relative = string.Empty;
        }

        relative = relative.TrimStart('/');

        var sessionPrefix = $"{SessionsFolder}/{sessionId}";
        if (relative.Equals(sessionPrefix, StringComparison.Ordinal)
            || relative.StartsWith(sessionPrefix + "/", StringComparison.Ordinal))
        {
            return $"{root}/{relative}";
        }

        return string.IsNullOrEmpty(relative)
            ? $"{root}/{sessionPrefix}"
            : $"{root}/{sessionPrefix}/{relative}";
    }

    /// <summary>
    /// True when the request mutates instructor-owned configuration and must be
    /// rejected for non-admin sessions in shared-lab mode. Tasks and workspace
    /// stay open (they are session-scoped); the Studio's skill auto-suggest POST
    /// is explicitly exempt because students depend on it.
    /// </summary>
    public static bool IsAdminGated(string method, string path)
    {
        if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = path.TrimEnd('/').ToLowerInvariant();
        if (normalized == "/api/skills/suggest")
        {
            return false;
        }

        return normalized.StartsWith("/api/credentials", StringComparison.Ordinal)
            || normalized.StartsWith("/api/agents", StringComparison.Ordinal)
            || normalized.StartsWith("/api/settings", StringComparison.Ordinal)
            || normalized.StartsWith("/api/providers", StringComparison.Ordinal)
            || normalized.StartsWith("/api/skills", StringComparison.Ordinal);
    }

    /// <summary>
    /// Fail-fast rule: a shared-lab deployment without an admin key would leave
    /// settings/credentials open to every student — refuse to start instead.
    /// </summary>
    public static void ValidateStartup(bool sharedLabEnabled, string? consoleApiKey)
    {
        if (sharedLabEnabled && string.IsNullOrWhiteSpace(consoleApiKey))
        {
            throw new InvalidOperationException(
                "SHARED_LAB=true requires CONSOLE_API_KEY to be set: the console key is the instructor/admin " +
                "credential in shared-lab mode. Refusing to start an anonymous shared deployment.");
        }
    }
}
