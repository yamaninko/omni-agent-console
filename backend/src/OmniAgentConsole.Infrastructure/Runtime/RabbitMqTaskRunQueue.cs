using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniAgentConsole.Application.Configuration;
using OmniAgentConsole.Application.Runtime;
using RabbitMQ.Client;

namespace OmniAgentConsole.Infrastructure.Runtime;

public sealed class RabbitMqTaskRunQueue : ITaskRunQueue, IAsyncDisposable
{
    /// <summary>
    /// Queue args left empty for backward compatibility with existing brokers.
    /// Message Priority is still set on publish (effective only if the queue was
    /// declared with x-max-priority). In-memory queue always prefers panels.
    /// </summary>
    private static readonly Dictionary<string, object?> QueueArguments = new();

    /// <summary>
    /// Hard ceiling for a single broker round-trip. A broker restart can leave an
    /// in-flight BasicGet/publish continuation pending forever, which used to wedge
    /// the worker's dequeue loop silently (no logs, no consumers, tasks stuck in
    /// Running). Abandoning the call and reconnecting is always recoverable because
    /// unacked messages are requeued when the channel dies.
    /// </summary>
    private static readonly TimeSpan BrokerOperationTimeout = TimeSpan.FromSeconds(30);

    private readonly TaskQueueOptions options;
    private readonly string connectionString;
    private readonly ILogger<RabbitMqTaskRunQueue> logger;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private IConnection? connection;
    private IChannel? channel;

    public RabbitMqTaskRunQueue(
        IOptions<TaskQueueOptions> options,
        IConfiguration configuration,
        ILogger<RabbitMqTaskRunQueue> logger)
    {
        this.options = options.Value;
        this.connectionString = configuration.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException("RabbitMQ connection string is required when TaskQueue:Mode is RabbitMq.");
        this.logger = logger;
    }

    public ValueTask EnqueueAsync(Guid taskRunId, CancellationToken cancellationToken)
        => EnqueueCoreAsync(taskRunId, QueuedWorkKind.TaskRun, cancellationToken);

    public ValueTask EnqueuePanelAsync(Guid panelSessionId, CancellationToken cancellationToken)
        => EnqueueCoreAsync(panelSessionId, QueuedWorkKind.PanelSession, cancellationToken);

