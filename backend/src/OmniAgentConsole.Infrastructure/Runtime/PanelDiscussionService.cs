using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniAgentConsole.Application.Providers;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Infrastructure.Runtime;

/// <summary>
/// Single-round moderated panel: automatic floor order, ~60s per guest, fail-forward.
/// Does not touch the Studio coding pipeline.
/// </summary>
public sealed class PanelDiscussionService : IPanelDiscussionService
{
    private readonly AgentConsoleDbContext dbContext;
    private readonly IPanelEventService panelEvents;
    private readonly IModelProvider modelProvider;
    private readonly ITokenUsageExtractor tokenUsageExtractor;

    public PanelDiscussionService(
        AgentConsoleDbContext dbContext,
        IPanelEventService panelEvents,
        IModelProvider modelProvider,
        ITokenUsageExtractor tokenUsageExtractor)
    {
        this.dbContext = dbContext;
        this.panelEvents = panelEvents;
        this.modelProvider = modelProvider;
        this.tokenUsageExtractor = tokenUsageExtractor;
    }

    public async Task RunSessionAsync(Guid panelSessionId, CancellationToken cancellationToken)
    {
        var session = await dbContext.PanelSessions
            .Include(x => x.Group!)
            .ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(x => x.Id == panelSessionId, cancellationToken);

        if (session is null)
        {
            throw new InvalidOperationException($"Panel session {panelSessionId} was not found.");
        }

        if (session.Status is PanelSessionStatus.Completed or PanelSessionStatus.Cancelled)
        {
            await panelEvents.WriteAsync(
                session.Id,
                null,
                ConsoleEventType.Warning,
                $"Panel is already {session.Status}. Execution skipped.",
                null,
                cancellationToken);
            return;
        }

        var speakers = PanelDiscussionPolicy.OrderSpeakers(
            session.Group?.Members ?? Enumerable.Empty<AgentGroupMember>(),
            m => m.Enabled,
            m => m.Role,
            m => m.SortOrder,
            m => m.DisplayName,
            m => m.Id);

        if (!PanelDiscussionPolicy.CanStart(speakers.Count))
        {
            session.Status = PanelSessionStatus.Failed;
            session.CompletedAt = DateTimeOffset.UtcNow;
            session.ErrorMessage = "No enabled guests in the group.";
            await dbContext.SaveChangesAsync(cancellationToken);
            await panelEvents.WriteAsync(
                session.Id,
                null,
                ConsoleEventType.TaskFailed,
                "Panel failed: group has no enabled guests.",
                null,
                cancellationToken);
            return;
        }

        session.Status = PanelSessionStatus.Running;
        session.StartedAt ??= DateTimeOffset.UtcNow;
        session.ErrorMessage = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();

        var roster = speakers
            .Select(m => new PanelDiscussionPolicy.RosterEntry(
                m.DisplayName,
                m.Role,
                m.Stance,
                m.StanceLabel,
                PanelDiscussionPolicy.BuildMissionSummary(
                    m.DisplayName,
                    m.Role,
                    m.Stance,
                    m.StanceLabel,
                    m.SystemPrompt)))
            .ToList();

        await panelEvents.WriteAsync(
            session.Id,
            null,
            ConsoleEventType.PanelStarted,
            $"Panel started. Topic: {session.Topic}",
            JsonSerializer.Serialize(new { session.Topic, speakerCount = speakers.Count, roster }),
            cancellationToken);

        await panelEvents.WriteAsync(
            session.Id,
            null,
            ConsoleEventType.UserMessage,
            session.Topic,
            null,
            cancellationToken);

        // Visible mission card so the chat explains who is on stage before anyone speaks.
        await panelEvents.WriteAsync(
            session.Id,
            null,
            ConsoleEventType.AgentStep,
            PanelDiscussionPolicy.BuildRosterBriefing(session.Topic, roster),
            JsonSerializer.Serialize(new { kind = "roster", roster }),
            cancellationToken);

        var priorTurns = new List<(string Speaker, string Content)>();
        var anySuccess = false;

        try
        {
            for (var i = 0; i < speakers.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Belt-and-braces: user cancel writes DB status before firing the token.
                await dbContext.Entry(session).ReloadAsync(cancellationToken);
                if (session.Status == PanelSessionStatus.Cancelled)
                {
                    break;
                }

                var member = speakers[i];
                var turnOrder = i + 1;
                var ok = await RunTurnAsync(session, member, turnOrder, priorTurns, roster, cancellationToken);
                if (ok)
                {
                    anySuccess = true;
                    var last = session.Turns.OrderByDescending(t => t.TurnOrder).FirstOrDefault();
                    if (last is { Status: PanelTurnStatus.Completed, Output: not null })
                    {
                        priorTurns.Add((last.MemberDisplayName, last.Output));
                    }
                }
                else if (!PanelDiscussionPolicy.ContinueAfterTurnFailure)
                {
                    break;
                }
            }

            stopwatch.Stop();
            await dbContext.Entry(session).ReloadAsync(CancellationToken.None);

            if (session.Status == PanelSessionStatus.Cancelled)
            {
                session.CompletedAt = DateTimeOffset.UtcNow;
                session.TotalLatencyMs = stopwatch.ElapsedMilliseconds;
                session.CurrentMemberId = null;
                session.FloorDeadline = null;
                await RecalcTotalsAsync(session, CancellationToken.None);
                await dbContext.SaveChangesAsync(CancellationToken.None);
                await panelEvents.WriteAsync(
                    session.Id,
                    null,
                    ConsoleEventType.TaskCancelled,
                    "Panel cancelled.",
                    null,
                    CancellationToken.None);
                return;
            }

            session.Status = anySuccess ? PanelSessionStatus.Completed : PanelSessionStatus.Failed;
            if (!anySuccess && string.IsNullOrWhiteSpace(session.ErrorMessage))
            {
                session.ErrorMessage = "All guest turns failed.";
            }

            session.CompletedAt = DateTimeOffset.UtcNow;
            session.TotalLatencyMs = stopwatch.ElapsedMilliseconds;
            session.CurrentMemberId = null;
            session.FloorDeadline = null;
            await RecalcTotalsAsync(session, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            await panelEvents.WriteAsync(
                session.Id,
                null,
                anySuccess ? ConsoleEventType.PanelCompleted : ConsoleEventType.TaskFailed,
                anySuccess ? "Panel completed (single round)." : "Panel failed: no successful turns.",
                null,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            try
            {
                await dbContext.Entry(session).ReloadAsync(CancellationToken.None);
            }
            catch { }

            if (session.Status == PanelSessionStatus.Cancelled)
            {
                session.CompletedAt ??= DateTimeOffset.UtcNow;
                session.TotalLatencyMs = stopwatch.ElapsedMilliseconds;
                session.CurrentMemberId = null;
                session.FloorDeadline = null;
                await RecalcTotalsAsync(session, CancellationToken.None);
                await dbContext.SaveChangesAsync(CancellationToken.None);
                await panelEvents.WriteAsync(
                    session.Id,
                    null,
                    ConsoleEventType.TaskCancelled,
                    "Panel cancelled.",
                    null,
                    CancellationToken.None);
                return;
            }

            // Host shutdown: leave Running for redelivery.
            await panelEvents.WriteAsync(
                session.Id,
                null,
                ConsoleEventType.Warning,
                "Panel interrupted by worker shutdown; it will be re-queued.",
                null,
                CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            session.Status = PanelSessionStatus.Failed;
            session.CompletedAt = DateTimeOffset.UtcNow;
            session.TotalLatencyMs = stopwatch.ElapsedMilliseconds;
            session.ErrorMessage = exception.Message;
            session.CurrentMemberId = null;
            session.FloorDeadline = null;
            await RecalcTotalsAsync(session, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await panelEvents.WriteAsync(
                session.Id,
                null,
                ConsoleEventType.TaskFailed,
                $"Panel failed: {exception.Message}",
                null,
                CancellationToken.None);
        }
    }

    private async Task<bool> RunTurnAsync(
        PanelSession session,
        AgentGroupMember member,
        int turnOrder,
        IReadOnlyList<(string Speaker, string Content)> priorTurns,
        IReadOnlyList<PanelDiscussionPolicy.RosterEntry> roster,
        CancellationToken cancellationToken)
    {
        var turn = new PanelTurn
        {
            SessionId = session.Id,
            MemberId = member.Id,
            MemberDisplayName = member.DisplayName,
            TurnOrder = turnOrder,
            Status = PanelTurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        dbContext.PanelTurns.Add(turn);

        var timeoutSeconds = member.TimeoutSeconds > 0
            ? member.TimeoutSeconds
            : PanelDiscussionPolicy.DefaultTimeoutSeconds;
        session.CurrentMemberId = member.Id;
        session.FloorDeadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        await dbContext.SaveChangesAsync(cancellationToken);

        var roleLabel = member.Role == PanelMemberRole.Moderator ? "Moderator" : "Commentator";
        var stanceLabel = PanelDiscussionPolicy.DescribeStance(member.Stance, member.StanceLabel);

        await panelEvents.WriteAsync(
            session.Id,
            turn.Id,
            ConsoleEventType.PanelFloorGranted,
            $"Floor granted to {member.DisplayName} ({roleLabel}, {stanceLabel}) (~{timeoutSeconds}s).",
            JsonSerializer.Serialize(new
            {
                memberId = member.Id,
                member.DisplayName,
                role = member.Role.ToString(),
                stance = member.Stance.ToString(),
                member.StanceLabel,
                timeoutSeconds,
                floorDeadline = session.FloorDeadline
            }),
            cancellationToken);

        await panelEvents.WriteAsync(
            session.Id,
            turn.Id,
            ConsoleEventType.AgentStarted,
            $"{member.DisplayName} ({roleLabel}) is speaking…",
            null,
            cancellationToken);

        var systemPrompt = PanelDiscussionPolicy.BuildMemberSystemPrompt(
            member.SystemPrompt,
            member.Role,
            member.Stance,
            member.StanceLabel,
            member.DisplayName,
            roster);
        var userPrompt = PanelDiscussionPolicy.BuildMemberUserPrompt(
            session.Topic,
            member.Role,
            member.DisplayName,
            member.Stance,
            member.StanceLabel,
            roster,
            priorTurns);
        var messages = new List<ChatMessage>
        {
            new("system", systemPrompt),
            new("user", InputSanitizer.Redact(userPrompt))
        };

        var modelChain = ModelChainExecutor.BuildModelChain(member.DefaultModel, member.FallbackModels);
        if (modelChain.Count == 0)
        {
            return await FailTurnAsync(session, turn, "No model configured for this guest.", cancellationToken);
        }

        var maxTokens = member.MaxTokens > 0 ? member.MaxTokens : PanelDiscussionPolicy.DefaultMaxTokens;
        var retryCount = Math.Max(0, member.RetryCount);
        var credentialId = await ResolveCredentialIdAsync(member, cancellationToken);
        var metadata = new Dictionary<string, string>
        {
            ["panelSessionId"] = session.Id.ToString("D"),
            ["panelMemberId"] = member.Id.ToString("D")
        };
        if (credentialId is { } cid && cid != Guid.Empty)
        {
            // Prefer the default / bound ApiCredential (Vault secret-ref). Without this,
            // panel turns only look at secret/providers/omniagent which is often empty
            // after a Vault dev-mode restart while credentials/* holds the real NIM key.
            metadata["apiCredentialId"] = cid.ToString("D");
        }

        var turnStopwatch = Stopwatch.StartNew();
        Exception? lastError = null;

        for (var chainIndex = 0; chainIndex < modelChain.Count; chainIndex++)
        {
            var model = modelChain[chainIndex];
            try
            {
                var response = await ExecuteWithRetryAsync(
                    new ModelRequest(
                        member.Provider,
                        model,
                        messages,
                        member.Temperature,
                        maxTokens,
                        timeoutSeconds,
                        metadata),
                    retryCount,
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(response.Content))
                {
                    throw new ProviderException(
                        ProviderErrorCode.UnknownError,
                        $"Model {model} returned an empty response.");
                }

                turnStopwatch.Stop();
                var usage = response.TotalTokens.HasValue
                    ? tokenUsageExtractor.Extract(response)
                    : tokenUsageExtractor.Estimate(
                        new ModelRequest(member.Provider, model, messages, member.Temperature, maxTokens, timeoutSeconds),
                        response);

                turn.Status = PanelTurnStatus.Completed;
                turn.Output = InputSanitizer.Redact(response.Content);
                turn.ModelUsed = model;
                turn.InputTokens = usage.InputTokens;
                turn.OutputTokens = usage.OutputTokens;
                turn.TotalTokens = usage.TotalTokens;
                turn.LatencyMs = turnStopwatch.ElapsedMilliseconds;
                turn.CompletedAt = DateTimeOffset.UtcNow;
                session.CurrentMemberId = null;
                session.FloorDeadline = null;
                await dbContext.SaveChangesAsync(cancellationToken);

                // Keep turn speech on the stream; full text also lives on PanelTurn.Output.
                await panelEvents.WriteAsync(
                    session.Id,
                    turn.Id,
                    ConsoleEventType.PanelTurnCompleted,
                    turn.Output ?? string.Empty,
                    JsonSerializer.Serialize(new
                    {
                        memberId = member.Id,
                        member.DisplayName,
                        role = member.Role.ToString(),
                        stance = member.Stance.ToString(),
                        member.StanceLabel,
                        model,
                        usage.InputTokens,
                        usage.OutputTokens,
                        turn.LatencyMs
                    }),
                    cancellationToken);

                await panelEvents.WriteAsync(
                    session.Id,
                    turn.Id,
                    ConsoleEventType.AgentCompleted,
                    $"{member.DisplayName} finished ({turn.LatencyMs} ms, {usage.TotalTokens} tokens).",
                    null,
                    cancellationToken);

                // Attach for priorTurns lookup via session.Turns navigation.
                session.Turns.Add(turn);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ProviderException exception) when (
                chainIndex < modelChain.Count - 1 && ModelChainExecutor.ShouldFallbackToNextModel(exception.ErrorCode))
            {
                lastError = exception;
                await panelEvents.WriteAsync(
                    session.Id,
                    turn.Id,
                    ConsoleEventType.Warning,
                    $"Model {model} failed ({exception.ErrorCode}); falling back to {modelChain[chainIndex + 1]}.",
                    null,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                lastError = exception;
                break;
            }
        }

        turnStopwatch.Stop();
        return await FailTurnAsync(
            session,
            turn,
            lastError?.Message ?? "Guest turn failed.",
            cancellationToken);
    }

    private async Task<ModelResponse> ExecuteWithRetryAsync(
        ModelRequest request,
        int retryCount,
        CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(0, retryCount);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await modelProvider.CreateChatCompletionAsync(request, cancellationToken);
            }
            catch (ProviderException exception) when (
                attempt < maxRetries
                && exception.ErrorCode is ProviderErrorCode.RateLimit
                    or ProviderErrorCode.Timeout
                    or ProviderErrorCode.ProviderUnavailable
                    or ProviderErrorCode.UnknownError)
            {
                var delay = TimeSpan.FromMilliseconds(Math.Min(4000, 500 * Math.Pow(2, attempt)));
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private async Task<bool> FailTurnAsync(
        PanelSession session,
        PanelTurn turn,
        string error,
        CancellationToken cancellationToken)
    {
        turn.Status = PanelTurnStatus.Failed;
        turn.ErrorMessage = InputSanitizer.Redact(error);
        turn.CompletedAt = DateTimeOffset.UtcNow;
        turn.LatencyMs = turn.StartedAt.HasValue
            ? (long)(DateTimeOffset.UtcNow - turn.StartedAt.Value).TotalMilliseconds
            : 0;
        session.CurrentMemberId = null;
        session.FloorDeadline = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        await panelEvents.WriteAsync(
            session.Id,
            turn.Id,
            ConsoleEventType.AgentFailed,
            $"{turn.MemberDisplayName} failed: {error}",
            null,
            cancellationToken);

        session.Turns.Add(turn);
        return false;
    }

    private async Task RecalcTotalsAsync(PanelSession session, CancellationToken cancellationToken)
    {
        var turns = await dbContext.PanelTurns
            .AsNoTracking()
            .Where(t => t.SessionId == session.Id)
            .ToListAsync(cancellationToken);

        session.TotalInputTokens = turns.Sum(t => t.InputTokens);
        session.TotalOutputTokens = turns.Sum(t => t.OutputTokens);
        session.TotalTokens = turns.Sum(t => t.TotalTokens);
    }

    /// <summary>
    /// Member-bound credential first; otherwise the default OmniAgent/NVIDIA row so panel
    /// turns share Studio's Vault secret path instead of the often-empty providers/omniagent.
    /// </summary>
    private async Task<Guid?> ResolveCredentialIdAsync(
        AgentGroupMember member,
        CancellationToken cancellationToken)
    {
        if (member.ApiCredentialId is { } bound && bound != Guid.Empty)
        {
            return bound;
        }

        var defaults = await dbContext.ApiCredentials
            .AsNoTracking()
            .Where(c => c.IsDefault
                || c.Provider == "OmniAgent"
                || c.Provider == "NVIDIA"
                || c.Provider == "Nvidia")
            .OrderByDescending(c => c.IsDefault)
            .ThenBy(c => c.Name)
            .Select(c => c.Id)
            .Take(1)
            .ToListAsync(cancellationToken);

        return defaults.Count > 0 ? defaults[0] : null;
    }
}
