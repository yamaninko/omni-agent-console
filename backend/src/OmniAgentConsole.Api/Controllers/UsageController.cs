using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniAgentConsole.Application.Usage;
using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Api.Controllers;

[ApiController]
[Route("api/usage")]
public sealed class UsageController : ControllerBase
{
    private readonly AgentConsoleDbContext dbContext;

    public UsageController(AgentConsoleDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<UsageSummaryDto>> Summary(CancellationToken cancellationToken)
    {
        var totalRequests = await dbContext.ModelCallLogs.CountAsync(cancellationToken);
        var successfulRequests = await dbContext.ModelCallLogs.CountAsync(x => x.Status == ModelCallStatus.Succeeded, cancellationToken);
        var errorCount = await dbContext.ModelCallLogs.CountAsync(x => x.Status == ModelCallStatus.Failed, cancellationToken);
        var activeTaskCount = await dbContext.TaskRuns.CountAsync(x => x.Status == TaskRunStatus.Running, cancellationToken);

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

        var successRate = totalRequests == 0 ? 0 : decimal.Round((decimal)successfulRequests / totalRequests * 100, 2);

        return Ok(new UsageSummaryDto(
            totalRequests,
            successRate,
            (long)(tokenTotals?.AverageLatency ?? 0),
            tokenTotals?.Input ?? 0,
            tokenTotals?.Output ?? 0,
            tokenTotals?.Total ?? 0,
            errorCount,
            activeTaskCount));
    }
}
