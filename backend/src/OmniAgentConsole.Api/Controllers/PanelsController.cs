using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OmniAgentConsole.Api.Middleware;
using OmniAgentConsole.Application.Configuration;
using OmniAgentConsole.Application.Panels;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Application.Secrets;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Api.Controllers;

[ApiController]
[Route("api/panels")]
public sealed class PanelsController : ControllerBase
{
    private readonly AgentConsoleDbContext dbContext;
    private readonly ITaskRunQueue queue;
    private readonly ITaskCancellationRegistry cancellationRegistry;
    private readonly ITaskCancellationBroadcast cancellationBroadcast;
    private readonly IProviderSecretResolver providerSecrets;
    private readonly IApiCredentialKeyResolver credentialKeys;
    private readonly SharedLabOptions sharedLab;

    public PanelsController(
        AgentConsoleDbContext dbContext,
        ITaskRunQueue queue,
        ITaskCancellationRegistry cancellationRegistry,
        ITaskCancellationBroadcast cancellationBroadcast,
        IProviderSecretResolver providerSecrets,
        IApiCredentialKeyResolver credentialKeys,
        IOptions<SharedLabOptions> sharedLab)
    {
        this.dbContext = dbContext;
        this.queue = queue;
        this.cancellationRegistry = cancellationRegistry;
        this.cancellationBroadcast = cancellationBroadcast;
        this.providerSecrets = providerSecrets;
        this.credentialKeys = credentialKeys;
        this.sharedLab = sharedLab.Value;
    }

