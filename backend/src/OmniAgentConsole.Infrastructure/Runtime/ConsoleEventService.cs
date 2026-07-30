using OmniAgentConsole.Application.Realtime;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Infrastructure.Runtime;

public sealed class ConsoleEventService : IConsoleEventService
{
    /// <summary>
    /// Matches <c>console_events.Message</c> varchar(4000). Long Fix-packaging prompts
    /// (and other agent dumps) used to throw Postgres 22001 and poison-drop the task.
    /// </summary>
    public const int MaxMessageLength = 4000;

    private readonly AgentConsoleDbContext dbContext;
    private readonly IConsoleEventPublisher eventPublisher;

    public ConsoleEventService(AgentConsoleDbContext dbContext, IConsoleEventPublisher eventPublisher)
    {
        this.dbContext = dbContext;
        this.eventPublisher = eventPublisher;
    }

    public async Task WriteAsync(
        Guid taskRunId,
        Guid? agentRunId,
        ConsoleEventType eventType,
        string message,
        string? payloadJson,
        CancellationToken cancellationToken)
    {
        var consoleEvent = new ConsoleEvent
        {
            TaskRunId = taskRunId,
            AgentRunId = agentRunId,
            EventType = eventType,
            Message = TruncateMessage(message),
            PayloadJson = payloadJson
        };

        dbContext.ConsoleEvents.Add(consoleEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
        await eventPublisher.PublishAsync(ToEnvelope(consoleEvent), cancellationToken);
    }

    /// <summary>
    /// Clamps console messages to the DB column limit without throwing.
    /// </summary>
    public static string TruncateMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        if (message.Length <= MaxMessageLength)
        {
            return message;
        }

        const string suffix = "…[truncated]";
        var keep = MaxMessageLength - suffix.Length;
        return message[..keep] + suffix;
    }

    private static ConsoleEventEnvelope ToEnvelope(ConsoleEvent consoleEvent)
    {
        return new ConsoleEventEnvelope(
            consoleEvent.Id,
            consoleEvent.TaskRunId,
            consoleEvent.AgentRunId,
            consoleEvent.EventType.ToString(),
            consoleEvent.Message,
            consoleEvent.PayloadJson,
            consoleEvent.CreatedAt);
    }
}
