using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OmniAgentConsole.Api.Middleware;
using OmniAgentConsole.Application.Configuration;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Api.Hubs;

public sealed class ConsoleHub : Hub
{
    private readonly AgentConsoleDbContext dbContext;
    private readonly SharedLabOptions sharedLab;

    public ConsoleHub(AgentConsoleDbContext dbContext, IOptions<SharedLabOptions> sharedLab)
    {
        this.dbContext = dbContext;
        this.sharedLab = sharedLab.Value;
    }

    public static string TaskGroup(Guid taskRunId) => $"task:{taskRunId:N}";

    public async Task SubscribeTask(Guid taskRunId)
    {
        // Shared-lab: only the owning session (or the instructor) may listen.
        // Stream ids may be TaskRun or PanelSession — both use the same group naming.
        if (sharedLab.Enabled)
        {
            var httpContext = Context.GetHttpContext();
            var isAdmin = httpContext is not null && SharedLabHttp.IsAdmin(httpContext);
            if (!isAdmin)
            {
                var sessionId = httpContext is null ? null : SharedLabHttp.GetSessionId(httpContext);
                var owner = await dbContext.TaskRuns
                    .AsNoTracking()
                    .Where(x => x.Id == taskRunId)
                    .Select(x => x.OwnerSessionId)
                    .FirstOrDefaultAsync();

                if (owner is null)
                {
                    owner = await dbContext.PanelSessions
                        .AsNoTracking()
                        .Where(x => x.Id == taskRunId)
                        .Select(x => x.OwnerSessionId)
                        .FirstOrDefaultAsync();
                }

                // Null owner means the row was not found (or legacy/unscoped).
                var found = await dbContext.TaskRuns.AsNoTracking().AnyAsync(x => x.Id == taskRunId)
                    || await dbContext.PanelSessions.AsNoTracking().AnyAsync(x => x.Id == taskRunId);

                if (!found || sessionId is null || !string.Equals(owner, sessionId, StringComparison.Ordinal))
                {
                    throw new HubException("Task not found.");
                }
            }
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, TaskGroup(taskRunId));
    }

    public Task UnsubscribeTask(Guid taskRunId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, TaskGroup(taskRunId));
    }
}
