using System.Text.Json;
using System.Text.Json.Nodes;

namespace OmniAgentConsole.Application.Tasks;

/// <summary>
/// Merges follow-up prompts into <see cref="Domain.Entities.TaskRun.InputContextJson"/>
/// so the same task session can continue without wiping console history.
/// </summary>
public static class TaskContinuationContext
{
    public const string IsContinuationProperty = "isContinuation";
    public const string PromptHistoryProperty = "promptHistory";

    public static string Merge(string? existingContextJson, string previousPrompt, string followUpPrompt)
    {
        var context = ParseObject(existingContextJson);
        context[IsContinuationProperty] = true;

        var history = new JsonArray();
        if (context[PromptHistoryProperty] is JsonArray existingHistory)
        {
            foreach (var item in existingHistory)
            {
                if (item is not null)
                {
                    history.Add(item.DeepClone());
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(previousPrompt))
        {
            history.Add(previousPrompt.Trim());
        }

        history.Add(followUpPrompt.Trim());
        context[PromptHistoryProperty] = history;

        return context.ToJsonString();
    }

    public static bool IsContinuation(string? inputContextJson)
    {
        if (string.IsNullOrWhiteSpace(inputContextJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(inputContextJson);
            return doc.RootElement.TryGetProperty(IsContinuationProperty, out var flag)
                   && flag.ValueKind is JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<string> GetPromptHistory(string? inputContextJson)
    {
        if (string.IsNullOrWhiteSpace(inputContextJson))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(inputContextJson);
            if (!doc.RootElement.TryGetProperty(PromptHistoryProperty, out var history)
                || history.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var prompts = new List<string>();
            foreach (var element in history.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        prompts.Add(value);
                    }
                }
            }

            return prompts;
        }
        catch
        {
            return [];
        }
    }

    private static JsonObject ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }
}
