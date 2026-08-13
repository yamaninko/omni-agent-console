using System.Text;

namespace OmniAgentConsole.Application.Panels;

/// <summary>
/// Builds an extractive "score card" from finished panel turns + vote tallies
/// (no extra LLM call — cheap, deterministic, demo-friendly).
/// </summary>
public static class PanelScorecardBuilder
{
    public sealed record Scorecard(
        string Title,
        string Topic,
        string Status,
        IReadOnlyList<(string Name, int Turns, int Chars)> Speakers,
        IReadOnlyList<PanelVoteTallyDto> Votes,
        string ClosingBlurb,
        string Markdown);

    public static Scorecard Build(
        string title,
        string topic,
        string status,
        IReadOnlyList<(string DisplayName, string? Output)> completedTurns,
        IReadOnlyList<PanelVoteTallyDto> votes)
    {
        var speakers = completedTurns
            .GroupBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => (
                Name: g.Key,
                Turns: g.Count(),
                Chars: g.Sum(x => x.Output?.Length ?? 0)))
            .OrderByDescending(x => x.Chars)
            .ToList();

        var topVote = votes.OrderByDescending(v => v.Votes).FirstOrDefault();
        var strongest = speakers.FirstOrDefault();

        var blurb = new StringBuilder();
        blurb.Append($"Debate on “{Truncate(topic, 120)}” finished with status {status}. ");
        if (speakers.Count > 0)
        {
            blurb.Append($"{speakers.Count} speaker(s) produced {completedTurns.Count} turn(s). ");
        }

        if (topVote is not null && topVote.Votes > 0)
        {
            blurb.Append($"Audience lead: {topVote.DisplayName} ({topVote.Votes} vote(s)). ");
        }

        if (!string.IsNullOrEmpty(strongest.Name))
        {
            blurb.Append($"Longest contribution: {strongest.Name} (~{strongest.Chars} chars).");
        }

        var md = new StringBuilder();
        md.AppendLine($"# Score card: {title}");
        md.AppendLine();
        md.AppendLine($"**Topic:** {topic}");
        md.AppendLine($"**Status:** {status}");
        md.AppendLine();
        md.AppendLine("## Speakers");
        foreach (var s in speakers)
        {
            md.AppendLine($"- **{s.Name}** — {s.Turns} turn(s), ~{s.Chars} chars");
        }

        md.AppendLine();
        md.AppendLine("## Audience votes");
        if (votes.Count == 0)
        {
            md.AppendLine("_No votes yet._");
        }
        else
        {
            foreach (var v in votes.OrderByDescending(x => x.Votes))
            {
                md.AppendLine($"- **{v.DisplayName}**: {v.Votes}");
            }
        }

        md.AppendLine();
        md.AppendLine("## Closing");
        md.AppendLine(blurb.ToString());

        return new Scorecard(title, topic, status, speakers, votes, blurb.ToString(), md.ToString());
    }

    private static string Truncate(string s, int max)
    {
        var one = s.Replace('\n', ' ').Trim();
        return one.Length <= max ? one : one[..(max - 1)] + "…";
    }
}
