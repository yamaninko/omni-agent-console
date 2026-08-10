using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniAgentConsole.Application.Configuration;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Infrastructure.Runtime;

/// <summary>
/// Reports (and eventually finalizes) tasks that stay Running with a silent console.
/// Without this, an orphaned queue message left the Studio spinner turning forever
/// with no warning — the user had no way to tell a slow run from a lost one.
/// </summary>
public sealed class StalledTaskWatchdogService : BackgroundService
{
    private readonly TaskWatchdogOptions options;
    private readonly ITaskCancellationRegistry cancellationRegistry;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<StalledTaskWatchdogService> logger;
    private readonly ConcurrentDictionary<Guid, byte> warnedTasks = new();

    public StalledTaskWatchdogService(
        IOptions<TaskWatchdogOptions> options,
        ITaskCancellationRegistry cancellationRegistry,
        IServiceScopeFactory scopeFactory,
        ILogger<StalledTaskWatchdogService> logger)
    {
        this.options = options.Value;
        this.cancellationRegistry = cancellationRegistry;
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(10, options.CheckIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                await InspectRunningTasksAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Stalled-task watchdog pass failed; retrying next interval.");
            }
        }
    }

    private async Task InspectRunningTasksAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AgentConsoleDbContext>();
        var consoleEvents = scope.ServiceProvider.GetRequiredService<IConsoleEventService>();

        var running = await dbContext.TaskRuns
            .Where(x => x.Status == TaskRunStatus.Running || x.Status == TaskRunStatus.Pending)
            .Select(x => new
            {
                x.Id,
                x.StartedAt,
                x.CreatedAt,
                LastEventAt = dbContext.ConsoleEvents
                    .Where(consoleEvent => consoleEvent.TaskRunId == x.Id)
                    .Max(consoleEvent => (DateTimeOffset?)consoleEvent.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        if (running.Count == 0)
        {
            warnedTasks.Clear();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var liveIds = running.Select(task => task.Id).ToHashSet();
        foreach (var warnedId in warnedTasks.Keys.Where(id => !liveIds.Contains(id)))
        {
            warnedTasks.TryRemove(warnedId, out _);
        }

        foreach (var task in running)
        {
            var lastActivityAt = task.LastEventAt ?? task.StartedAt ?? task.CreatedAt;
            var action = StalledTaskPolicy.Evaluate(
                lastActivityAt,
                now,
                cancellationRegistry.IsExecutingLocally(task.Id),
                options);

            switch (action)
            {
                case StalledTaskAction.Warn when warnedTasks.TryAdd(task.Id, 0):
                    logger.LogWarning(
                        "Task {TaskRunId} has been silent since {LastActivityAt}; reporting a stall.",
                        task.Id,
                        lastActivityAt);
                    await consoleEvents.WriteAsync(
                        task.Id,
                        null,
                        ConsoleEventType.Warning,
                        $"No agent activity for {(int)(now - lastActivityAt).TotalMinutes} minute(s). "
                        + "The run may have lost its queue message (for example after a RabbitMQ restart). "
                        + $"It will be marked as failed if nothing happens within {options.StallFailureMinutes} minute(s) of silence.",
                        null,
                        cancellationToken);
                    break;

                case StalledTaskAction.Fail:
                    await FailStalledTaskAsync(dbContext, consoleEvents, task.Id, lastActivityAt, now, cancellationToken);
                    warnedTasks.TryRemove(task.Id, out _);
                    break;
            }
        }
    }

    private async Task FailStalledTaskAsync(
        AgentConsoleDbContext dbContext,
        IConsoleEventService consoleEvents,
        Guid taskRunId,
        DateTimeOffset lastActivityAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var taskRun = await dbContext.TaskRuns.FirstOrDefaultAsync(x => x.Id == taskRunId, cancellationToken);
        if (taskRun is null || taskRun.Status is not (TaskRunStatus.Running or TaskRunStatus.Pending))
        {
            return;
        }

        var silentMinutes = (int)(now - lastActivityAt).TotalMinutes;
        var reason = $"Task stalled: no agent activity for {silentMinutes} minute(s). "
                     + "The queue message was most likely lost; use Rerun or send a follow-up to continue.";

        taskRun.Status = TaskRunStatus.Failed;
        taskRun.CompletedAt = now;
        taskRun.ErrorMessage = reason;
        await dbContext.SaveChangesAsync(cancellationToken);

        // Dangling agent runs would otherwise keep showing a spinner in Task Detail.
        var danglingAgentRuns = await dbContext.AgentRuns
            .Where(x => x.TaskRunId == taskRunId && x.Status == AgentRunStatus.Running)
            .ToListAsync(cancellationToken);
        foreach (var agentRun in danglingAgentRuns)
        {
            agentRun.Status = AgentRunStatus.Failed;
            agentRun.CompletedAt = now;
            agentRun.ErrorMessage = reason;
        }

        if (danglingAgentRuns.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogError("Task {TaskRunId} finalized as Failed after {SilentMinutes} minutes of silence.", taskRunId, silentMinutes);

        await consoleEvents.WriteAsync(
            taskRunId,
            null,
            ConsoleEventType.TaskFailed,
            reason,
            null,
            cancellationToken);
    }
}
