using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.UnitTests;

public sealed class TaskPipelinePolicyTests
{
    [Theory]
    [InlineData(null, "full")]
    [InlineData("", "full")]
    [InlineData("  ", "full")]
    [InlineData("full", "full")]
    [InlineData("FULL", "full")]
    [InlineData("coder", "coder")]
    [InlineData("plan-code-review", "plan-code-review")]
    [InlineData("unknown-pipeline", "full")]
    public void Normalize_MapsKnownAndUnknownKeys(string? input, string expected)
    {
        Assert.Equal(expected, TaskPipelinePolicy.Normalize(input));
    }

    [Fact]
    public void Resolve_Full_IsDefaultChain()
    {
        var pipeline = TaskPipelinePolicy.Resolve(null);
        Assert.Equal(
            new[]
            {
                AgentType.Planner,
                AgentType.Research,
                AgentType.Coder,
                AgentType.Reviewer,
                AgentType.OpsMonitor
            },
            pipeline);
    }

    [Fact]
    public void Resolve_CoderOnly_IsSingleAgent()
    {
        Assert.Equal(new[] { AgentType.Coder }, TaskPipelinePolicy.Resolve("coder"));
    }

    [Fact]
    public void Resolve_PlanCodeReview_SkipsResearchAndOps()
    {
        Assert.Equal(
            new[] { AgentType.Planner, AgentType.Coder, AgentType.Reviewer },
            TaskPipelinePolicy.Resolve("plan-code-review"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("full", true)]
    [InlineData("coder", true)]
    [InlineData("plan-code-review", true)]
    [InlineData("nope", false)]
    public void IsKnown_AcceptsEmptyAndNamedPipelines(string? key, bool expected)
    {
        Assert.Equal(expected, TaskPipelinePolicy.IsKnown(key));
    }
}
