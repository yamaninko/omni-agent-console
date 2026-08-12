using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using OmniAgentConsole.Application.Runtime;

namespace OmniAgentConsole.Infrastructure.Runtime;

public sealed class InMemoryTaskRunQueue : ITaskRunQueue
{
    private readonly Channel<(Guid Id, QueuedWorkKind Kind)> queue = Channel.CreateUnbounded<(Guid, QueuedWorkKind)>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(Guid taskRunId, CancellationToken cancellationToken)
    {
        return queue.Writer.WriteAsync((taskRunId, QueuedWorkKind.TaskRun), cancellationToken);
    }

    public ValueTask EnqueuePanelAsync(Guid panelSessionId, CancellationToken cancellationToken)
    {
        return queue.Writer.WriteAsync((panelSessionId, QueuedWorkKind.PanelSession), cancellationToken);
    }

    public async ValueTask<QueueMessage> DequeueAsync(CancellationToken cancellationToken)
    {
        var (id, kind) = await queue.Reader.ReadAsync(cancellationToken);
        return new QueueMessage(id, _ => Task.CompletedTask, Kind: kind);
    }
}
