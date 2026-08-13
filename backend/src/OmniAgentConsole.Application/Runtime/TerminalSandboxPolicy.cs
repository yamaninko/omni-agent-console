using System.Text.RegularExpressions;

namespace OmniAgentConsole.Application.Runtime;

/// <summary>
/// Allows only a tight whitelist of test/lint commands for the Coder run_terminal tool.
/// Rejects shell metacharacters and anything that is not an exact pattern match.
/// </summary>
public static class TerminalSandboxPolicy
{
    public const int MaxCommandLength = 240;
    public const int DefaultTimeoutSeconds = 90;
    public const int MaxTimeoutSeconds = 120;
    public const int MaxOutputChars = 16_000;

    private static readonly Regex[] Allowed =
    [
        new(@"^pytest(\s+[A-Za-z0-9_./\-]+)*$", RegexOptions.Compiled),
        new(@"^python\s+-m\s+pytest(\s+[A-Za-z0-9_./\-]+)*$", RegexOptions.Compiled),
        new(@"^python3\s+-m\s+pytest(\s+[A-Za-z0-9_./\-]+)*$", RegexOptions.Compiled),
        new(@"^npm\s+test$", RegexOptions.Compiled),
        new(@"^npm\s+run\s+test$", RegexOptions.Compiled),
        new(@"^dotnet\s+test(\s+[A-Za-z0-9_./\-]+)*$", RegexOptions.Compiled),
        new(@"^go\s+test(\s+[A-Za-z0-9_./\-]+)*$", RegexOptions.Compiled),
        new(@"^go\s+test\s+\./\.\.\.$", RegexOptions.Compiled),
        new(@"^ruff\s+check(\s+[A-Za-z0-9_./\-]+)*$", RegexOptions.Compiled),
        new(@"^tsc\s+--noEmit$", RegexOptions.Compiled)
    ];

    private static readonly char[] Forbidden =
        ['|', '&', ';', '>', '<', '`', '$', '(', ')', '{', '}', '\n', '\r', '\t'];

    public static bool IsAllowed(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var cmd = command.Trim();
        if (cmd.Length > MaxCommandLength)
        {
            return false;
        }

        if (cmd.IndexOfAny(Forbidden) >= 0)
        {
            return false;
        }

        // Block path traversal, but allow Go's package pattern "./..."
        if (HasDisallowedDots(cmd))
        {
            return false;
        }

        return Allowed.Any(rx => rx.IsMatch(cmd));
    }

    public static string? RejectReason(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return "Command is empty.";
        }

        var cmd = command.Trim();
        if (cmd.Length > MaxCommandLength)
        {
            return $"Command exceeds {MaxCommandLength} characters.";
        }

        if (cmd.IndexOfAny(Forbidden) >= 0)
        {
            return "Command contains forbidden shell metacharacters.";
        }

        if (HasDisallowedDots(cmd))
        {
            return "Parent-path segments are not allowed.";
        }

        if (!IsAllowed(cmd))
        {
            return "Command not on the allow-list (pytest / npm test / dotnet test / go test / ruff / tsc --noEmit).";
        }

        return null;
    }

    private static bool HasDisallowedDots(string cmd)
    {
        var stripped = cmd.Replace("./...", "", StringComparison.Ordinal);
        return stripped.Contains("..", StringComparison.Ordinal);
    }
}