    private async ValueTask EnqueueCoreAsync(Guid workId, QueuedWorkKind kind, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(workId.ToString("D"));
        var type = kind == QueuedWorkKind.PanelSession ? "panel-session" : "task-run";
        var properties = new BasicProperties
        {
            ContentType = "text/plain",
            MessageId = workId.ToString("D"),
            Persistent = true,
            Type = type,
            // Panels = 8, Studio tasks = 3 (requires x-max-priority on the queue).
            Priority = kind == QueuedWorkKind.PanelSession ? (byte)8 : (byte)3
        };

        // A cached channel can look open while the broker has already gone away,
        // so one reconnect-and-retry keeps enqueue from failing the API request.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var activeChannel = await EnsureChannelAsync(cancellationToken);
                await WithBrokerTimeoutAsync(
                    token => activeChannel.BasicPublishAsync(
                        exchange: string.Empty,
                        routingKey: options.QueueName,
                        mandatory: true,
                        basicProperties: properties,
                        body: body,
                        cancellationToken: token).AsTask(),
                    $"publish for {type} {workId}",
                    cancellationToken);
                return;
            }
            catch (Exception exception) when (attempt == 1 && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    exception,
                    "Enqueue of {WorkKind} {WorkId} failed; reconnecting and retrying once.",
                    type,
                    workId);
                await InvalidateChannelAsync();
            }
        }
    }

    public async ValueTask<QueueMessage> DequeueAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            IChannel activeChannel;
            BasicGetResult? result;
            try
            {
                activeChannel = await EnsureChannelAsync(cancellationToken);
                result = await WithBrokerTimeoutAsync(
                    token => activeChannel.BasicGetAsync(options.QueueName, autoAck: false, token),
                    $"BasicGet on '{options.QueueName}'",
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Never let a dead or hung broker connection stall the loop: drop the
                // cached channel so the next iteration reconnects from scratch.
                logger.LogWarning(exception, "Task queue poll failed; dropping the connection and reconnecting.");
                await InvalidateChannelAsync();
                await Task.Delay(options.PollIntervalMilliseconds, cancellationToken);
                continue;
            }

            if (result is null)
            {
                await Task.Delay(options.PollIntervalMilliseconds, cancellationToken);
                continue;
            }

            var payload = Encoding.UTF8.GetString(result.Body.Span);
            if (Guid.TryParse(payload, out var workId))
            {
                // Delivery tags are scoped to the channel that delivered the message.
                // Ack/nack must go through that exact channel; a reconnected channel
                // would treat the old tag as unknown. If the delivery channel died,
                // the broker has already requeued the message, so doing nothing is
                // the correct (at-least-once) outcome.
                var kind = string.Equals(result.BasicProperties?.Type, "panel-session", StringComparison.OrdinalIgnoreCase)
                    ? QueuedWorkKind.PanelSession
                    : QueuedWorkKind.TaskRun;
                var deliveryChannel = activeChannel;
                return new QueueMessage(workId, Redelivered: result.Redelivered, Kind: kind, AcknowledgeAsync: async success =>
                {
                    if (!deliveryChannel.IsOpen)
                    {
                        logger.LogWarning(
                            "Queue channel for work {WorkId} closed before {Outcome}; the broker will redeliver the message.",
                            workId,
                            success ? "ACK" : "NACK");
                        return;
                    }

                    if (success)
                    {
                        await deliveryChannel.BasicAckAsync(result.DeliveryTag, multiple: false);
                    }
                    else
                    {
                        await deliveryChannel.BasicNackAsync(result.DeliveryTag, multiple: false, requeue: true);
                    }
                });
            }

            logger.LogWarning("RabbitMQ task queue received an invalid task id payload: {Payload}", payload);
            await activeChannel.BasicAckAsync(result.DeliveryTag, multiple: false);
        }

        throw new OperationCanceledException(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (channel is not null)
        {
            await channel.DisposeAsync();
        }

        if (connection is not null)
        {
            await connection.DisposeAsync();
        }

        initializationLock.Dispose();
    }

    /// <summary>
    /// Runs a broker call under <see cref="BrokerOperationTimeout"/> without trusting the
    /// client to honour the cancellation token — a continuation orphaned by a broker
    /// restart never completes, so the call is abandoned rather than awaited forever.
    /// </summary>
    private static async Task<T> WithBrokerTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string description,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var operationTask = operation(timeoutSource.Token);
        var timeoutTask = Task.Delay(BrokerOperationTimeout, timeoutSource.Token);

        var finished = await Task.WhenAny(operationTask, timeoutTask);
        if (finished == operationTask)
        {
            timeoutSource.Cancel();
            return await operationTask;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Abandon the orphaned call; observe its outcome so it cannot surface later
        // as an unobserved task exception once the channel is disposed.
        timeoutSource.Cancel();
        _ = operationTask.ContinueWith(
            static abandoned => _ = abandoned.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        throw new TimeoutException(
            $"RabbitMQ {description} did not complete within {BrokerOperationTimeout.TotalSeconds:0}s.");
    }

    private async Task WithBrokerTimeoutAsync(
        Func<CancellationToken, Task> operation,
        string description,
        CancellationToken cancellationToken)
    {
        await WithBrokerTimeoutAsync<object?>(
            async token =>
            {
                await operation(token);
                return null;
            },
            description,
            cancellationToken);
    }

    /// <summary>
    /// Forces the next <see cref="EnsureChannelAsync"/> to build a fresh connection.
    /// Unacked deliveries on the discarded channel are requeued by the broker.
    /// </summary>
    private async Task InvalidateChannelAsync()
    {
        await initializationLock.WaitAsync(CancellationToken.None);
        try
        {
            if (channel is not null)
            {
                try
                {
                    await channel.DisposeAsync();
                }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "Discarding a broken RabbitMQ channel failed.");
                }

                channel = null;
            }

            if (connection is not null)
            {
                try
                {
                    await connection.DisposeAsync();
                }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "Discarding a broken RabbitMQ connection failed.");
                }

                connection = null;
            }
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (connection?.IsOpen == true && channel?.IsOpen == true)
        {
            return channel;
        }

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (connection?.IsOpen == true && channel?.IsOpen == true)
            {
                return channel;
            }

            if (channel is not null)
            {
                await channel.DisposeAsync();
                channel = null;
            }

            if (connection is not null)
            {
                await connection.DisposeAsync();
                connection = null;
            }

            var factory = new ConnectionFactory
            {
                Uri = new Uri(connectionString),
                ClientProvidedName = "omniagent-console-api",
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                // Fail pending RPCs instead of waiting on a broker that went away.
                ContinuationTimeout = BrokerOperationTimeout
            };

            connection = await factory.CreateConnectionAsync(cancellationToken);
            channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await channel.QueueDeclareAsync(
                queue: options.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: QueueArguments,
                passive: false,
                noWait: false,
                cancellationToken: cancellationToken);
            await channel.BasicQosAsync(0, 1, false, cancellationToken);

            return channel;
        }
        finally
        {
            initializationLock.Release();
        }
    }
}
