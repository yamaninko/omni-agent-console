using System.Text.Json;

namespace OmniAgentConsole.Application.Panels;

/// <summary>
/// Parses and mutates panel audience vote tallies stored as JSON
/// <c>{ "memberId": count, ... }</c> on <c>PanelSession.VotesJson</c>.
/// </summary>
public static class PanelVoteStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static Dictionary<Guid, int> Parse(string? votesJson)
    {
        var result = new Dictionary<Guid, int>();
        if (string.IsNullOrWhiteSpace(votesJson))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(votesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!Guid.TryParse(prop.Name, out var memberId))
                {
                    continue;
                }

                var count = prop.Value.ValueKind switch
                {
                    JsonValueKind.Number when prop.Value.TryGetInt32(out var n) => Math.Max(0, n),
                    JsonValueKind.String when int.TryParse(prop.Value.GetString(), out var n) => Math.Max(0, n),
                    _ => 0
                };
                if (count > 0)
                {
                    result[memberId] = count;
                }
            }
        }
        catch (JsonException)
        {
            // Corrupt blob → empty tallies (fail open for reads).
        }

        return result;
    }

    public static string Serialize(IReadOnlyDictionary<Guid, int> tallies)
    {
        var obj = new Dictionary<string, int>();
        foreach (var (id, count) in tallies.Where(kv => kv.Value > 0))
        {
            obj[id.ToString("D")] = count;
        }

        return JsonSerializer.Serialize(obj, JsonOptions);
    }

    /// <summary>Increments the vote for <paramref name="memberId"/> and returns the new map.</summary>
    public static Dictionary<Guid, int> Cast(string? votesJson, Guid memberId)
    {
        var map = Parse(votesJson);
        map[memberId] = map.TryGetValue(memberId, out var n) ? n + 1 : 1;
        return map;
    }

    public static IReadOnlyList<PanelVoteTallyDto> ToTallies(
        IReadOnlyDictionary<Guid, int> map,
        IReadOnlyDictionary<Guid, string> displayNames)
    {
        return map
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => displayNames.GetValueOrDefault(kv.Key, kv.Key.ToString("D")))
            .Select(kv => new PanelVoteTallyDto(
                kv.Key,
                displayNames.GetValueOrDefault(kv.Key, "Speaker"),
                kv.Value))
            .ToList();
    }
}
