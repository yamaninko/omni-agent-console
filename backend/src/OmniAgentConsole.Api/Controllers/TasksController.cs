using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Application.Tasks;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Api.Controllers;

[ApiController]
[Route("api/tasks")]
public sealed class TasksController : ControllerBase
{
    private readonly AgentConsoleDbContext dbContext;
    private readonly IConsoleEventService consoleEvents;
    private readonly ITaskRunQueue taskRunQueue;
    private readonly ITaskCancellationRegistry cancellationRegistry;
    private readonly ITaskCancellationBroadcast cancellationBroadcast;

    public TasksController(
        AgentConsoleDbContext dbContext,
        IConsoleEventService consoleEvents,
        ITaskRunQueue taskRunQueue,
        ITaskCancellationRegistry cancellationRegistry,
        ITaskCancellationBroadcast cancellationBroadcast)
    {
        this.dbContext = dbContext;
        this.consoleEvents = consoleEvents;
        this.taskRunQueue = taskRunQueue;
        this.cancellationRegistry = cancellationRegistry;
        this.cancellationBroadcast = cancellationBroadcast;
    }

    [HttpPost]
    public async Task<ActionResult<TaskSummaryDto>> Create(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return BadRequest("Prompt is required.");
        }

        var taskRun = new TaskRun
        {
            Title = string.IsNullOrWhiteSpace(request.Title) ? CreateTitle(request.Prompt) : request.Title,
            InputPrompt = InputSanitizer.Redact(request.Prompt),
            InputContextJson = string.IsNullOrWhiteSpace(request.InputContextJson) ? null : InputSanitizer.Redact(request.InputContextJson),
            Status = TaskRunStatus.Pending
        };

        dbContext.TaskRuns.Add(taskRun);
        await dbContext.SaveChangesAsync(cancellationToken);

        await consoleEvents.WriteAsync(
            taskRun.Id,
            null,
            ConsoleEventType.TaskCreated,
            "Task created",
            null,
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { taskRunId = taskRun.Id }, ToSummary(taskRun));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TaskSummaryDto>>> List(CancellationToken cancellationToken)
    {
        var tasks = await dbContext.TaskRuns
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(100)
            .Select(x => ToSummary(x))
            .ToListAsync(cancellationToken);

        return Ok(tasks);
    }

    [HttpGet("{taskRunId:guid}")]
    public async Task<ActionResult<TaskDetailDto>> GetById(Guid taskRunId, CancellationToken cancellationToken)
    {
        var taskRun = await dbContext.TaskRuns
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.AgentRuns.OrderBy(agent => agent.ExecutionOrder))
            .Include(x => x.ConsoleEvents.OrderBy(consoleEvent => consoleEvent.CreatedAt))
            .Include(x => x.ModelCallLogs.OrderBy(modelCall => modelCall.CreatedAt))
            .FirstOrDefaultAsync(x => x.Id == taskRunId, cancellationToken);

