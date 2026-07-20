using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OmniAgentConsole.Application.Runtime;
using StackExchange.Redis;

namespace OmniAgentConsole.Infrastructure.Runtime;

/// <summary>
/// Worker-side counterpart of <see cref="RedisTaskCancellationBroadcast"/>:
/// listens for cancel messages published by the API process and cancels the
/// matching task's local cancellation token, aborting the in-flight model call.
/// </summary>
public sealed class RedisTaskCancelSubscriber : BackgroundService
{
    private readonly IConnectionMultiplexer redis;
    private readonly ITaskCancellationRegistry cancellationRegistry;
    private readonly ILogger<RedisTaskCancelSubscriber> logger;

    public RedisTaskCancelSubscriber(
        IConnectionMultiplexer redis,
        ITaskCancellationRegistry cancellationRegistry,
        ILogger<RedisTaskCancelSubscriber> logger)
    {
        this.redis = redis;
        this.cancellationRegistry = cancellationRegistry;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisTaskCancellationBroadcast.ChannelName),
            (_, message) =>
            {
                if (!Guid.TryParse(message.ToString(), out var taskRunId))
                {
                    return;
                }

                var cancelled = cancellationRegistry.Cancel(taskRunId);
                logger.LogInformation(
                    "Received cross-process cancel for task {TaskRunId}: {Result}.",
                    taskRunId,
                    cancelled ? "local token cancelled" : "no local token (task not running here)");
            });

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            try
            {
                await subscriber.UnsubscribeAsync(RedisChannel.Literal(RedisTaskCancellationBroadcast.ChannelName));
            }
            catch
            {
            }
        }
    }
}
