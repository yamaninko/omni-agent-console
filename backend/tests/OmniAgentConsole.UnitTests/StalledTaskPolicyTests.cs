using OmniAgentConsole.Application.Configuration;
using OmniAgentConsole.Application.Runtime;

namespace OmniAgentConsole.UnitTests;

public sealed class StalledTaskPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 13, 0, 0, TimeSpan.Zero);
    private static readonly TaskWatchdogOptions Options = new()
    {
        StallWarningMinutes = 10,
        StallFailureMinutes = 20
    };

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(9)]
    public void Recent_activity_is_not_a_stall(int silentMinutes)
    {
        var action = StalledTaskPolicy.Evaluate(
            Now.AddMinutes(-silentMinutes),
            Now,
            executingLocally: false,
            Options);

        Assert.Equal(StalledTaskAction.None, action);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(19)]
    public void Silence_past_the_warning_threshold_warns(int silentMinutes)
    {
        var action = StalledTaskPolicy.Evaluate(
            Now.AddMinutes(-silentMinutes),
            Now,
            executingLocally: false,
            Options);

        Assert.Equal(StalledTaskAction.Warn, action);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(45)]
    public void Silence_past_the_failure_threshold_finalizes_the_run(int silentMinutes)
    {
        var action = StalledTaskPolicy.Evaluate(
            Now.AddMinutes(-silentMinutes),
            Now,
            executingLocally: false,
            Options);

        Assert.Equal(StalledTaskAction.Fail, action);
    }

    [Fact]
    public void A_task_running_in_this_process_is_never_touched()
    {
        var action = StalledTaskPolicy.Evaluate(
            Now.AddHours(-3),
            Now,
            executingLocally: true,
            Options);

        Assert.Equal(StalledTaskAction.None, action);
    }
}
