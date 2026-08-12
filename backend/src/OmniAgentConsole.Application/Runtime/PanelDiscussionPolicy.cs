using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Application.Runtime;

/// <summary>
/// Pure rules for moderated single-round panel discussions: speaking order,
/// roster briefing, stance/mission prompts, and fail-forward policy.
/// </summary>
public static class PanelDiscussionPolicy
{
    public const int DefaultTimeoutSeconds = 60;
    public const int DefaultMaxTokens = 800;
    public const int MinMembers = 1;
    public const int MaxMembersPerGroup = 20;

    /// <summary>One real seat on the panel (never invent names outside this list).</summary>
    public sealed record RosterEntry(
        string DisplayName,
        PanelMemberRole Role,
        PanelStance Stance,
        string? StanceLabel,
        string MissionSummary);

    /// <summary>
    /// Speaking order: Moderators first (by SortOrder), then Commentators (by SortOrder),
    /// then name / id as stable tie-breakers.
    /// </summary>
    public static IReadOnlyList<T> OrderSpeakers<T>(
        IEnumerable<T> members,
        Func<T, bool> isEnabled,
        Func<T, PanelMemberRole> role,
        Func<T, int> sortOrder,
        Func<T, string> displayName,
        Func<T, Guid> id)
    {
        return members
            .Where(isEnabled)
            .OrderBy(m => role(m) == PanelMemberRole.Moderator ? 0 : 1)
            .ThenBy(sortOrder)
            .ThenBy(displayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(id)
            .ToList();
    }

    /// <summary>Legacy overload used by older tests — treats everyone as commentator.</summary>
    public static IReadOnlyList<T> OrderSpeakers<T>(
        IEnumerable<T> members,
        Func<T, bool> isEnabled,
        Func<T, int> sortOrder,
        Func<T, string> displayName,
        Func<T, Guid> id)
    {
        return OrderSpeakers(
            members,
            isEnabled,
            _ => PanelMemberRole.Commentator,
            sortOrder,
            displayName,
            id);
    }

    public static bool CanStart(int enabledMemberCount) => enabledMemberCount >= MinMembers;

    /// <summary>
    /// MVP fail policy A: a failed guest turn does not abort the session;
    /// the next speaker still gets the floor.
    /// </summary>
    public static bool ContinueAfterTurnFailure => true;

    public static string DescribeStance(PanelStance stance, string? stanceLabel)
    {
        var label = string.IsNullOrWhiteSpace(stanceLabel) ? null : stanceLabel.Trim();
        return stance switch
        {
            PanelStance.For => label is null
                ? "FOR (pro / affirmative side of the topic)"
                : $"FOR — you defend: {label}",
            PanelStance.Against => label is null
                ? "AGAINST (con / opposing side of the topic)"
                : $"AGAINST — you defend: {label}",
            PanelStance.Custom => label is null
                ? "a custom position (state your thesis clearly)"
                : $"CUSTOM position — you defend: {label}",
            _ => label is null
                ? "NEUTRAL (no forced side; stay fair and balanced)"
                : $"NEUTRAL — framing: {label}"
        };
    }

    /// <summary>
    /// Short mission line for roster cards — persona first line + stance.
    /// </summary>
    public static string BuildMissionSummary(
        string displayName,
        PanelMemberRole role,
        PanelStance stance,
        string? stanceLabel,
        string systemPrompt)
    {
        var persona = FirstSentence(systemPrompt);
        var roleWord = role == PanelMemberRole.Moderator ? "Moderator" : "Commentator";
        var stanceText = DescribeStance(stance, stanceLabel);
        if (string.IsNullOrWhiteSpace(persona))
        {
            return $"{displayName} ({roleWord}) — {stanceText}";
        }

        return $"{displayName} ({roleWord}) — {persona} | Stance: {stanceText}";
    }

    /// <summary>
    /// Human-readable roster shown in the console before anyone speaks.
    /// </summary>
    public static string BuildRosterBriefing(string topic, IReadOnlyList<RosterEntry> roster)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Panel briefing — who is on stage and what they must do");
        sb.AppendLine($"Topic: {topic.Trim()}");
        sb.AppendLine();
        sb.AppendLine("Roster (only these people exist — do not invent other guests):");
        var n = 1;
        foreach (var seat in roster)
        {
            var role = seat.Role == PanelMemberRole.Moderator ? "Moderator" : "Commentator";
            sb.AppendLine($"{n}. {seat.DisplayName} · {role}");
            sb.AppendLine($"   Mission: {seat.MissionSummary}");
            sb.AppendLine($"   Stance: {DescribeStance(seat.Stance, seat.StanceLabel)}");
            n++;
        }

        sb.AppendLine();
        sb.AppendLine("Format: single round — each seat speaks once in the order above.");
        return sb.ToString().TrimEnd();
    }

