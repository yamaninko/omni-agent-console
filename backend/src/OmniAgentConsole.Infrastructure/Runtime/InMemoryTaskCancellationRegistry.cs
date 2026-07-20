using System.Collections.Concurrent;
using OmniAgentConsole.Application.Runtime;

namespace OmniAgentConsole.Infrastructure.Runtime;

public sealed class InMemoryTaskCancellationRegistry : ITaskCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> taskTokens = new();

    public CancellationToken CreateToken(Guid taskRunId, CancellationToken parentToken)
    {
        var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        var existing = taskTokens.AddOrUpdate(taskRunId, tokenSource, (_, oldTokenSource) =>
        {
            oldTokenSource.Dispose();
            return tokenSource;
        });

        return existing.Token;
    }

    public bool Cancel(Guid taskRunId)
    {
        if (!taskTokens.TryGetValue(taskRunId, out var tokenSource))
        {
            return false;
        }

        tokenSource.Cancel();
        return true;
    }

    public void Complete(Guid taskRunId)
    {
        if (taskTokens.TryRemove(taskRunId, out var tokenSource))
        {
            tokenSource.Dispose();
        }
    }
}
