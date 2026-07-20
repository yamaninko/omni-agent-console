using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using OmniAgentConsole.Application.Realtime;

namespace OmniAgentConsole.Infrastructure.Runtime;

public sealed class RedisConsoleEventPublisher : IConsoleEventPublisher
{
    private readonly IDatabase database;
    private readonly string channelName;

    public RedisConsoleEventPublisher(IConnectionMultiplexer redis)
    {
        database = redis.GetDatabase();
        channelName = "console-events";
    }

    public async Task PublishAsync(ConsoleEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(envelope);
        await database.PublishAsync(RedisChannel.Literal(channelName), json);
    }
}
