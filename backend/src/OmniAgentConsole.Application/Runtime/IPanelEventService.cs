using System;
using System.Threading;
using System.Threading.Tasks;
using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Application.Runtime;

public interface IPanelEventService
{
    Task WriteAsync(
        Guid panelSessionId,
        Guid? panelTurnId,
        ConsoleEventType eventType,
        string message,
        string? payloadJson,
        CancellationToken cancellationToken);
}
