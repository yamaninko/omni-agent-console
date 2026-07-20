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
    private const int MaxExportFiles = 50;
    private const int MaxExportFileChars = 1_000_000;

    // Upper bound on chat completions in one Coder tool loop; each iteration
    // typically writes one or two files, so this comfortably covers MaxExportFiles
    // while stopping a model that never converges.
    private const int MaxToolIterations = 24;

    // Matches "// filepath: src/app.ts" style annotations (also #, <!--, /*) at line start.
    private static readonly System.Text.RegularExpressions.Regex FilepathMarkerRegex = new(
        @"(?m)^[ \t]*(?://|#|<!--|/\*)\s*(?:file:|filename:|filepath:)\s*([a-zA-Z0-9_\-\./\\]+\.[a-zA-Z0-9_]+)[^\r\n]*",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly AgentType[] DefaultSequence =
    [
        AgentType.Planner,
        AgentType.Research,
        AgentType.Coder,
        AgentType.Reviewer,
        AgentType.OpsMonitor
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> ValidExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "js", "ts", "json", "go", "cs", "py", "html", "css", "yml", "yaml", "sh", "bash", "md", 
        "dockerfile", "txt", "sql", "conf", "ini", "rs", "c", "cpp", "h", "hpp", "java", "kt", "rb", "php"
    };
    private readonly AgentConsoleDbContext dbContext;
    private readonly IConsoleEventService consoleEvents;
    private readonly IModelProvider modelProvider;
    private readonly IModelRouter modelRouter;
    private readonly ITokenUsageExtractor tokenUsageExtractor;

    public AgentOrchestratorService(
        AgentConsoleDbContext dbContext,
        IConsoleEventService consoleEvents,
        IModelProvider modelProvider,
        IModelRouter modelRouter,
        ITokenUsageExtractor tokenUsageExtractor)
    {
        this.dbContext = dbContext;
        this.consoleEvents = consoleEvents;
        this.modelProvider = modelProvider;
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

        var (workspacePath, requestedSkillIds) = ParseTaskContext(taskRun.InputContextJson);
        var appliedSkills = await dbContext.SkillDefinitions
            .AsNoTracking()
            .Where(skill => skill.Enabled && requestedSkillIds.Contains(skill.Id))
            .OrderBy(skill => skill.SortOrder)
            .ThenBy(skill => skill.Name)
            .ToListAsync(cancellationToken);
        var skillsBlock = BuildSkillsBlock(appliedSkills);

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
                BuildTaskPayload(taskRun),
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
                BuildErrorPayload(exception),
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
            ConfigSnapshotJson = BuildAgentConfigPayload(agentDefinition, route)
        };

        dbContext.AgentRuns.Add(agentRun);
        await dbContext.SaveChangesAsync(cancellationToken);

        await consoleEvents.WriteAsync(
            taskRun.Id,
            agentRun.Id,
            agentDefinition.Type == AgentType.Planner ? ConsoleEventType.PlannerStarted : ConsoleEventType.AgentStarted,
            $"{agentDefinition.Name} started using model: {route.Model}",
            BuildAgentPayload(agentDefinition, route),
            cancellationToken);

        var messages = BuildMessages(taskRun, agentDefinition, previousOutputs, skillsBlock);
        agentRun.Input = TrimForStorage(InputSanitizer.Redact(messages.Last().Content), 24000);
        await dbContext.SaveChangesAsync(cancellationToken);

        var modelRequest = new ModelRequest(
            route.Provider,
            route.Model,
            messages,
            agentDefinition.Temperature,
            agentDefinition.MaxTokens,
            agentDefinition.TimeoutSeconds,
            BuildRequestMetadata(taskRun, agentRun, agentDefinition));

        var modelCall = new ModelCallLog
        {
            TaskRunId = taskRun.Id,
            AgentRunId = agentRun.Id,
            Provider = route.Provider,
            Model = route.Model,
            RequestType = ModelRequestType.ChatCompletion,
            PromptHash = ComputePromptHash(messages),
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
            BuildModelCallPayload(modelCall),
            cancellationToken);

        var agentStopwatch = Stopwatch.StartNew();
        try
        {
            var response = await ExecuteWithModelChainAsync(
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
            modelCall.EstimatedCost = CalculateEstimatedCost(modelCall.Model, usage.InputTokens, usage.OutputTokens);

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
                BuildUsagePayload(modelCall),
                cancellationToken);

            await consoleEvents.WriteAsync(
                taskRun.Id,
                agentRun.Id,
                agentDefinition.Type == AgentType.Planner ? ConsoleEventType.PlanCreated : ConsoleEventType.AgentCompleted,
                $"{agentDefinition.Name} completed",
                BuildAgentResultPayload(agentRun),
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
                BuildErrorPayload(exception),
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
            ConfigSnapshotJson = BuildAgentConfigPayload(agentDefinition, route)
        };

        dbContext.AgentRuns.Add(agentRun);
        await dbContext.SaveChangesAsync(cancellationToken);

        await consoleEvents.WriteAsync(
            taskRun.Id,
            agentRun.Id,
            ConsoleEventType.AgentStarted,
            $"{agentDefinition.Name} started using model: {route.Model} (agentic tool loop, max {MaxToolIterations} iterations)",
            BuildAgentPayload(agentDefinition, route),
            cancellationToken);

        var messages = new List<ChatMessage>(BuildMessages(taskRun, agentDefinition, previousOutputs, skillsBlock));
        agentRun.Input = TrimForStorage(InputSanitizer.Redact(messages.Last().Content), 24000);
        await dbContext.SaveChangesAsync(cancellationToken);

        var metadata = BuildRequestMetadata(taskRun, agentRun, agentDefinition);
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
                    PromptHash = ComputePromptHash(messages),
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
                    response = await ExecuteWithModelChainAsync(
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
                        BuildErrorPayload(exception),
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
                modelCall.EstimatedCost = CalculateEstimatedCost(modelCall.Model, usage.InputTokens, usage.OutputTokens);
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
                var (written, skipped) = ExportCodeBlocks(finalContent, exportRoot);
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
                BuildAgentResultPayload(agentRun),
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
                BuildErrorPayload(exception),
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

    /// <summary>
    /// Walks the agent's primary → fallback model chain until one model returns a
    /// usable answer (text content or tool calls). Updates <paramref name="modelCall"/>
    /// to the model actually used so usage tracking reflects fallbacks.
    /// </summary>
    private async Task<ModelResponse> ExecuteWithModelChainAsync(
        ModelRequest modelRequest,
        AgentDefinition agentDefinition,
        ModelCallLog modelCall,
        Guid taskRunId,
        Guid agentRunId,
        CancellationToken cancellationToken)
    {
        var modelChain = BuildModelChain(modelRequest.Model, agentDefinition.FallbackModels);
        ModelResponse response = null!;

        for (var chainIndex = 0; chainIndex < modelChain.Count; chainIndex++)
        {
            var chainModel = modelChain[chainIndex];

            if (chainIndex > 0)
            {
                modelCall.Model = chainModel;
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }

            try
            {
                response = await ExecuteModelCallWithRetryAsync(
                    modelRequest with { Model = chainModel },
                    agentDefinition.RetryCount,
                    taskRunId,
                    agentRunId,
                    cancellationToken);

                // Congested free-tier endpoints occasionally return HTTP 200 with
                // zero completion tokens; no text and no tool calls is a failure.
                if (string.IsNullOrWhiteSpace(response.Content) && response.ToolCalls is not { Count: > 0 })
                {
                    throw new ProviderException(
                        ProviderErrorCode.UnknownError,
                        $"Model {chainModel} returned an empty response (0 completion tokens).");
                }

                break;
            }
            catch (ProviderException exception) when (
                chainIndex < modelChain.Count - 1 && ShouldFallbackToNextModel(exception.ErrorCode))
            {
                await consoleEvents.WriteAsync(
                    taskRunId,
                    agentRunId,
                    ConsoleEventType.Warning,
                    $"Model {chainModel} failed ({exception.ErrorCode}: {exception.Message}); falling back to {modelChain[chainIndex + 1]}.",
                    BuildErrorPayload(exception),
                    CancellationToken.None);
            }
        }

        return response;
    }

    private static Dictionary<string, string> BuildRequestMetadata(
        TaskRun taskRun,
        AgentRun agentRun,
        AgentDefinition agentDefinition)
    {
        return new Dictionary<string, string>
        {
            ["taskRunId"] = taskRun.Id.ToString(),
            ["agentRunId"] = agentRun.Id.ToString(),
            ["agentType"] = agentDefinition.Type.ToString(),
            ["customApiUrl"] = agentDefinition.ApiCredential != null ? (agentDefinition.ApiCredential.BaseUrl ?? "") : (agentDefinition.CustomApiUrl ?? ""),
            ["customApiKey"] = agentDefinition.ApiCredential != null ? (agentDefinition.ApiCredential.ApiKey ?? "") : (agentDefinition.CustomApiKey ?? ""),
            ["provider"] = agentDefinition.ApiCredential != null ? (agentDefinition.ApiCredential.Provider ?? "") : agentDefinition.Provider.ToString()
        };
    }

    private async Task<ModelResponse> ExecuteModelCallWithRetryAsync(
        ModelRequest request,
        int retryCount,
        Guid taskRunId,
        Guid agentRunId,
        CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(0, retryCount);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await modelProvider.CreateChatCompletionAsync(request, cancellationToken);
            }
            catch (ProviderException exception) when (attempt < maxRetries && IsTransient(exception.ErrorCode))
            {
                var delay = TimeSpan.FromMilliseconds(Math.Min(4000, 500 * Math.Pow(2, attempt)));
                await consoleEvents.WriteAsync(
                    taskRunId,
                    agentRunId,
                    ConsoleEventType.Warning,
                    $"Provider call retry {attempt + 1}/{maxRetries} after {exception.ErrorCode}.",
                    BuildErrorPayload(exception),
                    cancellationToken);
                await Task.Delay(delay, cancellationToken);
            }
        }
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

    private static IReadOnlyList<ChatMessage> BuildMessages(
        TaskRun taskRun,
        AgentDefinition agentDefinition,
        IReadOnlyList<AgentOutput> previousOutputs,
        string? skillsBlock)
    {
        var systemPromptParts = new List<string>
        {
            agentDefinition.SystemPrompt.Trim(),
            GetRoleInstruction(agentDefinition.Type)
        };

        if (!string.IsNullOrWhiteSpace(skillsBlock))
        {
            systemPromptParts.Add(skillsBlock);
        }

        systemPromptParts.Add("Respond in the same language as the user prompt unless the user asks otherwise. Keep output concise and actionable.");

        var systemPrompt = string.Join("\n\n", systemPromptParts);

        var userBuilder = new StringBuilder();
        userBuilder.AppendLine("User task:");
        userBuilder.AppendLine(taskRun.InputPrompt.Trim());

        if (!string.IsNullOrWhiteSpace(taskRun.InputContextJson))
        {
            userBuilder.AppendLine();
            userBuilder.AppendLine("Input context JSON:");
            userBuilder.AppendLine(TrimForPrompt(taskRun.InputContextJson, 6000));
        }

        if (previousOutputs.Count > 0)
        {
            userBuilder.AppendLine();
            userBuilder.AppendLine("Previous agent outputs:");
            foreach (var output in previousOutputs)
            {
                userBuilder.AppendLine($"[{output.Name} / {output.Type}]");
                userBuilder.AppendLine(TrimForPrompt(output.Content, 6000));
                userBuilder.AppendLine();
            }
        }

        userBuilder.AppendLine();
        userBuilder.AppendLine("Current agent objective:");
        userBuilder.AppendLine(GetObjective(agentDefinition.Type));

        return
        [
            new ChatMessage("system", systemPrompt),
            new ChatMessage("user", userBuilder.ToString())
        ];
    }

    private static string GetRoleInstruction(AgentType agentType)
    {
        return agentType switch
        {
            AgentType.Planner => "Create an execution plan. Include selected agents, ordered steps, assumptions, and model suitability notes.",
            AgentType.Research => "Analyze only supplied prompt/context. Extract useful facts, unknowns, constraints, and follow-up research needs.",
            AgentType.Coder => "Build the project directly in the workspace using the provided filesystem tools. Call write_file once per file with the complete file content; use list_files/read_file to check your work. Always include a README.md. You cannot execute code, run tests, or use a shell — do not create scratch/check scripts, and do not rewrite a file unless you are fixing a concrete mistake. When every file is written, reply with a short plain-text summary of the project (no code blocks). Only if no tools are available: emit one fenced code block per file, tagged with a first-line comment like // filepath: path/to/file.go.",
            AgentType.Reviewer => "Review previous outputs for correctness, security, consistency, missing steps, and architectural fit. Return prioritized findings and concrete fixes.",
            AgentType.OpsMonitor => "Summarize execution health, usage signals, latency considerations, and operational risks from the previous outputs.",
            _ => "Complete the assigned agent role using the supplied context."
        };
    }

    private static string GetObjective(AgentType agentType)
    {
        return agentType switch
        {
            AgentType.Planner => "Produce the MVP execution plan for this task.",
            AgentType.Research => "Produce research notes and relevant context.",
            AgentType.Coder => "Produce the technical output requested by the user.",
            AgentType.Reviewer => "Review the previous outputs and suggest corrections.",
            AgentType.OpsMonitor => "Produce a short operational summary for this run.",
            _ => "Produce the requested agent output."
        };
    }

    private static string ComputePromptHash(IReadOnlyList<ChatMessage> messages)
    {
        var prompt = string.Join('\n', messages.Select(message => $"{message.Role}:{message.Content}"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(prompt));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsTransient(ProviderErrorCode errorCode)
    {
        return errorCode is ProviderErrorCode.RateLimit
            or ProviderErrorCode.Timeout
            or ProviderErrorCode.ProviderUnavailable
            or ProviderErrorCode.UnknownError;
    }

    private static string TrimForPrompt(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, maxLength), "\n[truncated]");
    }

    private static string TrimForStorage(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "\n[truncated]");
    }

    private static string BuildAgentConfigPayload(AgentDefinition agentDefinition, ModelRoute route)
    {
        return JsonSerializer.Serialize(new
        {
            agentDefinition.Name,
            Type = agentDefinition.Type.ToString(),
            Provider = route.Provider.ToString(),
            route.Model,
            agentDefinition.MaxTokens,
            agentDefinition.Temperature,
            agentDefinition.TimeoutSeconds,
            agentDefinition.RetryCount
        }, JsonOptions);
    }

    private static string BuildAgentPayload(AgentDefinition agentDefinition, ModelRoute route)
    {
        return JsonSerializer.Serialize(new
        {
            agentDefinition.Id,
            agentDefinition.Name,
            Type = agentDefinition.Type.ToString(),
            Provider = route.Provider.ToString(),
            route.Model
        }, JsonOptions);
    }

    private static string BuildModelCallPayload(ModelCallLog modelCall)
    {
        return JsonSerializer.Serialize(new
        {
            modelCall.Id,
            Provider = modelCall.Provider.ToString(),
            modelCall.Model,
            Status = modelCall.Status.ToString()
        }, JsonOptions);
    }

    private static string BuildUsagePayload(ModelCallLog modelCall)
    {
        return JsonSerializer.Serialize(new
        {
            modelCall.Id,
            Provider = modelCall.Provider.ToString(),
            modelCall.Model,
            modelCall.InputTokens,
            modelCall.OutputTokens,
            modelCall.TotalTokens,
            modelCall.LatencyMs,
            Status = modelCall.Status.ToString()
        }, JsonOptions);
    }

    private static string BuildAgentResultPayload(AgentRun agentRun)
    {
        return JsonSerializer.Serialize(new
        {
            agentRun.Id,
            agentRun.AgentName,
            AgentType = agentRun.AgentType.ToString(),
            Status = agentRun.Status.ToString(),
            agentRun.LatencyMs,
            agentRun.Output
        }, JsonOptions);
    }

    private static string BuildTaskPayload(TaskRun taskRun)
    {
        return JsonSerializer.Serialize(new
        {
            taskRun.Id,
            Status = taskRun.Status.ToString(),
            taskRun.TotalInputTokens,
            taskRun.TotalOutputTokens,
            taskRun.TotalTokens,
            taskRun.TotalLatencyMs
        }, JsonOptions);
    }

    private static string BuildErrorPayload(Exception exception)
    {
        return JsonSerializer.Serialize(new
        {
            error = exception.Message,
            errorCode = exception is ProviderException providerException
                ? providerException.ErrorCode.ToString()
                : ProviderErrorCode.UnknownError.ToString()
        }, JsonOptions);
    }

    private static decimal CalculateEstimatedCost(string model, int inputTokens, int outputTokens)
    {
        var pricing = new Dictionary<string, (decimal Input, decimal Output)>(StringComparer.OrdinalIgnoreCase)
        {
            ["meta/llama-3.1-8b-instruct"] = (0.05m, 0.05m),
            ["meta/llama-3.1-70b-instruct"] = (0.52m, 0.52m),
            ["meta/llama-3.1-405b-instruct"] = (2.66m, 2.66m),
            ["omniagent/nemotron-4-340b-instruct"] = (0.50m, 0.50m),
            ["mistralai/mixtral-8x22b-instruct-v0.1"] = (0.30m, 0.30m)
        };

        var (inputCostPerMillion, outputCostPerMillion) = pricing.TryGetValue(model, out var cost) 
            ? cost 
            : (0.05m, 0.05m);

        decimal inputCost = (inputTokens / 1_000_000m) * inputCostPerMillion;
        decimal outputCost = (outputTokens / 1_000_000m) * outputCostPerMillion;

        return inputCost + outputCost;
    }

    // workspacePath must already be validated by WorkspacePathGuard; filenames from
    // model output are re-validated here because they can contain traversal attempts.
    // Public for unit testing.
    public static (int Written, int Skipped) ExportCodeBlocks(string content, string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || string.IsNullOrWhiteSpace(content)) return (0, 0);

        try
        {
            if (!Directory.Exists(workspacePath))
            {
                Directory.CreateDirectory(workspacePath);
            }
        }
        catch { return (0, 0); }

        var matches = System.Text.RegularExpressions.Regex.Matches(
            content, 
            @"(?s)```([a-zA-Z0-9_-]*)\r?\n(.*?)\r?\n```"
        );

        if (matches.Count == 0)
        {
            // Models frequently emit multi-file output WITHOUT markdown fences, as a
            // stream of "// filepath: ..." annotated sections. Split on those markers
            // so each section lands in its own file instead of one concatenated blob.
            var markers = FilepathMarkerRegex.Matches(content);
            if (markers.Count >= 2)
            {
                var sectionWritten = 0;
                var sectionSkipped = 0;
                for (var i = 0; i < markers.Count; i++)
                {
                    var start = markers[i].Index + markers[i].Length;
                    var end = i + 1 < markers.Count ? markers[i + 1].Index : content.Length;
                    var body = content[start..end].Trim('\r', '\n');
                    var relativePath = markers[i].Groups[1].Value.Trim();

                    if (sectionWritten >= MaxExportFiles || body.Length == 0 || body.Length > MaxExportFileChars
                        || !WorkspacePathGuard.TryResolve(workspacePath, relativePath, out var sectionPath))
                    {
                        sectionSkipped++;
                        continue;
                    }

                    try
                    {
                        var sectionDir = Path.GetDirectoryName(sectionPath);
                        if (!string.IsNullOrEmpty(sectionDir) && !Directory.Exists(sectionDir))
                        {
                            Directory.CreateDirectory(sectionDir);
                        }

                        File.WriteAllText(sectionPath, body + "\n");
                        sectionWritten++;
                    }
                    catch
                    {
                        sectionSkipped++;
                    }
                }

                return (sectionWritten, sectionSkipped);
            }

            string filename = "README.md";

            var filepathMatch = markers.Count == 1 ? markers[0] : null;
            if (filepathMatch is not null)
            {
                var extracted = Path.GetFileName(filepathMatch.Groups[1].Value.Trim());
                if (IsValidFilename(extracted))
                {
                    filename = extracted;
                }
            }
            else if (content.Contains("def ") || (content.Contains("import ") && !content.Contains("package ") && !content.Contains("func ")))
            {
                filename = "main.py";
            }
            else if (content.Contains("package ") || content.Contains("import ") || content.Contains("func "))
            {
                filename = "main.go";
            }
            else if (content.Contains("class ") || content.Contains("using System;"))
            {
                filename = "Program.cs";
            }
            else if (content.Contains("import express") || content.Contains("require("))
            {
                filename = "index.js";
            }

            if (content.Length > MaxExportFileChars || !WorkspacePathGuard.TryResolve(workspacePath, filename, out var singleFilePath))
            {
                return (0, 1);
            }

            try
            {
                File.WriteAllText(singleFilePath, content);
                return (1, 0);
            }
            catch
            {
                return (0, 1);
            }
        }

        int fileIndex = 1;
        int written = 0;
        int skippedCount = 0;
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var langTag = match.Groups[1].Value.Trim().ToLowerInvariant();
            var blockContent = match.Groups[2].Value.Trim();
            if (string.IsNullOrWhiteSpace(blockContent)) continue;

            if (written >= MaxExportFiles || blockContent.Length > MaxExportFileChars)
            {
                skippedCount++;
                continue;
            }

            string? filename = null;
            var lines = blockContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0)
            {
                var firstLine = lines[0].Trim();
                var cleanLine = firstLine
                    .Replace("//", "")
                    .Replace("/*", "")
                    .Replace("*/", "")
                    .Replace("#", "")
                    .Replace("<!--", "")
                    .Replace("-->", "")
                    .Replace("file:", "")
                    .Replace("filename:", "")
                    .Replace("filepath:", "")
                    .Trim();

                var ext = Path.GetExtension(cleanLine).TrimStart('.');
                if ((ValidExtensions.Contains(ext) || cleanLine.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)) && IsValidFilename(cleanLine))
                {
                    filename = cleanLine;
                    blockContent = string.Join(Environment.NewLine, lines.Skip(1));
                }
            }

            if (filename == null)
            {
                int blockIndex = match.Index;
                int startSearch = Math.Max(0, blockIndex - 150);
                var preceding = content.Substring(startSearch, blockIndex - startSearch);
                var fileMatches = System.Text.RegularExpressions.Regex.Matches(
                    preceding, 
                    @"[a-zA-Z0-9_\-\./\\\\]+\.[a-zA-Z0-9_]+"
                );
                
                for (int i = fileMatches.Count - 1; i >= 0; i--)
                {
                    var possibleName = fileMatches[i].Value;
                    var ext = Path.GetExtension(possibleName).TrimStart('.');
                    if ((ValidExtensions.Contains(ext) || possibleName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)) && IsValidFilename(possibleName))
                    {
                        filename = possibleName;
                        break;
                    }
                }
            }

            if (filename == null)
            {
                string ext = "txt";
                if (!string.IsNullOrEmpty(langTag))
                {
                    ext = langTag switch
                    {
                        "go" => "go",
                        "python" or "py" => "py",
                        "javascript" or "js" => "js",
                        "typescript" or "ts" => "ts",
                        "csharp" or "cs" => "cs",
                        "html" => "html",
                        "css" => "css",
                        "json" => "json",
                        "bash" or "sh" => "sh",
                        "markdown" or "md" => "md",
                        "yaml" or "yml" => "yml",
                        "sql" => "sql",
                        _ => "txt"
                    };
                }
                // No filename could be inferred; keep these fallback files out of
                // the workspace root so they don't clutter the exported project.
                filename = $"output/output_{fileIndex}.{ext}";
                fileIndex++;
            }

            if (!WorkspacePathGuard.TryResolve(workspacePath, filename, out var fullPath))
            {
                skippedCount++;
                continue;
            }

            try
            {
                var fileDir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir))
                {
                    Directory.CreateDirectory(fileDir);
                }

                File.WriteAllText(fullPath, blockContent);
                written++;
            }
            catch
            {
                skippedCount++;
            }
        }

        return (written, skippedCount);
    }

    private static bool IsValidFilename(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains(" ") || text.Length > 100) return false;
        return text.Contains('.') && !text.Contains(':') && !text.Contains('?') && !text.Contains('&');
    }

    /// <summary>
    /// Primary model first, then the comma-separated fallbacks in order,
    /// de-duplicated case-insensitively. Public for unit testing.
    /// </summary>
    public static IReadOnlyList<string> BuildModelChain(string primaryModel, string? fallbackModels)
    {
        var chain = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? model)
        {
            var trimmed = model?.Trim();
            if (!string.IsNullOrEmpty(trimmed) && seen.Add(trimmed))
            {
                chain.Add(trimmed);
            }
        }

        Add(primaryModel);
        foreach (var fallback in (fallbackModels ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            Add(fallback);
        }

        return chain;
    }

    /// <summary>
    /// A different model is worth trying for everything except auth failures —
    /// the same key is used for the whole chain, so 401 would fail everywhere.
    /// </summary>
    public static bool ShouldFallbackToNextModel(ProviderErrorCode errorCode) =>
        errorCode != ProviderErrorCode.Unauthorized;

    // Context JSON is user-controlled; unknown properties and malformed ids are ignored.
    private static (string? WorkspacePath, List<Guid> SkillIds) ParseTaskContext(string? inputContextJson)
    {
        string? workspacePath = null;
        var skillIds = new List<Guid>();

        if (string.IsNullOrWhiteSpace(inputContextJson))
        {
            return (workspacePath, skillIds);
        }

        try
        {
            using var doc = JsonDocument.Parse(inputContextJson);

            if (doc.RootElement.TryGetProperty("workspacePath", out var pathProp) && pathProp.ValueKind == JsonValueKind.String)
            {
                workspacePath = pathProp.GetString();
            }

            if (doc.RootElement.TryGetProperty("skillIds", out var skillsProp) && skillsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in skillsProp.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out var skillId))
                    {
                        skillIds.Add(skillId);
                    }
                }
            }
        }
        catch { }

        return (workspacePath, skillIds);
    }

    private static string? BuildSkillsBlock(IReadOnlyList<SkillDefinition> skills)
    {
        if (skills.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Selected project skills. These are mandatory conventions for this task; every agent must follow them and the reviewer must flag violations:");
        foreach (var skill in skills)
        {
            builder.AppendLine();
            builder.AppendLine($"### {skill.Name} ({skill.Category})");
            builder.AppendLine(TrimForPrompt(skill.Instructions, 2000));
        }

        return builder.ToString().TrimEnd();
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

    private sealed record AgentOutput(string Name, AgentType Type, string Content);
}
