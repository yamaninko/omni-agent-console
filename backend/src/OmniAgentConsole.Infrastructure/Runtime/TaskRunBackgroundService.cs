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

            await RunQueuedTaskAsync(message, stoppingToken);
        }
    }

    private async Task RunQueuedTaskAsync(QueueMessage message, CancellationToken stoppingToken)
    {
        var taskRunId = message.TaskRunId;
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
                success = true; // Ack since the task doesn't exist and won't exist
                return;
            }

            if (taskStatus == TaskRunStatus.Cancelled)
            {
                logger.LogInformation("Queued task {TaskRunId} was cancelled before execution.", taskRunId);
                success = true; // Ack since it's already cancelled
                return;
            }

            await orchestrator.RunTaskAsync(taskRunId, taskToken);
            success = true;
        }
        catch (OperationCanceledException)
        {
            // The orchestrator only lets an OCE escape for host-shutdown interruptions;
            // user cancellations are finalized inside RunTaskAsync and return normally.
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
            // Poison-message guard: this message already failed once and was
            // redelivered. A second unexpected failure means requeueing again
            // would loop forever — finalize the task as Failed and ACK.
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
            // Nack and requeue on the first failure
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
}
