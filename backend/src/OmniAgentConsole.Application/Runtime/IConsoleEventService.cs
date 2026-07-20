using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Application.Runtime;

public interface IConsoleEventService
{
    Task WriteAsync(
        Guid taskRunId,
        Guid? agentRunId,
        ConsoleEventType eventType,
        string message,
        string? payloadJson,
        CancellationToken cancellationToken);
}