    public static string FormatRosterForPrompt(IReadOnlyList<RosterEntry> roster)
    {
        var sb = new StringBuilder();
        sb.AppendLine("OFFICIAL ROSTER (use only these names; never invent Dr. X or other guests):");
        var i = 1;
        foreach (var seat in roster)
        {
            var role = seat.Role == PanelMemberRole.Moderator ? "Moderator" : "Commentator";
            sb.AppendLine($"{i}. {seat.DisplayName} [{role}]");
            sb.AppendLine($"   Stance: {DescribeStance(seat.Stance, seat.StanceLabel)}");
            sb.AppendLine($"   Mission: {seat.MissionSummary}");
            i++;
        }

        return sb.ToString().TrimEnd();
    }

    public static string BuildMemberSystemPrompt(
        string personaSystemPrompt,
        PanelMemberRole role,
        PanelStance stance,
        string? stanceLabel,
        string displayName,
        IReadOnlyList<RosterEntry>? roster = null)
    {
        var persona = string.IsNullOrWhiteSpace(personaSystemPrompt)
            ? (role == PanelMemberRole.Moderator
                ? "You are a skilled panel moderator."
                : "You are a thoughtful panel commentator.")
            : personaSystemPrompt.Trim();

        var stanceLine = DescribeStance(stance, stanceLabel);
        var rosterBlock = roster is { Count: > 0 }
            ? FormatRosterForPrompt(roster)
            : string.Empty;

        if (role == PanelMemberRole.Moderator)
        {
            return $"""
                {persona}

                You are {displayName}, the MODERATOR of this panel.
                Your stance framing: {stanceLine}

                YOUR JOB (mission):
                1. Restate the user's topic clearly (do not change the topic).
                2. Introduce ONLY the real roster below by name, role, and their assigned stance/mission.
                3. Explain the format: one round, each person gets ~1 minute, automatic floor order.
                4. Stay impartial unless your stance says otherwise; do not argue for a side harder than the guests.

                HARD RULES:
                - NEVER invent guests, experts, or names that are not on the roster (no "Dr. Ömer", no fictional co-panelists).
                - If a guest's stance label seems written for another debate (e.g. remote work) but the topic is different, introduce them by name and say they will apply their assigned lens to TODAY's topic — do not pretend the panel is still about remote work.
                - Keep the opening short (~150–250 words / ~1 minute spoken).
                - No stage directions, no code, no "as an AI".

                {rosterBlock}
                """;
        }

        return $"""
            {persona}

            You are {displayName}, a COMMENTATOR on this panel.
            Your assigned stance: {stanceLine}

            YOUR JOB (mission):
            1. Speak only about the panel topic given in the user message (the real subject of this session).
            2. Stay in character as your persona above.
            3. Defend your assigned stance as it applies to THIS topic:
               - If the stance label matches the topic, argue that thesis directly.
               - If the stance label was written for a different subject, map it: take the same side-of-debate energy (pro / con / custom) and argue a coherent position ON THE ACTUAL TOPIC. Briefly state how you interpret your stance for this topic, then argue it.
            4. Engage prior speakers by name when relevant; agree with allies, challenge opponents.

            HARD RULES:
            - Do not hijack the show into an unrelated debate (e.g. remote work) unless the topic itself is about that.
            - Do not invent people who are not on the roster.
            - ~150–250 words / ~1 minute spoken. No stage directions, no code, no "as an AI".

            {rosterBlock}
            """;
    }

