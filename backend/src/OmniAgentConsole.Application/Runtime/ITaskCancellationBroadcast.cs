namespace OmniAgentConsole.Application.Runtime;

/// <summary>
/// Propagates a task cancellation request across processes. The in-memory
/// cancellation registry is process-local; when the API and the worker run in
/// separate containers the API must broadcast the cancel so the worker can
/// abort the in-flight model call instead of noticing at the next agent boundary.
/// </summary>
public interface ITaskCancellationBroadcast
{
    Task PublishCancelAsync(Guid taskRunId, CancellationToken cancellationToken);
}
