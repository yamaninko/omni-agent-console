using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OmniAgentConsole.Api.Middleware;
using OmniAgentConsole.Application.Configuration;
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
    private readonly SharedLabOptions sharedLab;

    public TasksController(
        AgentConsoleDbContext dbContext,
        IConsoleEventService consoleEvents,
        ITaskRunQueue taskRunQueue,
        ITaskCancellationRegistry cancellationRegistry,
        ITaskCancellationBroadcast cancellationBroadcast,
        IOptions<SharedLabOptions> sharedLab)
    {
        this.dbContext = dbContext;
        this.consoleEvents = consoleEvents;
        this.taskRunQueue = taskRunQueue;
        this.cancellationRegistry = cancellationRegistry;
        this.cancellationBroadcast = cancellationBroadcast;
        this.sharedLab = sharedLab.Value;
    }

    // True when the caller is a shared-lab student session (not the instructor).
    private bool SessionScoped => sharedLab.Enabled && !SharedLabHttp.IsAdmin(HttpContext);

    private string? CallerSessionId => SharedLabHttp.GetSessionId(HttpContext);

    // Ownership mismatches return 404 (not 403) so foreign task ids leak nothing.
    private bool IsForeignTask(TaskRun taskRun) =>
        SessionScoped
        && !string.Equals(taskRun.OwnerSessionId, CallerSessionId, StringComparison.Ordinal);

    /// <summary>
    /// Rewrites the context's workspacePath into the caller's session subtree
    /// (e.g. "/workspace/foo" → "/workspace/sessions/{sid}/foo"). Unparseable
    /// context is returned unchanged — the orchestrator will not export for it.
    /// </summary>
    private static string? MapContextWorkspacePath(string? inputContextJson, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(inputContextJson))
        {
            return inputContextJson;
        }

        try
        {
            if (JsonNode.Parse(inputContextJson) is not JsonObject context)
            {
                return inputContextJson;
            }

            var workspacePath = context["workspacePath"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(workspacePath))
            {
                context["workspacePath"] = SharedLabPolicy.MapWorkspacePath(
                    WorkspacePathGuard.DefaultRoot, sessionId, workspacePath);
            }

            return context.ToJsonString();
        }
        catch
        {
            return inputContextJson;
        }
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

        if (SessionScoped && CallerSessionId is { } sessionId)
        {
            taskRun.OwnerSessionId = sessionId;
            taskRun.InputContextJson = MapContextWorkspacePath(taskRun.InputContextJson, sessionId);
        }

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
        var query = dbContext.TaskRuns.AsNoTracking();
        if (SessionScoped)
        {
            var sessionId = CallerSessionId;
            query = query.Where(x => x.OwnerSessionId == sessionId);
        }

        var tasks = await query
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

        return taskRun is null || IsForeignTask(taskRun) ? NotFound() : Ok(ToDetail(taskRun));
    }

    /// <summary>
    /// Cheap status probe for the Studio poll loop. Does not load agent I/O,
    /// console events, or model call logs (those grow large mid-run and were
    /// re-serialized every 2s via GetById — heavy on Windows browsers + Docker).
    /// Live token totals come from a SQL aggregate over model_call_logs only.
    /// </summary>
    [HttpGet("{taskRunId:guid}/status")]
    public async Task<ActionResult<TaskStatusDto>> GetStatus(Guid taskRunId, CancellationToken cancellationToken)
    {
        var taskRun = await dbContext.TaskRuns
            .AsNoTracking()
            .Where(x => x.Id == taskRunId)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Status,
                x.CompletedAt,
                x.TotalInputTokens,
                x.TotalOutputTokens,
                x.TotalTokens,
                x.TotalLatencyMs,
                x.ErrorMessage,
                x.OwnerSessionId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (taskRun is null)
        {
            return NotFound();
        }

        // Mirror IsForeignTask without loading the full entity graph.
        if (SessionScoped
            && !string.Equals(taskRun.OwnerSessionId, CallerSessionId, StringComparison.Ordinal))
        {
            return NotFound();
        }

        var inputTokens = taskRun.TotalInputTokens;
        var outputTokens = taskRun.TotalOutputTokens;
        var totalTokens = taskRun.TotalTokens;
        var latencyMs = taskRun.TotalLatencyMs;

        // While Running, task-level totals are only finalized at the end of the
        // orchestrator; sum model call rows so the metrics panel stays live.
        if (taskRun.Status is TaskRunStatus.Running or TaskRunStatus.Pending)
        {
            var live = await dbContext.ModelCallLogs
                .AsNoTracking()
                .Where(x => x.TaskRunId == taskRunId)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Input = g.Sum(x => x.InputTokens),
                    Output = g.Sum(x => x.OutputTokens),
                    Total = g.Sum(x => x.TotalTokens),
                    Latency = g.Sum(x => x.LatencyMs)
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (live is not null)
            {
                inputTokens = live.Input;
                outputTokens = live.Output;
                totalTokens = live.Total;
                latencyMs = live.Latency;
            }
        }

        return Ok(new TaskStatusDto(
            taskRun.Id,
            taskRun.Title,
            taskRun.Status,
            taskRun.CompletedAt,
            inputTokens,
            outputTokens,
            totalTokens,
            latencyMs,
            taskRun.ErrorMessage));
    }

    [HttpPost("{taskRunId:guid}/run")]
    public async Task<IActionResult> Run(Guid taskRunId, CancellationToken cancellationToken)
    {
        var taskRun = await dbContext.TaskRuns.FirstOrDefaultAsync(x => x.Id == taskRunId, cancellationToken);
        if (taskRun is null || IsForeignTask(taskRun))
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

    /// <summary>
    /// Continues a finished task with a new follow-up prompt. Unlike /run (rerun),
    /// this keeps console events, agent runs, and token totals so the Studio
    /// session can iterate on the same workspace without losing history.
    /// </summary>
    [HttpPost("{taskRunId:guid}/continue")]
    public async Task<IActionResult> Continue(Guid taskRunId, [FromBody] ContinueTaskRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return BadRequest("Prompt is required.");
        }

        var taskRun = await dbContext.TaskRuns.FirstOrDefaultAsync(x => x.Id == taskRunId, cancellationToken);
        if (taskRun is null || IsForeignTask(taskRun))
        {
            return NotFound();
        }

        if (taskRun.Status == TaskRunStatus.Running || taskRun.Status == TaskRunStatus.Pending)
        {
            return Conflict(new
            {
                taskRun.Id,
                taskRun.Status,
                message = "Task is still active. Wait for it to finish or cancel it before continuing."
            });
        }

        if (taskRun.Status is not (TaskRunStatus.Completed or TaskRunStatus.Failed or TaskRunStatus.Cancelled))
        {
            return Conflict(new
            {
                taskRun.Id,
                taskRun.Status,
                message = "Only completed, failed, or cancelled tasks can be continued."
            });
        }

        var followUpPrompt = InputSanitizer.Redact(request.Prompt);
        taskRun.InputContextJson = TaskContinuationContext.Merge(
            taskRun.InputContextJson,
            taskRun.InputPrompt,
            followUpPrompt);
        taskRun.InputPrompt = followUpPrompt;
        taskRun.Status = TaskRunStatus.Running;
        taskRun.StartedAt = DateTimeOffset.UtcNow;
        taskRun.CompletedAt = null;
        taskRun.ErrorMessage = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        await consoleEvents.WriteAsync(
            taskRun.Id,
            null,
            ConsoleEventType.TaskStarted,
            "Follow-up queued for background execution",
            null,
            cancellationToken);

        await taskRunQueue.EnqueueAsync(taskRun.Id, cancellationToken);

        return Accepted(new { taskRun.Id, taskRun.Status, queued = true, continued = true });
    }

    [HttpPost("{taskRunId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid taskRunId, CancellationToken cancellationToken)
    {
        var taskRun = await dbContext.TaskRuns.FirstOrDefaultAsync(x => x.Id == taskRunId, cancellationToken);
        if (taskRun is null || IsForeignTask(taskRun))
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
        if (SessionScoped)
        {
            var owner = await dbContext.TaskRuns
                .AsNoTracking()
                .Where(x => x.Id == taskRunId)
                .Select(x => x.OwnerSessionId)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.Equals(owner, CallerSessionId, StringComparison.Ordinal))
            {
                return NotFound();
            }
        }

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
        if (taskRun is null || IsForeignTask(taskRun))
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
        if (taskRun is null || IsForeignTask(taskRun))
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
