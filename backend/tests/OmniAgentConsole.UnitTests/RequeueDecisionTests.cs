using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Runtime;
using Xunit;

namespace OmniAgentConsole.UnitTests;

public sealed class RequeueDecisionTests
{
    [Fact]
    public void UserCancelledTask_IsNotRequeued()
    {
        // User cancel: DB status flipped to Cancelled before/while the token fired.
        Assert.False(AgentOrchestratorService.ShouldRequeueAfterCancellation(TaskRunStatus.Cancelled, tokenCancelled: true));
        Assert.False(AgentOrchestratorService.ShouldRequeueAfterCancellation(TaskRunStatus.Cancelled, tokenCancelled: false));
    }

    [Theory]
    [InlineData(TaskRunStatus.Running)]
    [InlineData(TaskRunStatus.Pending)]
    public void HostShutdown_WhileTaskStillActive_IsRequeued(TaskRunStatus status)
    {
        Assert.True(AgentOrchestratorService.ShouldRequeueAfterCancellation(status, tokenCancelled: true));
    }

    [Theory]
    [InlineData(TaskRunStatus.Running)]
    [InlineData(TaskRunStatus.Failed)]
    public void StrayCancellation_WithoutTokenSignal_IsNotRequeued(TaskRunStatus status)
    {
        // e.g. an unexpected TaskCanceledException that is not tied to our token
        // must not create an infinite redelivery loop.
        Assert.False(AgentOrchestratorService.ShouldRequeueAfterCancellation(status, tokenCancelled: false));
    }
}
