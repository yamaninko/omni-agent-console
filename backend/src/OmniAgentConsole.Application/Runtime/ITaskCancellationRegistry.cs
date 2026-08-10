namespace OmniAgentConsole.Application.Runtime;

public interface ITaskCancellationRegistry
{
    CancellationToken CreateToken(Guid taskRunId, CancellationToken parentToken);
    bool Cancel(Guid taskRunId);
    void Complete(Guid taskRunId);

    /// <summary>
    /// True while this process is executing the task. Used by the stall watchdog to
    /// avoid finalizing a run that is alive locally but quiet.
    /// </summary>
    bool IsExecutingLocally(Guid taskRunId);
}
