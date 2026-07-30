using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniAgentConsole.Application.Agents;
using OmniAgentConsole.Application.Providers;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Infrastructure.Runtime;

public sealed class AgentOrchestratorService : IAgentOrchestratorService
{
    private const string WorkspaceRoot = WorkspacePathGuard.DefaultRoot;

    private static readonly AgentType[] DefaultSequence =
    [
        AgentType.Planner,
        AgentType.Research,
        AgentType.Coder,
        AgentType.Reviewer,
        AgentType.OpsMonitor
    ];

    private readonly AgentConsoleDbContext dbContext;
    private readonly IConsoleEventService consoleEvents;
    private readonly ModelChainExecutor chainExecutor;
    private readonly CoderToolLoopRunner coderToolLoop;
    private readonly IModelRouter modelRouter;
    private readonly ITokenUsageExtractor tokenUsageExtractor;

    public AgentOrchestratorService(
        AgentConsoleDbContext dbContext,
        IConsoleEventService consoleEvents,
        ModelChainExecutor chainExecutor,
        CoderToolLoopRunner coderToolLoop,
        IModelRouter modelRouter,
        ITokenUsageExtractor tokenUsageExtractor)
    {
        this.dbContext = dbContext;
        this.consoleEvents = consoleEvents;
        this.chainExecutor = chainExecutor;
        this.coderToolLoop = coderToolLoop;
        this.modelRouter = modelRouter;
        this.tokenUsageExtractor = tokenUsageExtractor;
    }

    public async Task RunTaskAsync(Guid taskRunId, CancellationToken cancellationToken)
    {
        var taskRun = await dbContext.TaskRuns.FirstOrDefaultAsync(x => x.Id == taskRunId, cancellationToken);
        if (taskRun is null)
        {
            throw new InvalidOperationException($"Task run {taskRunId} was not found.");
        }

        if (taskRun.Status is TaskRunStatus.Completed or TaskRunStatus.Cancelled)
        {
            await consoleEvents.WriteAsync(
                taskRun.Id,
                null,
                ConsoleEventType.Warning,
                $"Task is already {taskRun.Status}. Execution skipped.",
                null,
                cancellationToken);
            return;
        }

        // A redelivered queue message means a previous attempt was interrupted
        // mid-flight; close its dangling agent runs before starting the new attempt.
        var staleAgentRuns = await dbContext.AgentRuns
            .Where(x => x.TaskRunId == taskRun.Id && x.Status == AgentRunStatus.Running)
            .ToListAsync(cancellationToken);
        foreach (var stale in staleAgentRuns)
        {
            stale.Status = AgentRunStatus.Failed;
            stale.CompletedAt = DateTimeOffset.UtcNow;
            stale.ErrorMessage = "Interrupted by worker restart; task was re-queued.";
        }

        taskRun.Status = TaskRunStatus.Running;
        taskRun.StartedAt ??= DateTimeOffset.UtcNow;
        taskRun.ErrorMessage = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        // Keep the console row under varchar(4000); full prompt lives on task_runs.InputPrompt.
        var promptPreview = taskRun.InputPrompt.Length > 800
            ? taskRun.InputPrompt[..800] + "…"
            : taskRun.InputPrompt;
        await consoleEvents.WriteAsync(
            taskRun.Id,
            null,
            ConsoleEventType.TaskStarted,
            $"Task execution started with prompt: \"{promptPreview}\"",
            null,
            cancellationToken);

        var taskStopwatch = Stopwatch.StartNew();
        var previousOutputs = new List<AgentOutput>();

        var (workspacePath, requestedSkillIds) = AgentPromptBuilder.ParseTaskContext(taskRun.InputContextJson);
        var appliedSkills = await dbContext.SkillDefinitions
            .AsNoTracking()
            .Where(skill => skill.Enabled && requestedSkillIds.Contains(skill.Id))
            .OrderBy(skill => skill.SortOrder)
            .ThenBy(skill => skill.Name)
            .ToListAsync(cancellationToken);

        // Always inject Dockerized Service when building an app so Workspace Project run
        // gets Dockerfile + compose even if the user forgot the packaging skill.
        var dockerSkill = await dbContext.SkillDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                skill => skill.Enabled && skill.Name == "Dockerized Service",
                cancellationToken);
        var dockerAutoApplied = false;
        if (dockerSkill is not null
            && appliedSkills.All(skill => skill.Id != dockerSkill.Id))
        {
            appliedSkills = appliedSkills
                .Append(dockerSkill)
                .OrderBy(skill => skill.SortOrder)
                .ThenBy(skill => skill.Name)
                .ToList();
            dockerAutoApplied = true;
        }

