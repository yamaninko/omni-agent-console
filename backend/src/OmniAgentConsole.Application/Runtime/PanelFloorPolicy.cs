using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniAgentConsole.Application.Runtime;

/// <summary>
/// Resolves speaking order. Fixed = roster order (default).
/// Llm = parse a moderator-suggested order from free text / JSON names.
/// </summary>
public static class PanelFloorPolicy
{
    public const string Fixed = "fixed";
    public const string Llm = "llm";

    public static string NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return Fixed;
        var m = mode.Trim().ToLowerInvariant();
        return m is Llm or "moderator" or "dynamic" ? Llm : Fixed;
    }

    /// <summary>
    /// Reorders <paramref name="remaining"/> using names mentioned in moderator text.
    /// Unmentioned speakers keep original relative order at the end.
    /// </summary>
    public static IReadOnlyList<T> ApplyModeratorOrder<T>(
        IReadOnlyList<T> remaining,
        Func<T, string> displayName,
        string? moderatorSuggestion)
    {
        if (remaining.Count <= 1 || string.IsNullOrWhiteSpace(moderatorSuggestion))
        {
            return remaining;
        }

        var names = remaining.Select(displayName).ToList();
        var picked = new List<T>();
        var used = new HashSet<int>();

        // Prefer JSON array of strings if present.
        foreach (var token in ExtractNameTokens(moderatorSuggestion!))
        {
            for (var i = 0; i < names.Count; i++)
            {
                if (used.Contains(i)) continue;
                if (names[i].Equals(token, StringComparison.OrdinalIgnoreCase)
                    || names[i].Contains(token, StringComparison.OrdinalIgnoreCase)
                    || token.Contains(names[i], StringComparison.OrdinalIgnoreCase))
                {
                    picked.Add(remaining[i]);
                    used.Add(i);
                    break;
                }
            }
        }

        for (var i = 0; i < remaining.Count; i++)
        {
            if (!used.Contains(i))
            {
                picked.Add(remaining[i]);
            }
        }

        return picked;
    }

    private static IEnumerable<string> ExtractNameTokens(string text)
    {
        var list = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        var s = el.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                    }
                }

                return list;
            }
        }
        catch (JsonException)
        {
            // fall through to line/regex parse
        }

        foreach (Match m in Regex.Matches(text, @"[""']([^""']{2,40})[""']"))
        {
            list.Add(m.Groups[1].Value.Trim());
        }

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cleaned = Regex.Replace(line, @"^[\d\.\)\-\*]+\s*", "");
            if (cleaned.Length is >= 2 and <= 40)
            {
                list.Add(cleaned);
            }
        }

        return list;
    }
}
