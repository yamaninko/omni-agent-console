using OmniAgentConsole.Application.Tasks;

namespace OmniAgentConsole.Application.Dashboard;

public sealed record DashboardOverviewDto(
    int TotalTasks,
    int RunningTasks,
    int CompletedTasks,
    int FailedTasks,
    int CancelledTasks,
    int TotalRequests,
    decimal SuccessRate,
    long AverageLatencyMs,
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalTokens,
    int ErrorCount,
    IReadOnlyList<AgentUsageBreakdownDto> AgentBreakdown,
    IReadOnlyList<ModelUsageBreakdownDto> ModelBreakdown,
    IReadOnlyList<TaskSummaryDto> RecentTasks,
    decimal EstimatedCostTotal = 0m,
    int LivePanelSessions = 0,
    int LiveTaskSessions = 0,
    /// <summary>Instructor view: currently live Studio tasks and panel sessions.</summary>
    IReadOnlyList<LiveSessionDto>? LiveSessions = null);

/// <summary>A running/pending task or panel for the instructor dashboard.</summary>
public sealed record LiveSessionDto(
    string Kind,
    Guid Id,
    string Title,
    string Status,
    DateTimeOffset CreatedAt,
    string? OwnerSessionId,
    int TotalTokens,
    decimal EstimatedCost);

public sealed record AgentUsageBreakdownDto(
    string AgentName,
    string AgentType,
    int Requests,
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalTokens,
    long AverageLatencyMs,
    int ErrorCount);

public sealed record ModelUsageBreakdownDto(
    string Provider,
    string Model,
    int Requests,
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalTokens,
    long AverageLatencyMs,
    int ErrorCount);
