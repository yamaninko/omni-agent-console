using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using OmniAgentConsole.Application.Runtime;

namespace OmniAgentConsole.Infrastructure.Runtime;

public sealed class InMemoryTaskRunQueue : ITaskRunQueue
{
    // Panels prefer a separate channel so short debates are not stuck behind long Studio tasks.
    private readonly Channel<(Guid Id, QueuedWorkKind Kind)> panels = Channel.CreateUnbounded<(Guid, QueuedWorkKind)>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Channel<(Guid Id, QueuedWorkKind Kind)> tasks = Channel.CreateUnbounded<(Guid, QueuedWorkKind)>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ValueTask EnqueueAsync(Guid taskRunId, CancellationToken cancellationToken)
        => tasks.Writer.WriteAsync((taskRunId, QueuedWorkKind.TaskRun), cancellationToken);

    public ValueTask EnqueuePanelAsync(Guid panelSessionId, CancellationToken cancellationToken)
        => panels.Writer.WriteAsync((panelSessionId, QueuedWorkKind.PanelSession), cancellationToken);

    public async ValueTask<QueueMessage> DequeueAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (panels.Reader.TryRead(out var panelItem))
            {
                return new QueueMessage(panelItem.Id, _ => Task.CompletedTask, Kind: panelItem.Kind);
            }

            if (tasks.Reader.TryRead(out var taskItem))
            {
                return new QueueMessage(taskItem.Id, _ => Task.CompletedTask, Kind: taskItem.Kind);
            }

            var panelWait = panels.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var taskWait = tasks.Reader.WaitToReadAsync(cancellationToken).AsTask();
            await Task.WhenAny(panelWait, taskWait);
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new OperationCanceledException(cancellationToken);
    }
}
