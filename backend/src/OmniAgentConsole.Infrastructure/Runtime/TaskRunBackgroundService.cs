using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Infrastructure.Runtime;

public sealed class TaskRunBackgroundService : BackgroundService
{
    private readonly ITaskRunQueue queue;
    private readonly ITaskCancellationRegistry cancellationRegistry;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<TaskRunBackgroundService> logger;

    public TaskRunBackgroundService(
        ITaskRunQueue queue,
        ITaskCancellationRegistry cancellationRegistry,
        IServiceScopeFactory scopeFactory,
        ILogger<TaskRunBackgroundService> logger)
    {
        this.queue = queue;
        this.cancellationRegistry = cancellationRegistry;
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            QueueMessage message;
            try
            {
                message = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Task queue dequeue failed. Retrying in 5 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            if (message.Kind == QueuedWorkKind.PanelSession)
            {
                await RunQueuedPanelAsync(message, stoppingToken);
            }
            else
            {
                await RunQueuedTaskAsync(message, stoppingToken);
            }
        }
    }

    private async Task RunQueuedTaskAsync(QueueMessage message, CancellationToken stoppingToken)
    {
        var taskRunId = message.WorkId;
        var taskToken = cancellationRegistry.CreateToken(taskRunId, stoppingToken);
        var success = false;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AgentConsoleDbContext>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IAgentOrchestratorService>();

            var taskStatus = await dbContext.TaskRuns
                .AsNoTracking()
                .Where(x => x.Id == taskRunId)
                .Select(x => (TaskRunStatus?)x.Status)
                .FirstOrDefaultAsync(CancellationToken.None);

            if (taskStatus is null)
            {
                logger.LogWarning("Queued task {TaskRunId} was not found.", taskRunId);
                success = true;
                return;
            }

            if (taskStatus == TaskRunStatus.Cancelled)
            {
                logger.LogInformation("Queued task {TaskRunId} was cancelled before execution.", taskRunId);
                success = true;
                return;
            }

            await orchestrator.RunTaskAsync(taskRunId, taskToken);
            success = true;
        }
        catch (OperationCanceledException)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation(
                    "Task {TaskRunId} interrupted by worker shutdown; message will be NACK'ed for redelivery.",
                    taskRunId);
                success = false;
            }
            else
            {
                logger.LogInformation("Task {TaskRunId} execution was cancelled.", taskRunId);
                success = true;
            }
        }
        catch (Exception exception) when (message.Redelivered)
        {
            logger.LogError(
                exception,
                "Task {TaskRunId} failed again on redelivery; dropping the message.",
                taskRunId);
            await TryMarkTaskFailedAsync(
                taskRunId,
                "Task failed twice with an unexpected error; the queue message was dropped to avoid an infinite requeue loop.");
            success = true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Task {TaskRunId} background execution failed; the message will be redelivered.", taskRunId);
            success = false;
        }
        finally
        {
            cancellationRegistry.Complete(taskRunId);
            try
            {
                await message.AcknowledgeAsync(success);
            }
            catch (Exception ackEx)
            {
                logger.LogError(ackEx, "Failed to acknowledge queue message for task {TaskRunId}.", taskRunId);
            }
        }
    }

    private async Task RunQueuedPanelAsync(QueueMessage message, CancellationToken stoppingToken)
    {
        var panelSessionId = message.WorkId;
        var panelToken = cancellationRegistry.CreateToken(panelSessionId, stoppingToken);
        var success = false;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AgentConsoleDbContext>();
            var panelService = scope.ServiceProvider.GetRequiredService<IPanelDiscussionService>();

            var status = await dbContext.PanelSessions
                .AsNoTracking()
                .Where(x => x.Id == panelSessionId)
                .Select(x => (PanelSessionStatus?)x.Status)
                .FirstOrDefaultAsync(CancellationToken.None);

            if (status is null)
            {
                logger.LogWarning("Queued panel session {PanelSessionId} was not found.", panelSessionId);
                success = true;
                return;
            }

            if (status is PanelSessionStatus.Cancelled or PanelSessionStatus.Completed)
            {
                logger.LogInformation(
                    "Queued panel {PanelSessionId} already {Status}; skipping.",
                    panelSessionId,
                    status);
                success = true;
                return;
            }

            await panelService.RunSessionAsync(panelSessionId, panelToken);
            success = true;
        }
        catch (OperationCanceledException)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation(
                    "Panel {PanelSessionId} interrupted by worker shutdown; message will be NACK'ed for redelivery.",
                    panelSessionId);
                success = false;
            }
            else
            {
                logger.LogInformation("Panel {PanelSessionId} execution was cancelled.", panelSessionId);
                success = true;
            }
        }
        catch (Exception exception) when (message.Redelivered)
        {
            logger.LogError(
                exception,
                "Panel {PanelSessionId} failed again on redelivery; dropping the message.",
                panelSessionId);
            await TryMarkPanelFailedAsync(
                panelSessionId,
                "Panel failed twice with an unexpected error; the queue message was dropped.");
            success = true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Panel {PanelSessionId} background execution failed; the message will be redelivered.",
                panelSessionId);
            success = false;
        }
        finally
        {
            cancellationRegistry.Complete(panelSessionId);
            try
            {
                await message.AcknowledgeAsync(success);
            }
            catch (Exception ackEx)
            {
                logger.LogError(ackEx, "Failed to acknowledge queue message for panel {PanelSessionId}.", panelSessionId);
            }
        }
    }

    private async Task TryMarkTaskFailedAsync(Guid taskRunId, string reason)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AgentConsoleDbContext>();
            var taskRun = await dbContext.TaskRuns.FirstOrDefaultAsync(x => x.Id == taskRunId);
            if (taskRun is not null && taskRun.Status is not (TaskRunStatus.Completed or TaskRunStatus.Cancelled))
            {
                taskRun.Status = TaskRunStatus.Failed;
                taskRun.CompletedAt = DateTimeOffset.UtcNow;
                taskRun.ErrorMessage = reason;
                await dbContext.SaveChangesAsync();
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not mark poison task {TaskRunId} as failed.", taskRunId);
        }
    }

    private async Task TryMarkPanelFailedAsync(Guid panelSessionId, string reason)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AgentConsoleDbContext>();
            var session = await dbContext.PanelSessions.FirstOrDefaultAsync(x => x.Id == panelSessionId);
            if (session is not null
                && session.Status is not (PanelSessionStatus.Completed or PanelSessionStatus.Cancelled))
            {
                session.Status = PanelSessionStatus.Failed;
                session.CompletedAt = DateTimeOffset.UtcNow;
                session.ErrorMessage = reason;
                await dbContext.SaveChangesAsync();
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not mark poison panel {PanelSessionId} as failed.", panelSessionId);
        }
    }
}
