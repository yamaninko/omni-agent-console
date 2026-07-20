using System;
using System.Threading;
using System.Threading.Tasks;

namespace OmniAgentConsole.Application.Runtime;

public sealed record QueueMessage(Guid TaskRunId, Func<bool, Task> AcknowledgeAsync, bool Redelivered = false);

public interface ITaskRunQueue
{
    ValueTask EnqueueAsync(Guid taskRunId, CancellationToken cancellationToken);
    ValueTask<QueueMessage> DequeueAsync(CancellationToken cancellationToken);
}