    public static string BuildMemberUserPrompt(
        string topic,
        PanelMemberRole role,
        string displayName,
        PanelStance stance,
        string? stanceLabel,
        IReadOnlyList<RosterEntry> roster,
        IReadOnlyList<(string Speaker, string Content)> priorTurns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== THIS SESSION ===");
        sb.AppendLine($"Panel topic (the only subject you may discuss):");
        sb.AppendLine(topic.Trim());
        sb.AppendLine();
        sb.AppendLine($"You are speaking as: {displayName}");
        sb.AppendLine($"Your role: {(role == PanelMemberRole.Moderator ? "Moderator" : "Commentator")}");
        sb.AppendLine($"Your stance: {DescribeStance(stance, stanceLabel)}");
        sb.AppendLine();
        sb.AppendLine(FormatRosterForPrompt(roster));
        sb.AppendLine();

        if (priorTurns.Count == 0)
        {
            if (role == PanelMemberRole.Moderator)
            {
                sb.AppendLine(
                    "You speak first. Open the panel: welcome, restate the topic, introduce each roster member by name + role + stance/mission, then hand the floor to the speaking order.");
            }
            else
            {
                sb.AppendLine(
                    "You are the first commentator. Give your opening from your stance applied to the topic above.");
            }
        }
        else
        {
            sb.AppendLine("Prior turns (respond to these; stay on topic):");
            foreach (var (speaker, content) in priorTurns)
            {
                sb.AppendLine($"[{speaker}]");
                sb.AppendLine(content.Trim());
                sb.AppendLine();
            }

            if (role == PanelMemberRole.Moderator)
            {
                sb.AppendLine("Your turn as moderator: keep people on topic; brief reaction only.");
            }
            else
            {
                sb.AppendLine(
                    "Your turn as commentator: answer the topic and earlier speakers from your assigned stance/mission.");
            }
        }

        return sb.ToString();
    }

    // ── Backward-compatible aliases ─────────────────────────────────────

    public static string BuildGuestSystemPrompt(string personaSystemPrompt)
        => BuildMemberSystemPrompt(
            personaSystemPrompt,
            PanelMemberRole.Commentator,
            PanelStance.Neutral,
            null,
            "Guest");

    public static string BuildGuestUserPrompt(string topic, IReadOnlyList<(string Speaker, string Content)> priorTurns)
        => BuildMemberUserPrompt(
            topic,
            PanelMemberRole.Commentator,
            "Guest",
            PanelStance.Neutral,
            null,
            Array.Empty<RosterEntry>(),
            priorTurns);

    public static string BuildMemberSystemPrompt(
        string personaSystemPrompt,
        PanelMemberRole role,
        PanelStance stance,
        string? stanceLabel)
        => BuildMemberSystemPrompt(personaSystemPrompt, role, stance, stanceLabel, role == PanelMemberRole.Moderator ? "Moderator" : "Commentator");

    public static string BuildMemberUserPrompt(
        string topic,
        PanelMemberRole role,
        IReadOnlyList<(string Speaker, string Content)> priorTurns)
        => BuildMemberUserPrompt(
            topic,
            role,
            role == PanelMemberRole.Moderator ? "Moderator" : "Commentator",
            PanelStance.Neutral,
            null,
            Array.Empty<RosterEntry>(),
            priorTurns);

    private static string FirstSentence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var t = text.Trim().Replace('\n', ' ');
        while (t.Contains("  ", StringComparison.Ordinal))
        {
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        }

        var cut = t.IndexOfAny(['.', '!', '?']);
        if (cut is > 0 and < 180)
        {
            return t[..(cut + 1)].Trim();
        }

        return t.Length <= 160 ? t : t[..157].TrimEnd() + "…";
    }
}
