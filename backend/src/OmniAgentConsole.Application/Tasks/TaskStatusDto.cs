using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Application.Tasks;

/// <summary>
/// Lightweight snapshot for Studio status polling. Avoids loading agent
/// inputs/outputs, console event payloads, and model call rows on every tick.
/// </summary>
public sealed record TaskStatusDto(
    Guid Id,
    string Title,
    TaskRunStatus Status,
    DateTimeOffset? CompletedAt,
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalTokens,
    long TotalLatencyMs,
    string? ErrorMessage);
