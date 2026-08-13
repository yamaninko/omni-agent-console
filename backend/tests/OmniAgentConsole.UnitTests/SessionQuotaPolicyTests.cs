using OmniAgentConsole.Application.Runtime;

namespace OmniAgentConsole.UnitTests;

public sealed class SessionQuotaPolicyTests
{
    [Theory]
    [InlineData(2, 2, true)]
    [InlineData(1, 2, false)]
    [InlineData(5, 0, false)]
    public void Concurrent(int running, int max, bool over)
        => Assert.Equal(over, SessionQuotaPolicy.IsOverConcurrent(running, max));

    [Theory]
    [InlineData(30, 30, true)]
    [InlineData(29, 30, false)]
    public void DailyTasks(int created, int max, bool over)
        => Assert.Equal(over, SessionQuotaPolicy.IsOverDailyTasks(created, max));

    [Fact]
    public void DailyTokens()
    {
        Assert.True(SessionQuotaPolicy.IsOverDailyTokens(500_000, 500_000));
        Assert.False(SessionQuotaPolicy.IsOverDailyTokens(100, 500_000));
    }
}
