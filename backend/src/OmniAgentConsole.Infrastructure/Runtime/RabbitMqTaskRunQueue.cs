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
    private static readonly Dictionary<string, object?> QueueArguments = new();
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

    public async ValueTask EnqueueAsync(Guid taskRunId, CancellationToken cancellationToken)
    {
        var activeChannel = await EnsureChannelAsync(cancellationToken);
        var body = Encoding.UTF8.GetBytes(taskRunId.ToString("D"));
        var properties = new BasicProperties
        {
            ContentType = "text/plain",
            MessageId = taskRunId.ToString("D"),
            Persistent = true,
            Type = "task-run"
        };

        await activeChannel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: options.QueueName,
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }

    public async ValueTask<QueueMessage> DequeueAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var activeChannel = await EnsureChannelAsync(cancellationToken);
            var result = await activeChannel.BasicGetAsync(options.QueueName, autoAck: false, cancellationToken);
            if (result is null)
            {
                await Task.Delay(options.PollIntervalMilliseconds, cancellationToken);
                continue;
            }

            var payload = Encoding.UTF8.GetString(result.Body.Span);
            if (Guid.TryParse(payload, out var taskRunId))
            {
                // Delivery tags are scoped to the channel that delivered the message.
                // Ack/nack must go through that exact channel; a reconnected channel
                // would treat the old tag as unknown. If the delivery channel died,
                // the broker has already requeued the message, so doing nothing is
                // the correct (at-least-once) outcome.
                var deliveryChannel = activeChannel;
                return new QueueMessage(taskRunId, Redelivered: result.Redelivered, AcknowledgeAsync: async success =>
                {
                    if (!deliveryChannel.IsOpen)
                    {
                        logger.LogWarning(
                            "Queue channel for task {TaskRunId} closed before {Outcome}; the broker will redeliver the message.",
                            taskRunId,
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
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
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
