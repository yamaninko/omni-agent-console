namespace OmniAgentConsole.Application.Realtime;

public sealed record ConsoleEventEnvelope(
    Guid Id,
    Guid TaskRunId,
    Guid? AgentRunId,
    string EventType,
    string Message,
    string? PayloadJson,
    DateTimeOffset CreatedAt);
