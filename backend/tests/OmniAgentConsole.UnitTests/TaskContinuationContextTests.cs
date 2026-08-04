using OmniAgentConsole.Application.Tasks;

namespace OmniAgentConsole.UnitTests;

public sealed class TaskContinuationContextTests
{
    [Fact]
    public void Merge_seeds_history_from_previous_prompt_and_marks_continuation()
    {
        var merged = TaskContinuationContext.Merge(
            """{"workspacePath":"/workspace/demo","skillIds":["11111111-1111-1111-1111-111111111111"]}""",
            "Build a shopping list API",
            "Add JWT auth");

        Assert.True(TaskContinuationContext.IsContinuation(merged));
        var history = TaskContinuationContext.GetPromptHistory(merged);
        Assert.Equal(["Build a shopping list API", "Add JWT auth"], history);
        Assert.Contains("/workspace/demo", merged);
    }

    [Fact]
    public void Merge_appends_to_existing_prompt_history()
    {
        var first = TaskContinuationContext.Merge(null, "Original", "Follow-up 1");
        var second = TaskContinuationContext.Merge(first, "ignored-when-history-exists", "Follow-up 2");

        var history = TaskContinuationContext.GetPromptHistory(second);
        Assert.Equal(["Original", "Follow-up 1", "Follow-up 2"], history);
    }

    [Fact]
    public void IsContinuation_false_for_missing_or_malformed_context()
    {
        Assert.False(TaskContinuationContext.IsContinuation(null));
        Assert.False(TaskContinuationContext.IsContinuation("{not-json"));
        Assert.False(TaskContinuationContext.IsContinuation("""{"workspacePath":"/workspace/x"}"""));
    }
}