        return taskRun is null ? NotFound() : Ok(ToDetail(taskRun));
    }

    [HttpPost("{taskRunId:guid}/run")]
    public async Task<IActionResult> Run(Guid taskRunId, CancellationToken cancellationToken)
    {
        var taskRun = await dbContext.TaskRuns.FirstOrDefaultAsync(x => x.Id == taskRunId, cancellationToken);
        if (taskRun is null)
        {
            return NotFound();
        }

        if (taskRun.Status == TaskRunStatus.Running)
        {
            return Conflict(new { taskRun.Id, taskRun.Status, message = "Task is already running." });
        }

        if (taskRun.Status is not (TaskRunStatus.Pending or TaskRunStatus.Failed or TaskRunStatus.Cancelled or TaskRunStatus.Completed))
        {
            return Conflict(new
            {
                taskRun.Id,
                taskRun.Status,
                message = "Only pending, failed, cancelled, or completed tasks can be started."
            });
        }

        if (taskRun.Status is TaskRunStatus.Failed or TaskRunStatus.Cancelled or TaskRunStatus.Completed)
        {
            var agentRuns = await dbContext.AgentRuns.Where(x => x.TaskRunId == taskRunId).ToListAsync(cancellationToken);
            dbContext.AgentRuns.RemoveRange(agentRuns);

            var modelCallLogs = await dbContext.ModelCallLogs.Where(x => x.TaskRunId == taskRunId).ToListAsync(cancellationToken);
            dbContext.ModelCallLogs.RemoveRange(modelCallLogs);

            var consoleEventsList = await dbContext.ConsoleEvents.Where(x => x.TaskRunId == taskRunId).ToListAsync(cancellationToken);
            dbContext.ConsoleEvents.RemoveRange(consoleEventsList);

            taskRun.TotalInputTokens = 0;
            taskRun.TotalOutputTokens = 0;
            taskRun.TotalTokens = 0;
            taskRun.TotalLatencyMs = 0;
        }

        taskRun.Status = TaskRunStatus.Running;
        taskRun.StartedAt = DateTimeOffset.UtcNow;
        taskRun.CompletedAt = null;
        taskRun.ErrorMessage = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        await consoleEvents.WriteAsync(
            taskRun.Id,
            null,
            ConsoleEventType.TaskStarted,
            "Task queued for background execution",
            null,
            cancellationToken);

        await taskRunQueue.EnqueueAsync(taskRun.Id, cancellationToken);

        return Accepted(new { taskRun.Id, taskRun.Status, queued = true });
    }

    [HttpPost("{taskRunId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid taskRunId, CancellationToken cancellationToken)
    {
        var taskRun = await dbContext.TaskRuns.FirstOrDefaultAsync(x => x.Id == taskRunId, cancellationToken);
        if (taskRun is null)
        {
            return NotFound();
        }

        if (taskRun.Status is TaskRunStatus.Completed or TaskRunStatus.Failed or TaskRunStatus.Cancelled)
        {
            return Accepted(new { taskRun.Id, taskRun.Status });
        }

        taskRun.Status = TaskRunStatus.Cancelled;
        taskRun.CompletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        // Status must be Cancelled in the DB before the token fires, so the
        // worker classifies the OperationCanceledException as a user cancel
        // (ACK) rather than a shutdown (NACK/requeue).
        cancellationRegistry.Cancel(taskRun.Id);
        await cancellationBroadcast.PublishCancelAsync(taskRun.Id, cancellationToken);

        await consoleEvents.WriteAsync(
            taskRun.Id,
            null,
            ConsoleEventType.TaskCancelled,
            "Task cancellation requested",
            null,
            cancellationToken);

        return Accepted(new { taskRun.Id, taskRun.Status });
    }

    [HttpGet("{taskRunId:guid}/events")]
    public async Task<ActionResult<IReadOnlyList<ConsoleEventDto>>> GetEvents(Guid taskRunId, CancellationToken cancellationToken)
    {
        var events = await dbContext.ConsoleEvents
            .AsNoTracking()
            .Where(x => x.TaskRunId == taskRunId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(events.Select(ToConsoleEventDto).ToList());
    }

    [HttpPut("{taskRunId:guid}/title")]
    public async Task<IActionResult> UpdateTitle(Guid taskRunId, [FromBody] UpdateTitleRequest request, CancellationToken cancellationToken)
    {
        var taskRun = await dbContext.TaskRuns.FirstOrDefaultAsync(x => x.Id == taskRunId, cancellationToken);
        if (taskRun is null)
        {
            return NotFound();
        }

        taskRun.Title = request.Title;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    [HttpDelete("{taskRunId:guid}")]
    public async Task<IActionResult> Delete(Guid taskRunId, CancellationToken cancellationToken)
    {
        var taskRun = await dbContext.TaskRuns.FirstOrDefaultAsync(x => x.Id == taskRunId, cancellationToken);
        if (taskRun is null)
        {
            return NotFound();
        }

        var agentRuns = await dbContext.AgentRuns.Where(x => x.TaskRunId == taskRunId).ToListAsync(cancellationToken);
        dbContext.AgentRuns.RemoveRange(agentRuns);

        var modelCallLogs = await dbContext.ModelCallLogs.Where(x => x.TaskRunId == taskRunId).ToListAsync(cancellationToken);
        dbContext.ModelCallLogs.RemoveRange(modelCallLogs);

        var consoleEventsList = await dbContext.ConsoleEvents.Where(x => x.TaskRunId == taskRunId).ToListAsync(cancellationToken);
        dbContext.ConsoleEvents.RemoveRange(consoleEventsList);

        dbContext.TaskRuns.Remove(taskRun);
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private static TaskSummaryDto ToSummary(TaskRun taskRun)
    {
        return new TaskSummaryDto(
            taskRun.Id,
            taskRun.Title,
            taskRun.Status,
            taskRun.CreatedAt,
            taskRun.CompletedAt,
            taskRun.TotalTokens,
            taskRun.TotalLatencyMs);
    }

    private static TaskDetailDto ToDetail(TaskRun taskRun)
    {
        var agentNames = taskRun.AgentRuns.ToDictionary(x => x.Id, x => x.AgentName);

        return new TaskDetailDto(
            taskRun.Id,
            taskRun.Title,
            taskRun.InputPrompt,
            taskRun.InputContextJson,
            taskRun.Status,
            taskRun.CreatedAt,
            taskRun.StartedAt,
            taskRun.CompletedAt,
            taskRun.TotalInputTokens,
            taskRun.TotalOutputTokens,
            taskRun.TotalTokens,
            taskRun.TotalLatencyMs,
            taskRun.ErrorMessage,
            taskRun.AgentRuns
                .OrderBy(x => x.ExecutionOrder)
                .Select(ToAgentRunDto)
                .ToList(),
            taskRun.ModelCallLogs
                .OrderBy(x => x.CreatedAt)
                .Select(x => ToModelCallDto(x, agentNames.GetValueOrDefault(x.AgentRunId, "Unknown Agent")))
                .ToList(),
            taskRun.ConsoleEvents
                .OrderBy(x => x.CreatedAt)
                .Select(ToConsoleEventDto)
                .ToList());
    }

    private static AgentRunDetailDto ToAgentRunDto(AgentRun agentRun)
    {
        return new AgentRunDetailDto(
            agentRun.Id,
            agentRun.AgentName,
            agentRun.AgentType,
            agentRun.Status,
            agentRun.Input,
            agentRun.Output,
            agentRun.ExecutionOrder,
            agentRun.StartedAt,
            agentRun.CompletedAt,
            agentRun.LatencyMs,
            agentRun.ErrorMessage);
    }

    private static ModelCallLogDetailDto ToModelCallDto(ModelCallLog modelCall, string agentName)
    {
        return new ModelCallLogDetailDto(
            modelCall.Id,
            modelCall.AgentRunId,
            agentName,
            modelCall.Provider,
            modelCall.Model,
            modelCall.RequestType,
            modelCall.InputTokens,
            modelCall.OutputTokens,
            modelCall.TotalTokens,
            modelCall.LatencyMs,
            modelCall.Status,
            modelCall.ErrorCode,
            modelCall.ErrorMessage,
            modelCall.StartedAt,
            modelCall.CompletedAt,
            modelCall.EstimatedCost ?? 0m);
    }

    private static ConsoleEventDto ToConsoleEventDto(ConsoleEvent consoleEvent)
    {
        return new ConsoleEventDto(
            consoleEvent.Id,
            consoleEvent.TaskRunId,
            consoleEvent.AgentRunId,
            consoleEvent.EventType,
            consoleEvent.Message,
            consoleEvent.PayloadJson,
            consoleEvent.CreatedAt);
    }

    private static string CreateTitle(string prompt)
    {
        var normalized = prompt.Trim().ReplaceLineEndings(" ");
        return normalized.Length <= 80 ? normalized : string.Concat(normalized.AsSpan(0, 77), "...");
    }
}

public record UpdateTitleRequest(string Title);