        var skillsBlock = AgentPromptBuilder.BuildSkillsBlock(appliedSkills);

        if (appliedSkills.Count > 0)
        {
            var suffix = dockerAutoApplied ? " (Dockerized Service auto-applied)" : string.Empty;
            await consoleEvents.WriteAsync(
                taskRun.Id,
                null,
                ConsoleEventType.AgentStep,
                $"Applied {appliedSkills.Count} skill(s): {string.Join(", ", appliedSkills.Select(skill => skill.Name))}{suffix}",
                null,
                cancellationToken);
        }
        else if (dockerAutoApplied)
        {
            await consoleEvents.WriteAsync(
                taskRun.Id,
                null,
                ConsoleEventType.AgentStep,
                "Auto-applied Dockerized Service (required for Workspace Project run).",
                null,
                cancellationToken);
        }

        try
        {
            var definitionsList = await dbContext.AgentDefinitions
                .Include(x => x.ApiCredential)
                .Where(x => x.Enabled)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var defaultCredential = await dbContext.ApiCredentials
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.IsDefault, cancellationToken);

            if (defaultCredential != null)
            {
                foreach (var definition in definitionsList)
                {
                    if (definition.ApiCredential == null)
                    {
                        definition.ApiCredential = defaultCredential;
                        definition.ApiCredentialId = defaultCredential.Id;
                    }
                }
            }

