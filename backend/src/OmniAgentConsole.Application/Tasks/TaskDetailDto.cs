using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Application.Tasks;

public sealed record TaskDetailDto(
    Guid Id,
    string Title,
    string InputPrompt,
    string? InputContextJson,
    TaskRunStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalTokens,
    long TotalLatencyMs,
    string? ErrorMessage,
    IReadOnlyList<AgentRunDetailDto> AgentRuns,
    IReadOnlyList<ModelCallLogDetailDto> ModelCallLogs,
    IReadOnlyList<ConsoleEventDto> ConsoleEvents);

public sealed record AgentRunDetailDto(
    Guid Id,
    string AgentName,
    AgentType AgentType,
    AgentRunStatus Status,
    string? Input,
    string? Output,
    int ExecutionOrder,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    long LatencyMs,
    string? ErrorMessage);

public sealed record ModelCallLogDetailDto(
    Guid Id,
    Guid AgentRunId,
    string AgentName,
    ProviderType Provider,
    string Model,
    ModelRequestType RequestType,
    int InputTokens,
    int OutputTokens,
    int TotalTokens,
    long LatencyMs,
    ModelCallStatus Status,
    ProviderErrorCode ErrorCode,
    string? ErrorMessage,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    decimal EstimatedCost);

public sealed record ConsoleEventDto(
    Guid Id,
    Guid TaskRunId,
    Guid? AgentRunId,
    ConsoleEventType EventType,
    string Message,
    string? PayloadJson,
    DateTimeOffset CreatedAt);
