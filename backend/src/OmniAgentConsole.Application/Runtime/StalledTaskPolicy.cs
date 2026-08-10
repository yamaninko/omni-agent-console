using OmniAgentConsole.Application.Configuration;

namespace OmniAgentConsole.Application.Runtime;

public enum StalledTaskAction
{
    /// <summary>The run is progressing (or slow but still within budget).</summary>
    None = 0,

    /// <summary>Console has been silent long enough to tell the user something is wrong.</summary>
    Warn = 1,

    /// <summary>Silence persisted; finalize the run so the console stops spinning.</summary>
    Fail = 2
}

/// <summary>
/// Decides what to do with a Running task based on how long its console has been
/// silent. Pure so the thresholds stay verifiable without a broker or database.
/// </summary>
public static class StalledTaskPolicy
{
    public static StalledTaskAction Evaluate(
        DateTimeOffset lastActivityAt,
        DateTimeOffset now,
        bool executingLocally,
        TaskWatchdogOptions options)
    {
        // A task owned by this process is alive by definition; its own cancellation
        // and timeout paths finalize it, so the watchdog must not race them.
        if (executingLocally)
        {
            return StalledTaskAction.None;
        }

        var silence = now - lastActivityAt;
        if (silence >= TimeSpan.FromMinutes(options.StallFailureMinutes))
        {
            return StalledTaskAction.Fail;
        }

        return silence >= TimeSpan.FromMinutes(options.StallWarningMinutes)
            ? StalledTaskAction.Warn
            : StalledTaskAction.None;
    }
}
