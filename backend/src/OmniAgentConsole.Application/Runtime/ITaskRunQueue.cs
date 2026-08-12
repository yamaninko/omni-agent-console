using System;
using System.Threading;
using System.Threading.Tasks;

namespace OmniAgentConsole.Application.Runtime;

public enum QueuedWorkKind
{
    TaskRun = 0,
    PanelSession = 1
}

public sealed record QueueMessage(
    Guid WorkId,
    Func<bool, Task> AcknowledgeAsync,
    bool Redelivered = false,
    QueuedWorkKind Kind = QueuedWorkKind.TaskRun)
{
    /// <summary>Backward-compatible alias for <see cref="WorkId"/> when Kind is TaskRun.</summary>
    public Guid TaskRunId => WorkId;
}

public interface ITaskRunQueue
{
    ValueTask EnqueueAsync(Guid taskRunId, CancellationToken cancellationToken);
    ValueTask EnqueuePanelAsync(Guid panelSessionId, CancellationToken cancellationToken);
    ValueTask<QueueMessage> DequeueAsync(CancellationToken cancellationToken);
}
