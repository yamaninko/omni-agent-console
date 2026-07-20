namespace OmniAgentConsole.Application.Usage;

public sealed record UsageSummaryDto(
    int TotalRequests,
    decimal SuccessRate,
    long AverageLatencyMs,
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalTokens,
    int ErrorCount,
    int ActiveTaskCount);
