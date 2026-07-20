using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using OmniAgentConsole.Api.Hubs;
using OmniAgentConsole.Application.Realtime;

namespace OmniAgentConsole.Api.Realtime;

public sealed class RedisConsoleEventSubscriber : BackgroundService
{
    private readonly IConnectionMultiplexer redis;
    private readonly IHubContext<ConsoleHub> hubContext;
    private readonly ILogger<RedisConsoleEventSubscriber> logger;
    private readonly string channelName;

    public RedisConsoleEventSubscriber(
        IConnectionMultiplexer redis,
        IHubContext<ConsoleHub> hubContext,
        ILogger<RedisConsoleEventSubscriber> logger)
    {
        this.redis = redis;
        this.hubContext = hubContext;
        this.logger = logger;
        this.channelName = "console-events";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        await subscriber.SubscribeAsync(RedisChannel.Literal(channelName), async (channel, message) =>
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<ConsoleEventEnvelope>(message.ToString(), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (envelope != null)
                {
                    await hubContext.Clients
                        .Group(ConsoleHub.TaskGroup(envelope.TaskRunId))
                        .SendAsync("ReceiveConsoleEvent", envelope, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling Redis console event message.");
            }
        });

        // Keep running until cancellation
        var tcs = new TaskCompletionSource();
        using (stoppingToken.Register(state => ((TaskCompletionSource)state!).TrySetResult(), tcs))
        {
            await tcs.Task;
        }

        await subscriber.UnsubscribeAsync(RedisChannel.Literal(channelName));
    }
}
