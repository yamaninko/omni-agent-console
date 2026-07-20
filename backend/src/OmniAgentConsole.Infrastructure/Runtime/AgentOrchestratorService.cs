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

    // Upper bound on chat completions in one Coder tool loop; each iteration
    // typically writes one or two files, so this comfortably covers the 50-file
    // export budget while stopping a model that never converges.
    private const int MaxToolIterations = 24;

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
    private readonly IModelRouter modelRouter;
    private readonly ITokenUsageExtractor tokenUsageExtractor;

    public AgentOrchestratorService(
        AgentConsoleDbContext dbContext,
        IConsoleEventService consoleEvents,
        ModelChainExecutor chainExecutor,
        IModelRouter modelRouter,
        ITokenUsageExtractor tokenUsageExtractor)
    {
        this.dbContext = dbContext;
        this.consoleEvents = consoleEvents;
        this.chainExecutor = chainExecutor;
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

        await consoleEvents.WriteAsync(
            taskRun.Id,
            null,
            ConsoleEventType.TaskStarted,
            $"Task execution started with prompt: \"{taskRun.InputPrompt}\"",
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
        var skillsBlock = AgentPromptBuilder.BuildSkillsBlock(appliedSkills);

        if (appliedSkills.Count > 0)
        {
            await consoleEvents.WriteAsync(
                taskRun.Id,
                null,
                ConsoleEventType.AgentStep,
                $"Applied {appliedSkills.Count} skill(s): {string.Join(", ", appliedSkills.Select(skill => skill.Name))}",
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
                    output = await RunCoderToolLoopAsync(
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
    /// Runs the Coder as an agentic tool loop: the model calls write_file /
    /// read_file / list_files to build the project incrementally, each iteration
    /// being one short chat completion. Falls back to markdown fence scraping
    /// when the model never uses the tools (e.g. no function-calling support).
    /// </summary>
    private async Task<AgentOutput> RunCoderToolLoopAsync(
        TaskRun taskRun,
        AgentDefinition agentDefinition,
        IReadOnlyList<AgentOutput> previousOutputs,
        int executionOrder,
        string? skillsBlock,
        string exportRoot,
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
            ConsoleEventType.AgentStarted,
            $"{agentDefinition.Name} started using model: {route.Model} (agentic tool loop, max {MaxToolIterations} iterations)",
            RunTelemetry.BuildAgentPayload(agentDefinition, route),
            cancellationToken);

        var messages = new List<ChatMessage>(AgentPromptBuilder.BuildMessages(taskRun, agentDefinition, previousOutputs, skillsBlock));
        agentRun.Input = RunTelemetry.TrimForStorage(InputSanitizer.Redact(messages.Last().Content), 24000);
        await dbContext.SaveChangesAsync(cancellationToken);

        var metadata = AgentPromptBuilder.BuildRequestMetadata(taskRun, agentRun, agentDefinition);
        var tools = new AgentWorkspaceTools(exportRoot);
        var agentStopwatch = Stopwatch.StartNew();
        ModelCallLog modelCall = null!;
        var iterationStopwatch = new Stopwatch();

        // Start each iteration's chain from the model that last succeeded, so a
        // dead primary is not re-tried on every single iteration.
        var activeModel = route.Model;

        try
        {
            string? finalContent = null;

            for (var iteration = 1; iteration <= MaxToolIterations && finalContent is null; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Safety net alongside the Redis cancel broadcast: even if the
                // broadcast is lost, a user cancel is honored within one iteration.
                await dbContext.Entry(taskRun).ReloadAsync(cancellationToken);
                if (taskRun.Status == TaskRunStatus.Cancelled)
                {
                    throw new OperationCanceledException();
                }

                var modelRequest = new ModelRequest(
                    route.Provider,
                    activeModel,
                    messages,
                    agentDefinition.Temperature,
                    agentDefinition.MaxTokens,
                    agentDefinition.TimeoutSeconds,
                    metadata,
                    AgentWorkspaceTools.Definitions);

                modelCall = new ModelCallLog
                {
                    TaskRunId = taskRun.Id,
                    AgentRunId = agentRun.Id,
                    Provider = route.Provider,
                    Model = activeModel,
                    RequestType = ModelRequestType.ChatCompletion,
                    PromptHash = RunTelemetry.ComputePromptHash(messages),
                    Status = ModelCallStatus.Started,
                    StartedAt = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                dbContext.ModelCallLogs.Add(modelCall);
                await dbContext.SaveChangesAsync(cancellationToken);

                ModelResponse response;
                try
                {
                    iterationStopwatch.Restart();
                    response = await chainExecutor.ExecuteAsync(
                        modelRequest,
                        agentDefinition,
                        modelCall,
                        taskRun.Id,
                        agentRun.Id,
                        cancellationToken);
                    iterationStopwatch.Stop();
                }
                catch (ProviderException exception) when (tools.WrittenFiles.Count > 0)
                {
                    // The whole chain went down mid-loop, but files are already on
                    // disk — finish with what exists instead of failing the task.
                    iterationStopwatch.Stop();
                    modelCall.Status = ModelCallStatus.Failed;
                    modelCall.CompletedAt = DateTimeOffset.UtcNow;
                    modelCall.LatencyMs = iterationStopwatch.ElapsedMilliseconds;
                    modelCall.ErrorCode = exception.ErrorCode;
                    modelCall.ErrorMessage = InputSanitizer.Redact(exception.Message);
                    await dbContext.SaveChangesAsync(CancellationToken.None);

                    await consoleEvents.WriteAsync(
                        taskRun.Id,
                        agentRun.Id,
                        ConsoleEventType.Warning,
                        $"Model chain failed at iteration {iteration} ({exception.ErrorCode}); finishing with the {tools.WrittenFiles.Count} file(s) already written.",
                        RunTelemetry.BuildErrorPayload(exception),
                        cancellationToken);

                    finalContent = $"The model provider became unavailable at iteration {iteration}; " +
                                   "the project files listed below had already been written.";
                    break;
                }

                var usage = response.TotalTokens.HasValue
                    ? tokenUsageExtractor.Extract(response)
                    : tokenUsageExtractor.Estimate(modelRequest, response);

                modelCall.Status = ModelCallStatus.Succeeded;
                modelCall.CompletedAt = DateTimeOffset.UtcNow;
                modelCall.LatencyMs = iterationStopwatch.ElapsedMilliseconds;
                modelCall.InputTokens = usage.InputTokens;
                modelCall.OutputTokens = usage.OutputTokens;
                modelCall.TotalTokens = usage.TotalTokens;
                modelCall.RawMetadataJson = response.RawMetadataJson;
                modelCall.EstimatedCost = RunTelemetry.CalculateEstimatedCost(modelCall.Model, usage.InputTokens, usage.OutputTokens);
                await dbContext.SaveChangesAsync(cancellationToken);

                activeModel = modelCall.Model;

                if (response.ToolCalls is not { Count: > 0 })
                {
                    finalContent = response.Content;
                    break;
                }

                await consoleEvents.WriteAsync(
                    taskRun.Id,
                    agentRun.Id,
                    ConsoleEventType.AgentStep,
                    $"Iteration {iteration}: {modelCall.Model} requested {response.ToolCalls.Count} tool call(s) ({iterationStopwatch.Elapsed.TotalSeconds:F1}s)",
                    null,
                    cancellationToken);

                messages.Add(new ChatMessage("assistant", response.Content ?? string.Empty, response.ToolCalls));

                foreach (var toolCall in response.ToolCalls)
                {
                    var result = tools.Execute(toolCall.Name, toolCall.ArgumentsJson);

                    await consoleEvents.WriteAsync(
                        taskRun.Id,
                        agentRun.Id,
                        result.Success ? ConsoleEventType.AgentStep : ConsoleEventType.Warning,
                        result.Success
                            ? $"🔧 {DescribeToolSuccess(toolCall.Name, result)}"
                            : $"🔧 {toolCall.Name} rejected: {result.Output}",
                        null,
                        cancellationToken);

                    messages.Add(new ChatMessage("tool", result.Output, null, toolCall.Id));
                }
            }

            agentStopwatch.Stop();

            finalContent ??= $"Stopped after reaching the {MaxToolIterations}-iteration limit; " +
                             $"{tools.WrittenFiles.Count} file(s) were written before the cutoff.";

            if (tools.WrittenFiles.Count == 0)
            {
                // Model ignored the tools (or has no function-calling support):
                // salvage whatever fenced code the final answer contains.
                var (written, skipped) = CodeBlockExporter.Export(finalContent, exportRoot);
                var skippedNote = skipped > 0 ? $" ({skipped} block(s) skipped by safety limits)" : string.Empty;
                await consoleEvents.WriteAsync(
                    taskRun.Id,
                    agentRun.Id,
                    written > 0 ? ConsoleEventType.AgentStep : ConsoleEventType.Warning,
                    $"Model used no filesystem tools; exported {written} file(s) from markdown blocks instead{skippedNote}",
                    null,
                    cancellationToken);
            }
            else
            {
                await consoleEvents.WriteAsync(
                    taskRun.Id,
                    agentRun.Id,
                    ConsoleEventType.AgentStep,
                    $"Coder wrote {tools.WrittenFiles.Count} file(s) via tools: {string.Join(", ", tools.WrittenFiles)}",
                    null,
                    cancellationToken);

                finalContent += "\n\nFiles written to workspace:\n" +
                                string.Join('\n', tools.WrittenFiles.Select(file => $"- {file}"));
            }

            agentRun.Output = InputSanitizer.Redact(finalContent);
            agentRun.Status = AgentRunStatus.Completed;
            agentRun.CompletedAt = DateTimeOffset.UtcNow;
            agentRun.LatencyMs = agentStopwatch.ElapsedMilliseconds;
            await dbContext.SaveChangesAsync(cancellationToken);

            await consoleEvents.WriteAsync(
                taskRun.Id,
                agentRun.Id,
                ConsoleEventType.AgentCompleted,
                $"{agentDefinition.Name} completed ({tools.WrittenFiles.Count} file(s), {agentStopwatch.Elapsed.TotalSeconds:F1}s total)",
                RunTelemetry.BuildAgentResultPayload(agentRun),
                cancellationToken);

            return new AgentOutput(agentDefinition.Name, agentDefinition.Type, finalContent);
        }
        catch (ProviderException exception)
        {
            agentStopwatch.Stop();
            if (modelCall is not null && modelCall.Status == ModelCallStatus.Started)
            {
                modelCall.Status = ModelCallStatus.Failed;
                modelCall.CompletedAt = DateTimeOffset.UtcNow;
                modelCall.LatencyMs = iterationStopwatch.ElapsedMilliseconds;
                modelCall.ErrorCode = exception.ErrorCode;
                modelCall.ErrorMessage = InputSanitizer.Redact(exception.Message);
            }

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
            if (modelCall is not null && modelCall.Status == ModelCallStatus.Started)
            {
                modelCall.Status = ModelCallStatus.Cancelled;
                modelCall.CompletedAt = DateTimeOffset.UtcNow;
                modelCall.LatencyMs = iterationStopwatch.ElapsedMilliseconds;
            }

            agentRun.Status = AgentRunStatus.Cancelled;
            agentRun.CompletedAt = DateTimeOffset.UtcNow;
            agentRun.LatencyMs = agentStopwatch.ElapsedMilliseconds;
            agentRun.ErrorMessage = "Cancelled";
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private static string DescribeToolSuccess(string toolName, ToolExecutionResult result)
    {
        return toolName switch
        {
            "write_file" => result.Output,
            "read_file" => $"Read {result.AffectedPath}",
            "list_files" => "Listed workspace files",
            _ => $"{toolName} succeeded"
        };
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
