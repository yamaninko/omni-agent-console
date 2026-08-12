using OmniAgentConsole.Application.Realtime;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Infrastructure.Runtime;

public sealed class PanelEventService : IPanelEventService
{
    private readonly AgentConsoleDbContext dbContext;
    private readonly IConsoleEventPublisher eventPublisher;

    public PanelEventService(AgentConsoleDbContext dbContext, IConsoleEventPublisher eventPublisher)
    {
        this.dbContext = dbContext;
        this.eventPublisher = eventPublisher;
    }

    public async Task WriteAsync(
        Guid panelSessionId,
        Guid? panelTurnId,
        ConsoleEventType eventType,
        string message,
        string? payloadJson,
        CancellationToken cancellationToken)
    {
        var panelEvent = new PanelConsoleEvent
        {
            PanelSessionId = panelSessionId,
            PanelTurnId = panelTurnId,
            EventType = eventType,
            Message = ConsoleEventService.TruncateMessage(message),
            PayloadJson = payloadJson
        };

        dbContext.PanelConsoleEvents.Add(panelEvent);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Reuse the task stream envelope + hub group naming so the frontend
        // ConsoleStreamService can subscribe with the panel session id.
        var envelope = new ConsoleEventEnvelope(
            panelEvent.Id,
            panelSessionId,
            panelTurnId,
            panelEvent.EventType.ToString(),
            panelEvent.Message,
            panelEvent.PayloadJson,
            panelEvent.CreatedAt);

        await eventPublisher.PublishAsync(envelope, cancellationToken);
    }
}
