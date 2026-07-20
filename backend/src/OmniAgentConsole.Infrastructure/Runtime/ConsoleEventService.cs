using OmniAgentConsole.Application.Realtime;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Infrastructure.Runtime;

public sealed class ConsoleEventService : IConsoleEventService
{
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
            Message = message,
            PayloadJson = payloadJson
        };

        dbContext.ConsoleEvents.Add(consoleEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
        await eventPublisher.PublishAsync(ToEnvelope(consoleEvent), cancellationToken);
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