            var definitions = definitionsList
                .GroupBy(x => x.Type)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt).First()
                );

            var executionOrder = 1;
            foreach (var agentType in DefaultSequence)
            {
                await dbContext.Entry(taskRun).ReloadAsync(cancellationToken);
                if (taskRun.Status == TaskRunStatus.Cancelled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (!definitions.TryGetValue(agentType, out var agentDefinition))
                {
                    await consoleEvents.WriteAsync(
                        taskRun.Id,
                        null,
                        ConsoleEventType.Warning,
                        $"{agentType} is disabled or not configured. Skipping.",
                        null,
                        cancellationToken);
                    continue;
                }

                AgentOutput output;
                string coderExportRoot = string.Empty;
                var coderHasWorkspace = agentDefinition.Type == AgentType.Coder
                    && !string.IsNullOrWhiteSpace(workspacePath)
                    && WorkspacePathGuard.TryResolve(WorkspaceRoot, workspacePath, out coderExportRoot);

                if (coderHasWorkspace)
                {
                    output = await coderToolLoop.RunAsync(
                        taskRun,
                        agentDefinition,
                        previousOutputs,
                        executionOrder,
                        skillsBlock,
                        coderExportRoot,
                        cancellationToken);
                }
                else
                {
                    if (agentDefinition.Type == AgentType.Coder && !string.IsNullOrWhiteSpace(workspacePath))
                    {
                        await consoleEvents.WriteAsync(
                            taskRun.Id,
                            null,
                            ConsoleEventType.Warning,
                            $"Workspace path must stay under {WorkspaceRoot}; Coder runs without filesystem tools.",
                            null,
                            cancellationToken);
                    }

                    output = await RunAgentAsync(
                        taskRun,
                        agentDefinition,
                        previousOutputs,
                        executionOrder,
                        skillsBlock,
                        cancellationToken);
                }

                previousOutputs.Add(output);
                executionOrder++;

                // Single post-Reviewer Coder fix pass (educational "AI reviewed → AI fixed").
                if (agentType == AgentType.Reviewer)
                {
                    executionOrder = await MaybeRunReviewerFixLoopAsync(
                        taskRun,
                        definitions,
                        previousOutputs,
                        executionOrder,
                        skillsBlock,
                        workspacePath,
                        output.Content,
                        cancellationToken);
                }
            }

            taskStopwatch.Stop();
            taskRun.Status = TaskRunStatus.Completed;
            taskRun.CompletedAt = DateTimeOffset.UtcNow;
            taskRun.TotalLatencyMs = taskStopwatch.ElapsedMilliseconds;
            await RecalculateTaskTotalsAsync(taskRun, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            await consoleEvents.WriteAsync(
                taskRun.Id,
                null,
                ConsoleEventType.TaskCompleted,
                "Task completed",
                RunTelemetry.BuildTaskPayload(taskRun),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            taskStopwatch.Stop();

            var statusAfterReload = taskRun.Status;
            try
            {
                await dbContext.Entry(taskRun).ReloadAsync(CancellationToken.None);
                statusAfterReload = taskRun.Status;
            }
            catch { }

            if (ShouldRequeueAfterCancellation(statusAfterReload, cancellationToken.IsCancellationRequested))
            {
                // Host shutdown, not a user cancel: leave the task Running and rethrow
                // so the queue layer NACKs the message and it is redelivered on restart.
                try
                {
                    await consoleEvents.WriteAsync(
                        taskRun.Id,
                        null,
                        ConsoleEventType.Warning,
                        "Task execution interrupted by worker shutdown; it will be re-queued.",
                        null,
                        CancellationToken.None);
                }
                catch { }

                throw;
            }

            taskRun.Status = TaskRunStatus.Cancelled;
            taskRun.CompletedAt = DateTimeOffset.UtcNow;
            taskRun.TotalLatencyMs = taskStopwatch.ElapsedMilliseconds;
            await RecalculateTaskTotalsAsync(taskRun, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            await consoleEvents.WriteAsync(
                taskRun.Id,
                null,
                ConsoleEventType.TaskCancelled,
                "Task execution cancelled",
                null,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            taskStopwatch.Stop();
            taskRun.Status = TaskRunStatus.Failed;
            taskRun.CompletedAt = DateTimeOffset.UtcNow;
            taskRun.TotalLatencyMs = taskStopwatch.ElapsedMilliseconds;
            taskRun.ErrorMessage = exception.Message;
            await RecalculateTaskTotalsAsync(taskRun, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            await consoleEvents.WriteAsync(
                taskRun.Id,
                null,
                ConsoleEventType.TaskFailed,
                $"Task failed: {exception.Message}",
                RunTelemetry.BuildErrorPayload(exception),
                cancellationToken);
        }
    }

    private async Task<AgentOutput> RunAgentAsync(
        TaskRun taskRun,
        AgentDefinition agentDefinition,
        IReadOnlyList<AgentOutput> previousOutputs,
        int executionOrder,
        string? skillsBlock,
        CancellationToken cancellationToken)
    {
        var route = modelRouter.Resolve(agentDefinition);
        var agentRun = new AgentRun
        {
            TaskRunId = taskRun.Id,
            AgentDefinitionId = agentDefinition.Id,
            AgentName = agentDefinition.Name,
            AgentType = agentDefinition.Type,
            Status = AgentRunStatus.Running,
            ExecutionOrder = executionOrder,
            StartedAt = DateTimeOffset.UtcNow,
            ConfigSnapshotJson = RunTelemetry.BuildAgentConfigPayload(agentDefinition, route)
        };

        dbContext.AgentRuns.Add(agentRun);
        await dbContext.SaveChangesAsync(cancellationToken);

        await consoleEvents.WriteAsync(
            taskRun.Id,
            agentRun.Id,
            agentDefinition.Type == AgentType.Planner ? ConsoleEventType.PlannerStarted : ConsoleEventType.AgentStarted,
            $"{agentDefinition.Name} started using model: {route.Model}",
            RunTelemetry.BuildAgentPayload(agentDefinition, route),
            cancellationToken);

        var messages = AgentPromptBuilder.BuildMessages(taskRun, agentDefinition, previousOutputs, skillsBlock);
        agentRun.Input = RunTelemetry.TrimForStorage(InputSanitizer.Redact(messages.Last().Content), 24000);
        await dbContext.SaveChangesAsync(cancellationToken);

        var modelRequest = new ModelRequest(
            route.Provider,
            route.Model,
            messages,
            agentDefinition.Temperature,
            agentDefinition.MaxTokens,
            agentDefinition.TimeoutSeconds,
            AgentPromptBuilder.BuildRequestMetadata(taskRun, agentRun, agentDefinition));

        var modelCall = new ModelCallLog
        {
            TaskRunId = taskRun.Id,
            AgentRunId = agentRun.Id,
            Provider = route.Provider,
            Model = route.Model,
            RequestType = ModelRequestType.ChatCompletion,
            PromptHash = RunTelemetry.ComputePromptHash(messages),
            Status = ModelCallStatus.Started,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ModelCallLogs.Add(modelCall);
        await dbContext.SaveChangesAsync(cancellationToken);

        await consoleEvents.WriteAsync(
            taskRun.Id,
            agentRun.Id,
            ConsoleEventType.ModelCallStarted,
            $"OMNIAGENT API called model={route.Model}",
            RunTelemetry.BuildModelCallPayload(modelCall),
            cancellationToken);

        var agentStopwatch = Stopwatch.StartNew();
        try
        {
            var response = await chainExecutor.ExecuteAsync(
                modelRequest,
                agentDefinition,
                modelCall,
                taskRun.Id,
                agentRun.Id,
                cancellationToken);

            agentStopwatch.Stop();
            var usage = response.TotalTokens.HasValue
                ? tokenUsageExtractor.Extract(response)
                : tokenUsageExtractor.Estimate(modelRequest, response);

            modelCall.Status = ModelCallStatus.Succeeded;
            modelCall.CompletedAt = DateTimeOffset.UtcNow;
            modelCall.LatencyMs = agentStopwatch.ElapsedMilliseconds;
            modelCall.InputTokens = usage.InputTokens;
            modelCall.OutputTokens = usage.OutputTokens;
            modelCall.TotalTokens = usage.TotalTokens;
            modelCall.RawMetadataJson = response.RawMetadataJson;
            modelCall.EstimatedCost = RunTelemetry.CalculateEstimatedCost(modelCall.Model, usage.InputTokens, usage.OutputTokens);

            agentRun.Output = InputSanitizer.Redact(response.Content);
            agentRun.Status = AgentRunStatus.Completed;
            agentRun.CompletedAt = DateTimeOffset.UtcNow;
            agentRun.LatencyMs = agentStopwatch.ElapsedMilliseconds;

            await dbContext.SaveChangesAsync(cancellationToken);

            await consoleEvents.WriteAsync(
                taskRun.Id,
                agentRun.Id,
                ConsoleEventType.ModelCallCompleted,
                $"Model call completed using {modelCall.Model} (Tokens: {usage.InputTokens} in, {usage.OutputTokens} out, {usage.TotalTokens} total. Latency: {agentStopwatch.Elapsed.TotalSeconds:F1}s. Est. Cost: ${modelCall.EstimatedCost:F5})",
                RunTelemetry.BuildUsagePayload(modelCall),
                cancellationToken);

            await consoleEvents.WriteAsync(
                taskRun.Id,
                agentRun.Id,
                agentDefinition.Type == AgentType.Planner ? ConsoleEventType.PlanCreated : ConsoleEventType.AgentCompleted,
                $"{agentDefinition.Name} completed",
                RunTelemetry.BuildAgentResultPayload(agentRun),
                cancellationToken);

            return new AgentOutput(agentDefinition.Name, agentDefinition.Type, response.Content);
        }
        catch (ProviderException exception)
        {
            agentStopwatch.Stop();
            modelCall.Status = ModelCallStatus.Failed;
            modelCall.CompletedAt = DateTimeOffset.UtcNow;
            modelCall.LatencyMs = agentStopwatch.ElapsedMilliseconds;
            modelCall.ErrorCode = exception.ErrorCode;
            modelCall.ErrorMessage = InputSanitizer.Redact(exception.Message);

            agentRun.Status = AgentRunStatus.Failed;
            agentRun.CompletedAt = DateTimeOffset.UtcNow;
            agentRun.LatencyMs = agentStopwatch.ElapsedMilliseconds;
            agentRun.ErrorMessage = InputSanitizer.Redact(exception.Message);

            await dbContext.SaveChangesAsync(cancellationToken);

            await consoleEvents.WriteAsync(
                taskRun.Id,
                agentRun.Id,
                ConsoleEventType.AgentFailed,
                $"{agentDefinition.Name} failed: {exception.Message}",
                RunTelemetry.BuildErrorPayload(exception),
                cancellationToken);

            throw;
        }
        catch (OperationCanceledException)
        {
            agentStopwatch.Stop();
            modelCall.Status = ModelCallStatus.Cancelled;
            modelCall.CompletedAt = DateTimeOffset.UtcNow;
            modelCall.LatencyMs = agentStopwatch.ElapsedMilliseconds;

            agentRun.Status = AgentRunStatus.Cancelled;
            agentRun.CompletedAt = DateTimeOffset.UtcNow;
            agentRun.LatencyMs = agentStopwatch.ElapsedMilliseconds;
            agentRun.ErrorMessage = "Cancelled";

            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// At most one extra Coder pass after Reviewer when findings look actionable.
    /// Returns the next executionOrder value (bumped if a fix loop ran).
    /// </summary>
    private async Task<int> MaybeRunReviewerFixLoopAsync(
        TaskRun taskRun,
        IReadOnlyDictionary<AgentType, AgentDefinition> definitions,
        List<AgentOutput> previousOutputs,
        int executionOrder,
        string? skillsBlock,
        string? workspacePath,
        string? reviewerContent,
        CancellationToken cancellationToken)
    {
        if (!ReviewerFixLoopPolicy.ShouldRunFixLoop(reviewerContent))
        {
            await consoleEvents.WriteAsync(
                taskRun.Id,
                null,
                ConsoleEventType.AgentStep,
                "Fix loop skipped (no findings).",
                null,
                cancellationToken);
            return executionOrder;
        }

        if (!definitions.TryGetValue(AgentType.Coder, out var coderDefinition))
        {
            await consoleEvents.WriteAsync(
                taskRun.Id,
                null,
                ConsoleEventType.Warning,
                "Fix loop skipped: Coder agent is disabled or not configured.",
                null,
                cancellationToken);
            return executionOrder;
        }

        if (string.IsNullOrWhiteSpace(workspacePath)
            || !WorkspacePathGuard.TryResolve(WorkspaceRoot, workspacePath, out var fixExportRoot))
        {
            await consoleEvents.WriteAsync(
                taskRun.Id,
                null,
                ConsoleEventType.Warning,
                "Fix loop skipped: workspace path is missing or outside the workspace root.",
                null,
                cancellationToken);
            return executionOrder;
        }

        await dbContext.Entry(taskRun).ReloadAsync(cancellationToken);
        if (taskRun.Status == TaskRunStatus.Cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        await consoleEvents.WriteAsync(
            taskRun.Id,
            null,
            ConsoleEventType.AgentStep,
            "Fix loop started: Coder will address Reviewer findings (single pass).",
            null,
            cancellationToken);

        var fixObjective = ReviewerFixLoopPolicy.BuildFixLoopObjective(reviewerContent ?? string.Empty);
        var fixOutput = await coderToolLoop.RunAsync(
            taskRun,
            coderDefinition,
            previousOutputs,
            executionOrder,
            skillsBlock,
            fixExportRoot,
            cancellationToken,
            objectiveOverride: fixObjective,
            displayNameSuffix: " (fix loop)");

        previousOutputs.Add(fixOutput);
        return executionOrder + 1;
    }

    private async Task RecalculateTaskTotalsAsync(TaskRun taskRun, CancellationToken cancellationToken)
    {
        var totalRows = await dbContext.ModelCallLogs
            .Where(x => x.TaskRunId == taskRun.Id)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Input = group.Sum(x => x.InputTokens),
                Output = group.Sum(x => x.OutputTokens),
                Total = group.Sum(x => x.TotalTokens)
            })
            .ToListAsync(cancellationToken);
        var totals = totalRows.SingleOrDefault();

        taskRun.TotalInputTokens = totals?.Input ?? 0;
        taskRun.TotalOutputTokens = totals?.Output ?? 0;
        taskRun.TotalTokens = totals?.Total ?? 0;
    }

    /// <summary>
    /// After an <see cref="OperationCanceledException"/>: a task whose DB status became
    /// Cancelled was stopped by the user (finalize + ACK). If the status is anything else
    /// and our token fired, the cancellation came from host shutdown — the message must be
    /// NACK'ed and redelivered. An OCE without token cancellation (e.g. a stray provider
    /// timeout) is finalized as Cancelled rather than requeued, so it cannot loop forever.
    /// </summary>
    public static bool ShouldRequeueAfterCancellation(TaskRunStatus statusAfterReload, bool tokenCancelled) =>
        statusAfterReload != TaskRunStatus.Cancelled && tokenCancelled;

}
