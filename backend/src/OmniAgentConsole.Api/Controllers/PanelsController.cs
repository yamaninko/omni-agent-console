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

        var session = new PanelSession
        {
            GroupId = group.Id,
            Topic = topic,
            Title = title,
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

        if (session.Status is PanelSessionStatus.Completed or PanelSessionStatus.Cancelled)
        {
            // Allow re-run: clear prior turns/events and re-queue.
            var oldTurns = await dbContext.PanelTurns.Where(t => t.SessionId == panelId).ToListAsync(cancellationToken);
            var oldEvents = await dbContext.PanelConsoleEvents.Where(e => e.PanelSessionId == panelId).ToListAsync(cancellationToken);
            dbContext.PanelTurns.RemoveRange(oldTurns);
            dbContext.PanelConsoleEvents.RemoveRange(oldEvents);
        }

        session.Status = PanelSessionStatus.Pending;
        session.StartedAt = null;
        session.CompletedAt = null;
        session.ErrorMessage = null;
        session.CurrentMemberId = null;
        session.FloorDeadline = null;
        session.TotalLatencyMs = 0;
        session.TotalInputTokens = 0;
        session.TotalOutputTokens = 0;
        session.TotalTokens = 0;
        await dbContext.SaveChangesAsync(cancellationToken);

        await queue.EnqueuePanelAsync(panelId, cancellationToken);
        return Accepted(new { id = panelId, status = "Queued" });
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

        return new PanelSessionDetailDto(
            session.Id,
            session.GroupId,
            session.Group?.Name ?? string.Empty,
            session.Title,
            session.Topic,
            session.Status.ToString(),
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
            events);
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
