using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniAgentConsole.Application.Dashboard;
using OmniAgentConsole.Application.Tasks;
using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private readonly AgentConsoleDbContext dbContext;

    public DashboardController(AgentConsoleDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    [HttpGet("overview")]
    public async Task<ActionResult<DashboardOverviewDto>> Overview(CancellationToken cancellationToken)
    {
        var totalTasks = await dbContext.TaskRuns.CountAsync(cancellationToken);
        var runningTasks = await dbContext.TaskRuns.CountAsync(x => x.Status == TaskRunStatus.Running, cancellationToken);
        var completedTasks = await dbContext.TaskRuns.CountAsync(x => x.Status == TaskRunStatus.Completed, cancellationToken);
        var failedTasks = await dbContext.TaskRuns.CountAsync(x => x.Status == TaskRunStatus.Failed, cancellationToken);
        var cancelledTasks = await dbContext.TaskRuns.CountAsync(x => x.Status == TaskRunStatus.Cancelled, cancellationToken);

        var totalRequests = await dbContext.ModelCallLogs.CountAsync(cancellationToken);
        var successfulRequests = await dbContext.ModelCallLogs.CountAsync(x => x.Status == ModelCallStatus.Succeeded, cancellationToken);
        var errorCount = await dbContext.ModelCallLogs.CountAsync(x => x.Status == ModelCallStatus.Failed, cancellationToken);

        var tokenTotalRows = await dbContext.ModelCallLogs
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Input = group.Sum(x => x.InputTokens),
                Output = group.Sum(x => x.OutputTokens),
                Total = group.Sum(x => x.TotalTokens),
                AverageLatency = group.Average(x => (double?)x.LatencyMs)
            })
            .ToListAsync(cancellationToken);
        var tokenTotals = tokenTotalRows.SingleOrDefault();

        var agentRows = await dbContext.ModelCallLogs
            .Join(
                dbContext.AgentRuns,
                modelCall => modelCall.AgentRunId,
                agentRun => agentRun.Id,
                (modelCall, agentRun) => new { modelCall, agentRun })
            .GroupBy(x => new { x.agentRun.AgentName, x.agentRun.AgentType })
            .Select(group => new
            {
                group.Key.AgentName,
                group.Key.AgentType,
                Requests = group.Count(),
                Input = group.Sum(x => x.modelCall.InputTokens),
                Output = group.Sum(x => x.modelCall.OutputTokens),
                Total = group.Sum(x => x.modelCall.TotalTokens),
                AverageLatency = group.Average(x => (double?)x.modelCall.LatencyMs),
                Errors = group.Count(x => x.modelCall.Status == ModelCallStatus.Failed)
            })
            .OrderByDescending(x => x.Total)
            .ToListAsync(cancellationToken);

        var modelRows = await dbContext.ModelCallLogs
            .GroupBy(x => new { x.Provider, x.Model })
            .Select(group => new
            {
                group.Key.Provider,
                group.Key.Model,
                Requests = group.Count(),
                Input = group.Sum(x => x.InputTokens),
                Output = group.Sum(x => x.OutputTokens),
                Total = group.Sum(x => x.TotalTokens),
                AverageLatency = group.Average(x => (double?)x.LatencyMs),
                Errors = group.Count(x => x.Status == ModelCallStatus.Failed)
            })
            .OrderByDescending(x => x.Total)
            .ToListAsync(cancellationToken);

        var recentTasks = await dbContext.TaskRuns
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(8)
            .Select(x => new TaskSummaryDto(
                x.Id,
                x.Title,
                x.Status,
                x.CreatedAt,
                x.CompletedAt,
                x.TotalTokens,
                x.TotalLatencyMs))
            .ToListAsync(cancellationToken);

        var successRate = totalRequests == 0 ? 0 : decimal.Round((decimal)successfulRequests / totalRequests * 100, 2);

        var estimatedCost = await dbContext.ModelCallLogs
            .SumAsync(m => m.EstimatedCost ?? 0m, cancellationToken);
        var livePanels = await dbContext.PanelSessions.CountAsync(
            p => p.Status == PanelSessionStatus.Pending || p.Status == PanelSessionStatus.Running,
            cancellationToken);
        var liveTasks = runningTasks + await dbContext.TaskRuns.CountAsync(
            t => t.Status == TaskRunStatus.Pending,
            cancellationToken);

        var liveTaskRows = await dbContext.TaskRuns
            .AsNoTracking()
            .Where(t => t.Status == TaskRunStatus.Pending || t.Status == TaskRunStatus.Running)
            .OrderByDescending(t => t.CreatedAt)
            .Take(20)
            .Select(t => new
            {
                t.Id,
                t.Title,
                Status = t.Status.ToString(),
                t.CreatedAt,
                t.OwnerSessionId,
                t.TotalTokens,
                Cost = t.ModelCallLogs.Sum(m => m.EstimatedCost ?? 0m)
            })
            .ToListAsync(cancellationToken);

        var livePanelRows = await dbContext.PanelSessions
            .AsNoTracking()
            .Where(p => p.Status == PanelSessionStatus.Pending || p.Status == PanelSessionStatus.Running)
            .OrderByDescending(p => p.CreatedAt)
            .Take(20)
            .Select(p => new
            {
                p.Id,
                Title = p.Title,
                Status = p.Status.ToString(),
                p.CreatedAt,
                p.OwnerSessionId,
                p.TotalTokens
            })
            .ToListAsync(cancellationToken);

        var liveSessions = liveTaskRows
            .Select(t => new LiveSessionDto(
                "task",
                t.Id,
                t.Title,
                t.Status,
                t.CreatedAt,
                t.OwnerSessionId,
                t.TotalTokens,
                t.Cost))
            .Concat(livePanelRows.Select(p => new LiveSessionDto(
                "panel",
                p.Id,
                p.Title,
                p.Status,
                p.CreatedAt,
                p.OwnerSessionId,
                p.TotalTokens,
                0m)))
            .OrderByDescending(x => x.CreatedAt)
            .Take(30)
            .ToList();

        return Ok(new DashboardOverviewDto(
            totalTasks,
            runningTasks,
            completedTasks,
            failedTasks,
            cancelledTasks,
            totalRequests,
            successRate,
            (long)(tokenTotals?.AverageLatency ?? 0),
            tokenTotals?.Input ?? 0,
            tokenTotals?.Output ?? 0,
            tokenTotals?.Total ?? 0,
            errorCount,
            agentRows.Select(x => new AgentUsageBreakdownDto(
                x.AgentName,
                x.AgentType.ToString(),
                x.Requests,
                x.Input,
                x.Output,
                x.Total,
                (long)(x.AverageLatency ?? 0),
                x.Errors)).ToList(),
            modelRows.Select(x => new ModelUsageBreakdownDto(
                x.Provider.ToString(),
                x.Model,
                x.Requests,
                x.Input,
                x.Output,
                x.Total,
                (long)(x.AverageLatency ?? 0),
                x.Errors)).ToList(),
            recentTasks,
            estimatedCost,
            livePanels,
            liveTasks,
            liveSessions));
    }

    [HttpGet("recent-tasks")]
    public async Task<IActionResult> RecentTasks(CancellationToken cancellationToken)
    {
        var tasks = await dbContext.TaskRuns
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        return Ok(tasks);
    }
}
