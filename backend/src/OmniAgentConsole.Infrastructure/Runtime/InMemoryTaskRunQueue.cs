using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using OmniAgentConsole.Application.Runtime;

namespace OmniAgentConsole.Infrastructure.Runtime;

public sealed class InMemoryTaskRunQueue : ITaskRunQueue
{
    private readonly Channel<Guid> queue = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(Guid taskRunId, CancellationToken cancellationToken)
    {
        return queue.Writer.WriteAsync(taskRunId, cancellationToken);
    }

    public async ValueTask<QueueMessage> DequeueAsync(CancellationToken cancellationToken)
    {
        var taskRunId = await queue.Reader.ReadAsync(cancellationToken);
        return new QueueMessage(taskRunId, _ => Task.CompletedTask);
    }
}
