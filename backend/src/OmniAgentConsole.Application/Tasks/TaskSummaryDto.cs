using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Application.Tasks;

public sealed record TaskSummaryDto(
    Guid Id,
    string Title,
    TaskRunStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int TotalTokens,
    long TotalLatencyMs);
