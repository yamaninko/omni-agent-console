using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OmniAgentConsole.Domain.Entities;

namespace OmniAgentConsole.Application.Runtime;

public sealed record SkillSuggestion(IReadOnlyList<Guid> SkillIds, IReadOnlyList<string> Questions);

/// <summary>
/// Keyword-based skill auto-suggestion. Each skill declares comma-separated keywords;
/// a skill is suggested when any keyword appears in the prompt as a whole word.
/// When the prompt leaves the stack or the datastore ambiguous, follow-up questions
/// are returned so the user can refine the prompt (or pick skills manually).
/// </summary>
public static class SkillSuggestionEngine
{
    private const int MinPromptLength = 12;

    private static readonly Regex DatabaseMentionRegex = new(
        @"(?<![\p{L}\p{N}])(veritaban\w*|database|db|sql)(?![\p{L}\p{N}])",
        RegexOptions.Compiled);

    public static SkillSuggestion Suggest(string? prompt, IReadOnlyList<SkillDefinition> enabledSkills)
    {
        var trimmed = prompt?.Trim() ?? string.Empty;
        if (trimmed.Length < MinPromptLength)
        {
            return new SkillSuggestion([], []);
        }

        var haystack = trimmed.ToLowerInvariant();

        var matched = enabledSkills
            .Where(skill => SplitKeywords(skill.Keywords).Any(keyword => MatchesWholeWord(haystack, keyword)))
            .ToList();

        var questions = new List<string>();
        var matchedCategories = matched
            .Select(skill => skill.Category)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!matchedCategories.Contains("Backend") && !matchedCategories.Contains("Frontend"))
        {
            questions.Add("Hangi dil/framework ile yazılsın? (örn. Node.js/TypeScript, Go, Python/FastAPI, .NET, Java Spring, Angular, React, Flutter)");
        }

        if (DatabaseMentionRegex.IsMatch(haystack) && !matchedCategories.Contains("Data"))
        {
            questions.Add("Hangi veritabanı/veri katmanı kullanılacak? (örn. PostgreSQL, MongoDB, Redis)");
        }

        return new SkillSuggestion(
            matched.Select(skill => skill.Id).ToList(),
            questions);
    }

    private static IEnumerable<string> SplitKeywords(string? keywords) =>
        string.IsNullOrWhiteSpace(keywords)
            ? []
            : keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(keyword => keyword.ToLowerInvariant())
                .Where(keyword => keyword.Length >= 2);

    // Letter/number lookarounds instead of \b so "go" does not match inside "django"
    // and Turkish letters count as word characters.
    private static bool MatchesWholeWord(string haystack, string keyword) =>
        Regex.IsMatch(haystack, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(keyword)}(?![\p{{L}}\p{{N}}])");
}
