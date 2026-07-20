using System.Text.RegularExpressions;

namespace OmniAgentConsole.Application.Runtime;

/// <summary>
/// Decides whether a single post-Reviewer Coder pass should run.
/// Pure heuristics — no I/O. Limit is always one fix loop per task (enforced by the orchestrator).
/// </summary>
public static partial class ReviewerFixLoopPolicy
{
    private static readonly string[] ClearNoFindingPhrases =
    [
        "no findings",
        "no issues",
        "no problems",
        "no concerns",
        "looks good",
        "lgtm",
        "approved",
        "all good",
        "nothing to fix",
        "no changes needed",
        "no corrections",
        "bulgu yok",
        "sorun yok",
        "düzeltme gerekmiyor",
        "her şey yolunda"
    ];

    private static readonly string[] FindingMarkers =
    [
        "critical",
        "high severity",
        "medium severity",
        "low severity",
        "severity:",
        "must fix",
        "should fix",
        "security issue",
        "security risk",
        "vulnerability",
        "sql injection",
        "xss",
        "hardcoded secret",
        "hardcoded password",
        "bug:",
        "issue:",
        "finding:",
        "problem:",
        "risk:",
        "incorrect",
        "insecure",
        "broken",
        "fix required",
        "fix:",
        "öneri:",
        "bulgu:",
        "bulgu ",
        "hata:",
        "eksik ",
        "güvenlik açığı",
        "güvenlik risk"
    ];

    /// <summary>
    /// Returns true when the Reviewer output appears to contain actionable findings
    /// (not an empty or pure-approval note).
    /// </summary>
    public static bool ShouldRunFixLoop(string? reviewerOutput)
    {
        if (string.IsNullOrWhiteSpace(reviewerOutput))
        {
            return false;
        }

        var normalized = reviewerOutput.Trim();
        if (normalized.Length < 40)
        {
            // Too short to be a real review with prioritized findings.
            return false;
        }

        var lower = normalized.ToLowerInvariant();

        // Explicit clean bill of health → skip (even if the text is long).
        foreach (var phrase in ClearNoFindingPhrases)
        {
            if (lower.Contains(phrase, StringComparison.Ordinal))
            {
                // If the same review also lists severity findings, still run.
                if (!ContainsFindingMarker(lower) && !NumberedOrBulletList().IsMatch(normalized))
                {
                    return false;
                }
            }
        }

        if (ContainsFindingMarker(lower))
        {
            return true;
        }

        // Structured lists of issues without keyword hits still count.
        if (NumberedOrBulletList().IsMatch(normalized) && normalized.Length >= 80)
        {
            return true;
        }

        return false;
    }

    public static string BuildFixLoopObjective(string reviewerOutput)
    {
        var trimmed = reviewerOutput.Trim();
        if (trimmed.Length > 8000)
        {
            trimmed = trimmed[..8000] + "\n…[truncated]";
        }

        return
            """
            FIX LOOP (single pass): Apply ONLY the Reviewer findings below.
            - Use write_file / read_file / list_files to patch the existing workspace.
            - Do not expand scope, do not rewrite unrelated files, do not add scratch/check scripts.
            - When the concrete findings are addressed, reply with a short plain-text summary of what you changed (no code blocks).

            Reviewer findings to fix:
            """ + trimmed;
    }

    private static bool ContainsFindingMarker(string lower)
    {
        foreach (var marker in FindingMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"(?m)^\s*(?:[-*•]|\d+[.)])\s+\S+", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedOrBulletList();
}
