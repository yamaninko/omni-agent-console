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
    IReadOnlyList<TaskSummaryDto> RecentTasks);

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
