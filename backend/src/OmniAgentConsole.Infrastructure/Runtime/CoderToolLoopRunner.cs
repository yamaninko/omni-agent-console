using System.Diagnostics;
using OmniAgentConsole.Application.Agents;
using OmniAgentConsole.Application.Providers;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Infrastructure.Runtime;

/// <summary>
/// Runs the Coder as an agentic tool loop: the model calls write_file /
/// read_file / list_files to build the project incrementally, each iteration
/// being one short chat completion. Falls back to markdown fence scraping
/// when the model never uses the tools (e.g. no function-calling support).
/// </summary>
public sealed class CoderToolLoopRunner
{
    // Upper bound on chat completions in one Coder tool loop; each iteration
    // typically writes one or two files, so this comfortably covers the 50-file
    // export budget while stopping a model that never converges.
    private const int MaxToolIterations = 24;

    private readonly AgentConsoleDbContext dbContext;
    private readonly IConsoleEventService consoleEvents;
    private readonly IModelRouter modelRouter;
    private readonly ITokenUsageExtractor tokenUsageExtractor;
    private readonly ModelChainExecutor chainExecutor;

    public CoderToolLoopRunner(
        AgentConsoleDbContext dbContext,
        IConsoleEventService consoleEvents,
        IModelRouter modelRouter,
        ITokenUsageExtractor tokenUsageExtractor,
        ModelChainExecutor chainExecutor)
    {
        this.dbContext = dbContext;
        this.consoleEvents = consoleEvents;
        this.modelRouter = modelRouter;
        this.tokenUsageExtractor = tokenUsageExtractor;
        this.chainExecutor = chainExecutor;
    }

    public async Task<AgentOutput> RunAsync(
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
}