    private bool SessionScoped => sharedLab.Enabled && !SharedLabHttp.IsAdmin(HttpContext);
    private string? CallerSessionId => SharedLabHttp.GetSessionId(HttpContext);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PanelSessionSummaryDto>>> List(CancellationToken cancellationToken)
    {
        var query = dbContext.PanelSessions.AsNoTracking().AsQueryable();
        if (SessionScoped)
        {
            var sid = CallerSessionId;
            query = query.Where(x => x.OwnerSessionId == sid);
        }

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(100)
            .Select(x => new PanelSessionSummaryDto(
                x.Id,
                x.GroupId,
                x.Group != null ? x.Group.Name : string.Empty,
                x.Title,
                x.Topic,
                x.Status.ToString(),
                x.MaxRounds,
                x.CreatedAt,
                x.CompletedAt,
                x.TotalTokens,
                x.TotalLatencyMs))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{panelId:guid}")]
    public async Task<ActionResult<PanelSessionDetailDto>> Get(Guid panelId, CancellationToken cancellationToken)
    {
        var session = await LoadOwnedSessionAsync(panelId, tracking: false, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        return Ok(ToDetail(session));
    }

    [HttpGet("{panelId:guid}/events")]
    public async Task<ActionResult<IReadOnlyList<PanelConsoleEventDto>>> Events(
        Guid panelId,
        CancellationToken cancellationToken)
    {
        var exists = await OwnedExistsAsync(panelId, cancellationToken);
        if (!exists)
        {
            return NotFound();
        }

        var events = await dbContext.PanelConsoleEvents
            .AsNoTracking()
            .Where(x => x.PanelSessionId == panelId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new PanelConsoleEventDto(
                x.Id,
                x.PanelSessionId,
                x.PanelTurnId,
                x.EventType.ToString(),
                x.Message,
                x.PayloadJson,
                x.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(events);
    }

    [HttpGet("{panelId:guid}/transcript")]
    public async Task<IActionResult> Transcript(Guid panelId, CancellationToken cancellationToken)
    {
        var session = await LoadOwnedSessionAsync(panelId, tracking: false, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        var md = BuildTranscriptMarkdown(session);
        var fileName = $"panel-{panelId:N}.md";
        return File(System.Text.Encoding.UTF8.GetBytes(md), "text/markdown; charset=utf-8", fileName);
    }

    private static string BuildTranscriptMarkdown(PanelSession session)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Panel: {session.Title}");
        sb.AppendLine();
        sb.AppendLine($"- **Id:** `{session.Id}`");
        sb.AppendLine($"- **Group:** {session.Group?.Name ?? session.GroupId.ToString()}");
        sb.AppendLine($"- **Status:** {session.Status}");
        sb.AppendLine($"- **Rounds configured:** {session.MaxRounds}");
        sb.AppendLine($"- **Created:** {session.CreatedAt:O}");
        if (session.CompletedAt is not null)
        {
            sb.AppendLine($"- **Completed:** {session.CompletedAt:O}");
        }

        sb.AppendLine();
        sb.AppendLine("## Topic");
        sb.AppendLine();
        sb.AppendLine(session.Topic);
        sb.AppendLine();
        sb.AppendLine("## Turns");
        sb.AppendLine();
        foreach (var turn in (session.Turns ?? Array.Empty<PanelTurn>()).OrderBy(t => t.TurnOrder))
        {
            sb.AppendLine($"### #{turn.TurnOrder} {turn.MemberDisplayName} ({turn.Status})");
            if (!string.IsNullOrWhiteSpace(turn.ModelUsed))
            {
                sb.AppendLine();
                sb.AppendLine($"*Model: `{turn.ModelUsed}` · {turn.LatencyMs} ms · {turn.TotalTokens} tokens*");
            }

            sb.AppendLine();
            sb.AppendLine(string.IsNullOrWhiteSpace(turn.Output) ? "_(no output)_" : turn.Output.Trim());
            if (!string.IsNullOrWhiteSpace(turn.ErrorMessage))
            {
                sb.AppendLine();
                sb.AppendLine($"> Error: {turn.ErrorMessage}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    [HttpPost]
    public async Task<ActionResult<PanelSessionDetailDto>> Create(
        [FromBody] CreatePanelSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Topic))
        {
            return BadRequest("Topic is required.");
        }

        var group = await dbContext.AgentGroups
            .AsNoTracking()
            .Include(x => x.Members)
            .FirstOrDefaultAsync(x => x.Id == request.GroupId, cancellationToken);

        if (group is null)
        {
            return BadRequest("Group not found.");
        }

        var enabledCount = group.Members.Count(m => m.Enabled);
        if (!PanelDiscussionPolicy.CanStart(enabledCount))
        {
            return BadRequest("Group has no enabled guests. Add at least one persona.");
        }

        var topic = InputSanitizer.Redact(request.Topic.Trim());
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? TruncateTitle(topic)
            : request.Title.Trim();
        var maxRounds = Math.Clamp(request.MaxRounds <= 0 ? 1 : request.MaxRounds, 1, 3);

        var session = new PanelSession
        {
            GroupId = group.Id,
            Topic = topic,
            Title = title,
            MaxRounds = maxRounds,
            Status = PanelSessionStatus.Pending,
            OwnerSessionId = SessionScoped ? CallerSessionId : null
        };

        dbContext.PanelSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Reload with group name for DTO.
        session = (await dbContext.PanelSessions
            .AsNoTracking()
            .Include(x => x.Group)
            .Include(x => x.Turns)
            .Include(x => x.ConsoleEvents)
            .FirstAsync(x => x.Id == session.Id, cancellationToken))!;

        return CreatedAtAction(nameof(Get), new { panelId = session.Id }, ToDetail(session));
    }

    [HttpPost("{panelId:guid}/start")]
    public async Task<IActionResult> Start(Guid panelId, CancellationToken cancellationToken)
    {
        var session = await LoadOwnedSessionAsync(panelId, tracking: true, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        if (!await HasAnyProviderKeyAsync(cancellationToken))
        {
            return BadRequest(
                "API key is not configured. Open Settings, paste your NVIDIA / OmniAgent API key, click Save, then retry the panel. " +
                "(Vault dev mode loses secrets when the stack restarts.)");
        }

        if (session.Status is PanelSessionStatus.Running)
        {
            return Conflict("Panel is already running.");
        }

        if (session.Status is PanelSessionStatus.Completed or PanelSessionStatus.Cancelled or PanelSessionStatus.Failed)
        {
            // Fresh Start re-run: wipe prior turns/events.
            var oldTurns = await dbContext.PanelTurns.Where(t => t.SessionId == panelId).ToListAsync(cancellationToken);
            var oldEvents = await dbContext.PanelConsoleEvents.Where(e => e.PanelSessionId == panelId).ToListAsync(cancellationToken);
            dbContext.PanelTurns.RemoveRange(oldTurns);
            dbContext.PanelConsoleEvents.RemoveRange(oldEvents);
            session.StartedAt = null;
            session.TotalLatencyMs = 0;
            session.TotalInputTokens = 0;
            session.TotalOutputTokens = 0;
            session.TotalTokens = 0;
            session.VotesJson = null;
        }

        session.Status = PanelSessionStatus.Pending;
        session.CompletedAt = null;
        session.ErrorMessage = null;
        session.CurrentMemberId = null;
        session.FloorDeadline = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        await queue.EnqueuePanelAsync(panelId, cancellationToken);
        return Accepted(new { id = panelId, status = "Queued" });
    }

    /// <summary>
    /// After a finished panel, inject a user follow-up and run one more roster pass
    /// (keeps prior turns as transcript context).
    /// </summary>
    [HttpPost("{panelId:guid}/continue")]
    public async Task<IActionResult> Continue(
        Guid panelId,
        [FromBody] ContinuePanelRequest request,
        CancellationToken cancellationToken)
    {
        var session = await LoadOwnedSessionAsync(panelId, tracking: true, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        if (session.Status is PanelSessionStatus.Running or PanelSessionStatus.Pending)
        {
            return Conflict("Panel is still running; wait for it to finish or cancel first.");
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest("Message is required.");
        }

        if (!await HasAnyProviderKeyAsync(cancellationToken))
        {
            return BadRequest(
                "API key is not configured. Open Settings, paste your NVIDIA / OmniAgent API key, then retry.");
        }

        var message = InputSanitizer.Redact(request.Message.Trim());
        session.MaxRounds = Math.Clamp(request.ExtraRounds <= 0 ? 1 : request.ExtraRounds, 1, 3);
        session.Status = PanelSessionStatus.Pending;
        session.CompletedAt = null;
        session.ErrorMessage = null;
        session.CurrentMemberId = null;
        session.FloorDeadline = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        // Persist user interjection on the stream before the worker runs.
        dbContext.PanelConsoleEvents.Add(new PanelConsoleEvent
        {
            PanelSessionId = panelId,
            EventType = ConsoleEventType.UserMessage,
            Message = message,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { kind = "followUp" })
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        await queue.EnqueuePanelAsync(panelId, cancellationToken);
        return Accepted(new { id = panelId, status = "Queued", followUp = true });
    }

    [HttpDelete("{panelId:guid}")]
    public async Task<IActionResult> Delete(Guid panelId, CancellationToken cancellationToken)
    {
        var session = await LoadOwnedSessionAsync(panelId, tracking: true, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        if (session.Status is PanelSessionStatus.Running or PanelSessionStatus.Pending)
        {
            return Conflict("Cancel the panel before deleting it.");
        }

        // Cascades remove turns + console events (configured on PanelSession).
        dbContext.PanelSessions.Remove(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Deletes finished sessions (Completed / Failed / Cancelled). Skips live ones.</summary>
    [HttpPost("bulk-delete")]
    public async Task<ActionResult<object>> BulkDeleteFinished(CancellationToken cancellationToken)
    {
        var query = dbContext.PanelSessions.AsQueryable()
            .Where(x => x.Status == PanelSessionStatus.Completed
                || x.Status == PanelSessionStatus.Failed
                || x.Status == PanelSessionStatus.Cancelled);

        if (SessionScoped)
        {
            var sid = CallerSessionId;
            query = query.Where(x => x.OwnerSessionId == sid);
        }

        var doomed = await query.ToListAsync(cancellationToken);
        if (doomed.Count == 0)
        {
            return Ok(new { deleted = 0 });
        }

        dbContext.PanelSessions.RemoveRange(doomed);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { deleted = doomed.Count });
    }

    [HttpPost("{panelId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid panelId, CancellationToken cancellationToken)
    {
        var session = await LoadOwnedSessionAsync(panelId, tracking: true, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        if (session.Status is PanelSessionStatus.Completed or PanelSessionStatus.Cancelled or PanelSessionStatus.Failed)
        {
            return Ok(new { id = panelId, status = session.Status.ToString() });
        }

        // Write Cancelled before token so the worker classifies as user cancel (ACK).
        session.Status = PanelSessionStatus.Cancelled;
        session.CompletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        cancellationRegistry.Cancel(panelId);
        await cancellationBroadcast.PublishCancelAsync(panelId, cancellationToken);

        return Ok(new { id = panelId, status = "Cancelled" });
    }

    /// <summary>
    /// Audience vote: who convinced you? Allowed once the panel is finished
    /// (Completed / Failed / Cancelled). Votes accumulate in VotesJson.
    /// </summary>
    [HttpPost("{panelId:guid}/vote")]
    public async Task<ActionResult<IReadOnlyList<PanelVoteTallyDto>>> Vote(
        Guid panelId,
        [FromBody] CastPanelVoteRequest request,
        CancellationToken cancellationToken)
    {
        var session = await LoadOwnedSessionAsync(panelId, tracking: true, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        if (session.Status is PanelSessionStatus.Pending or PanelSessionStatus.Running)
        {
            return Conflict("Wait for the panel to finish before voting.");
        }

        if (request.MemberId == Guid.Empty)
        {
            return BadRequest("MemberId is required.");
        }

        // Must be a speaker who actually took a turn (or is on the roster via turns).
        var turnNames = (session.Turns ?? Array.Empty<PanelTurn>())
            .GroupBy(t => t.MemberId)
            .ToDictionary(g => g.Key, g => g.First().MemberDisplayName);

        if (!turnNames.ContainsKey(request.MemberId))
        {
            // Fall back: group member still present.
            var member = await dbContext.AgentGroupMembers
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    m => m.Id == request.MemberId && m.GroupId == session.GroupId,
                    cancellationToken);
            if (member is null)
            {
                return BadRequest("Speaker is not part of this panel.");
            }

            turnNames[member.Id] = member.DisplayName;
        }

        var map = PanelVoteStore.Cast(session.VotesJson, request.MemberId);
        session.VotesJson = PanelVoteStore.Serialize(map);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(PanelVoteStore.ToTallies(map, turnNames));
    }

    private async Task<bool> OwnedExistsAsync(Guid panelId, CancellationToken cancellationToken)
    {
        var query = dbContext.PanelSessions.AsNoTracking().Where(x => x.Id == panelId);
        if (SessionScoped)
        {
            var sid = CallerSessionId;
            query = query.Where(x => x.OwnerSessionId == sid);
        }

        return await query.AnyAsync(cancellationToken);
    }

    private async Task<PanelSession?> LoadOwnedSessionAsync(
        Guid panelId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        IQueryable<PanelSession> query = tracking
            ? dbContext.PanelSessions
            : dbContext.PanelSessions.AsNoTracking();

        query = query
            .Include(x => x.Group)
            .Include(x => x.Turns)
            .Include(x => x.ConsoleEvents)
            .Where(x => x.Id == panelId);

        if (SessionScoped)
        {
            var sid = CallerSessionId;
            query = query.Where(x => x.OwnerSessionId == sid);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private static PanelSessionDetailDto ToDetail(PanelSession session)
    {
        var turns = (session.Turns ?? Array.Empty<PanelTurn>())
            .OrderBy(t => t.TurnOrder)
            .Select(t => new PanelTurnDto(
                t.Id,
                t.MemberId,
                t.MemberDisplayName,
                t.TurnOrder,
                t.Output,
                t.Status.ToString(),
                t.ModelUsed,
                t.InputTokens,
                t.OutputTokens,
                t.TotalTokens,
                t.LatencyMs,
                t.ErrorMessage,
                t.StartedAt,
                t.CompletedAt))
            .ToList();

        var events = (session.ConsoleEvents ?? Array.Empty<PanelConsoleEvent>())
            .OrderBy(e => e.CreatedAt)
            .Select(e => new PanelConsoleEventDto(
                e.Id,
                e.PanelSessionId,
                e.PanelTurnId,
                e.EventType.ToString(),
                e.Message,
                e.PayloadJson,
                e.CreatedAt))
            .ToList();

        var displayNames = turns
            .GroupBy(t => t.MemberId)
            .ToDictionary(g => g.Key, g => g.First().MemberDisplayName);
        var votes = PanelVoteStore.ToTallies(PanelVoteStore.Parse(session.VotesJson), displayNames);

        return new PanelSessionDetailDto(
            session.Id,
            session.GroupId,
            session.Group?.Name ?? string.Empty,
            session.Title,
            session.Topic,
            session.Status.ToString(),
            session.MaxRounds,
            session.CurrentMemberId,
            session.FloorDeadline,
            session.CreatedAt,
            session.StartedAt,
            session.CompletedAt,
            session.TotalInputTokens,
            session.TotalOutputTokens,
            session.TotalTokens,
            session.TotalLatencyMs,
            session.ErrorMessage,
            turns,
            events,
            votes);
    }

    private static string TruncateTitle(string topic)
    {
        const int max = 80;
        var oneLine = topic.Replace('\n', ' ').Trim();
        return oneLine.Length <= max ? oneLine : oneLine[..(max - 1)] + "…";
    }

    private async Task<bool> HasAnyProviderKeyAsync(CancellationToken cancellationToken)
    {
        if (await providerSecrets.HasOmniAgentApiKeyAsync(cancellationToken))
        {
            return true;
        }

        var defaultId = await dbContext.ApiCredentials
            .AsNoTracking()
            .Where(c => c.IsDefault
                || c.Provider == "OmniAgent"
                || c.Provider == "NVIDIA"
                || c.Provider == "Nvidia")
            .OrderByDescending(c => c.IsDefault)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (defaultId is null)
        {
            return false;
        }

        var key = await credentialKeys.ResolveByIdAsync(defaultId.Value, cancellationToken);
        return !string.IsNullOrWhiteSpace(key);
    }
}
