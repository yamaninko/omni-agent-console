namespace OmniAgentConsole.Application.Configuration;

/// <summary>
/// Guards against a task sitting in Running forever with no feedback (seen when a
/// broker restart orphaned the queue message: the console just showed a spinner).
/// </summary>
public sealed class TaskWatchdogOptions
{
    public const string SectionName = "TaskWatchdog";

    public bool Enabled { get; set; } = true;

    /// <summary>How often stalled tasks are looked for.</summary>
    public int CheckIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Console silence after which a Running task is reported as stalled. Must stay
    /// above the slowest agent timeout (Coder is 300s) plus provider queue time,
    /// otherwise a healthy-but-slow model call would be flagged.
    /// </summary>
    public int StallWarningMinutes { get; set; } = 10;

    /// <summary>
    /// Additional silence after the warning before the run is finalized as Failed so
    /// the user gets a terminal state they can rerun or continue from.
    /// </summary>
    public int StallFailureMinutes { get; set; } = 20;
}
