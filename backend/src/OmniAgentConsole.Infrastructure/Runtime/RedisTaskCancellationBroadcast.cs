using OmniAgentConsole.Application.Runtime;
using StackExchange.Redis;

namespace OmniAgentConsole.Infrastructure.Runtime;

public sealed class RedisTaskCancellationBroadcast : ITaskCancellationBroadcast
{
    public const string ChannelName = "task-cancellations";

    private readonly IDatabase database;

    public RedisTaskCancellationBroadcast(IConnectionMultiplexer redis)
    {
        database = redis.GetDatabase();
    }

    public async Task PublishCancelAsync(Guid taskRunId, CancellationToken cancellationToken)
    {
        await database.PublishAsync(RedisChannel.Literal(ChannelName), taskRunId.ToString());
    }
}

public sealed class NullTaskCancellationBroadcast : ITaskCancellationBroadcast
{
    public Task PublishCancelAsync(Guid taskRunId, CancellationToken cancellationToken) => Task.CompletedTask;
}
