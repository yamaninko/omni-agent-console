using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniAgentConsole.Application.Panels;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Api.Controllers;

[ApiController]
[Route("api/agent-groups")]
public sealed class AgentGroupsController : ControllerBase
{
    private readonly AgentConsoleDbContext dbContext;

    public AgentGroupsController(AgentConsoleDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AgentGroupSummaryDto>>> List(CancellationToken cancellationToken)
    {
        var groups = await dbContext.AgentGroups
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AgentGroupSummaryDto(
                x.Id,
                x.Name,
                x.Description,
                x.Members.Count,
                x.CreatedAt,
                x.UpdatedAt,
                x.IsTemplate))
            .ToListAsync(cancellationToken);

        return Ok(groups);
    }

    [HttpGet("{groupId:guid}")]
    public async Task<ActionResult<AgentGroupDetailDto>> Get(Guid groupId, CancellationToken cancellationToken)
    {
        var group = await dbContext.AgentGroups
            .AsNoTracking()
            .Include(x => x.Members)
            .FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken);

        if (group is null)
        {
            return NotFound();
        }

        return Ok(ToDetail(group));
    }

    [HttpPost]
    public async Task<ActionResult<AgentGroupDetailDto>> Create(
        [FromBody] CreateAgentGroupRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        var group = new AgentGroup
        {
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim()
        };

        dbContext.AgentGroups.Add(group);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { groupId = group.Id }, ToDetail(group));
    }

    [HttpPut("{groupId:guid}")]
    public async Task<ActionResult<AgentGroupDetailDto>> Update(
        Guid groupId,
        [FromBody] UpdateAgentGroupRequest request,
        CancellationToken cancellationToken)
    {
        var group = await dbContext.AgentGroups
            .Include(x => x.Members)
            .FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken);

        if (group is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        if (group.IsTemplate)
        {
            return Conflict("Template groups are read-only. Clone the template first, then edit the copy.");
        }

        group.Name = request.Name.Trim();
        group.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        group.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDetail(group));
    }

    /// <summary>Mark/unmark as instructor template cast (students clone, don't mutate).</summary>
    [HttpPost("{groupId:guid}/set-template")]
    public async Task<ActionResult<AgentGroupDetailDto>> SetTemplate(
        Guid groupId,
        [FromBody] SetGroupTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var group = await dbContext.AgentGroups
            .Include(x => x.Members)
            .FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken);
        if (group is null)
        {
            return NotFound();
        }

        group.IsTemplate = request.IsTemplate;
        group.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToDetail(group));
    }

    [HttpPost("{groupId:guid}/clone")]
    public async Task<ActionResult<AgentGroupDetailDto>> Clone(Guid groupId, CancellationToken cancellationToken)
    {
        var source = await dbContext.AgentGroups
            .AsNoTracking()
            .Include(x => x.Members)
            .FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken);

        if (source is null)
        {
            return NotFound();
        }

        var clone = new AgentGroup
        {
            Name = source.IsTemplate ? $"{source.Name} (student copy)" : $"{source.Name} (copy)",
            Description = source.Description,
            IsTemplate = false
        };
        dbContext.AgentGroups.Add(clone);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var m in source.Members.OrderBy(x => x.SortOrder).ThenBy(x => x.DisplayName))
        {
            dbContext.AgentGroupMembers.Add(new AgentGroupMember
            {
                GroupId = clone.Id,
                DisplayName = m.DisplayName,
                SystemPrompt = m.SystemPrompt,
                DefaultModel = m.DefaultModel,
                FallbackModels = m.FallbackModels,
                Provider = m.Provider,
                ApiCredentialId = m.ApiCredentialId,
                MaxTokens = m.MaxTokens,
                Temperature = m.Temperature,
                TimeoutSeconds = m.TimeoutSeconds,
                RetryCount = m.RetryCount,
                SortOrder = m.SortOrder,
                Enabled = m.Enabled,
                Role = m.Role,
                Stance = m.Stance,
                StanceLabel = m.StanceLabel
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var detail = await dbContext.AgentGroups
            .AsNoTracking()
            .Include(x => x.Members)
            .FirstAsync(x => x.Id == clone.Id, cancellationToken);

        return CreatedAtAction(nameof(Get), new { groupId = clone.Id }, ToDetail(detail));
    }

    [HttpDelete("{groupId:guid}")]
    public async Task<IActionResult> Delete(Guid groupId, CancellationToken cancellationToken)
    {
        var group = await dbContext.AgentGroups.FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken);
        if (group is null)
        {
            return NotFound();
        }

        if (group.IsTemplate)
        {
            return Conflict("Unmark as template before deleting, or keep it as the instructor library cast.");
        }

        var hasSessions = await dbContext.PanelSessions.AnyAsync(
            x => x.GroupId == groupId,
            cancellationToken);
        if (hasSessions)
        {
            return Conflict("Cannot delete a group that has panel history. Create a new group instead, or clear panel sessions first.");
        }

        dbContext.AgentGroups.Remove(group);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{groupId:guid}/members")]
    public async Task<ActionResult<AgentGroupMemberDto>> AddMember(
        Guid groupId,
        [FromBody] UpsertAgentGroupMemberRequest request,
        CancellationToken cancellationToken)
    {
        var group = await dbContext.AgentGroups
            .Include(x => x.Members)
            .FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken);

        if (group is null)
        {
            return NotFound();
        }

        if (group.IsTemplate)
        {
            return Conflict("Template groups are read-only. Clone first.");
        }

        if (group.Members.Count >= PanelDiscussionPolicy.MaxMembersPerGroup)
        {
            return BadRequest($"A group may have at most {PanelDiscussionPolicy.MaxMembersPerGroup} members.");
        }

        var validationError = ValidateMember(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var member = new AgentGroupMember { GroupId = groupId };
        ApplyMember(member, request, isCreate: true);
        if (group.Members.Count > 0 && request.SortOrder == 0)
        {
            member.SortOrder = group.Members.Max(m => m.SortOrder) + 1;
        }

        dbContext.AgentGroupMembers.Add(member);
        group.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToMemberDto(member));
    }

    [HttpPut("{groupId:guid}/members/{memberId:guid}")]
    public async Task<ActionResult<AgentGroupMemberDto>> UpdateMember(
        Guid groupId,
        Guid memberId,
        [FromBody] UpsertAgentGroupMemberRequest request,
        CancellationToken cancellationToken)
    {
        var member = await dbContext.AgentGroupMembers
            .FirstOrDefaultAsync(x => x.Id == memberId && x.GroupId == groupId, cancellationToken);

        if (member is null)
        {
            return NotFound();
        }

        var groupGate = await dbContext.AgentGroups.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken);
        if (groupGate?.IsTemplate == true)
        {
            return Conflict("Template groups are read-only. Clone first.");
        }

        var validationError = ValidateMember(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        ApplyMember(member, request, isCreate: false);
        var group = await dbContext.AgentGroups.FirstAsync(x => x.Id == groupId, cancellationToken);
        group.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToMemberDto(member));
    }

    [HttpDelete("{groupId:guid}/members/{memberId:guid}")]
    public async Task<IActionResult> DeleteMember(
        Guid groupId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var member = await dbContext.AgentGroupMembers
            .FirstOrDefaultAsync(x => x.Id == memberId && x.GroupId == groupId, cancellationToken);

        if (member is null)
        {
            return NotFound();
        }

        var groupGate = await dbContext.AgentGroups.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken);
        if (groupGate?.IsTemplate == true)
        {
            return Conflict("Template groups are read-only. Clone first.");
        }

        dbContext.AgentGroupMembers.Remove(member);
        var group = await dbContext.AgentGroups.FirstAsync(x => x.Id == groupId, cancellationToken);
        group.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPut("{groupId:guid}/members/reorder")]
    public async Task<ActionResult<AgentGroupDetailDto>> Reorder(
        Guid groupId,
        [FromBody] ReorderMembersRequest request,
        CancellationToken cancellationToken)
    {
        var group = await dbContext.AgentGroups
            .Include(x => x.Members)
            .FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken);

        if (group is null)
        {
            return NotFound();
        }

        if (group.IsTemplate)
        {
            return Conflict("Template groups are read-only. Clone first.");
        }

        var ids = request.MemberIdsInOrder ?? Array.Empty<Guid>();
        if (ids.Count != group.Members.Count || ids.Distinct().Count() != ids.Count)
        {
            return BadRequest("Reorder list must include each member id exactly once.");
        }

        var byId = group.Members.ToDictionary(m => m.Id);
        for (var i = 0; i < ids.Count; i++)
        {
            if (!byId.TryGetValue(ids[i], out var member))
            {
                return BadRequest($"Unknown member id: {ids[i]}");
            }

            member.SortOrder = i;
        }

        group.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToDetail(group));
    }

    private static string? ValidateMember(UpsertAgentGroupMemberRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return "Display name is required.";
        }

        if (string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            return "System prompt is required.";
        }

        if (string.IsNullOrWhiteSpace(request.DefaultModel))
        {
            return "Default model is required.";
        }

        if (request.MaxTokens is < 64 or > 200_000)
        {
            return "MaxTokens must be between 64 and 200000.";
        }

        if (request.TimeoutSeconds is < 5 or > 600)
        {
            return "TimeoutSeconds must be between 5 and 600.";
        }

        if (request.RetryCount is < 0 or > 5)
        {
            return "RetryCount must be between 0 and 5.";
        }

        if (request.Temperature is < 0 or > 2)
        {
            return "Temperature must be between 0 and 2.";
        }

        return null;
    }

    private static void ApplyMember(AgentGroupMember member, UpsertAgentGroupMemberRequest request, bool isCreate)
    {
        member.DisplayName = request.DisplayName.Trim();
        member.SystemPrompt = request.SystemPrompt.Trim();
        member.DefaultModel = request.DefaultModel.Trim();
        member.FallbackModels = string.IsNullOrWhiteSpace(request.FallbackModels)
            ? null
            : request.FallbackModels.Trim();
        member.Provider = request.Provider;
        member.ApiCredentialId = request.ApiCredentialId;
        member.MaxTokens = request.MaxTokens > 0 ? request.MaxTokens : PanelDiscussionPolicy.DefaultMaxTokens;
        member.Temperature = request.Temperature;
        member.TimeoutSeconds = request.TimeoutSeconds > 0
            ? request.TimeoutSeconds
            : PanelDiscussionPolicy.DefaultTimeoutSeconds;
        member.RetryCount = request.RetryCount;
        member.Enabled = request.Enabled;
        member.Role = request.Role;
        member.Stance = request.Stance;
        member.StanceLabel = string.IsNullOrWhiteSpace(request.StanceLabel)
            ? null
            : request.StanceLabel.Trim();
        if (isCreate || request.SortOrder != 0)
        {
            member.SortOrder = request.SortOrder;
        }
    }

    private static AgentGroupDetailDto ToDetail(AgentGroup group)
    {
        var members = (group.Members ?? Array.Empty<AgentGroupMember>())
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.DisplayName)
            .Select(ToMemberDto)
            .ToList();

        return new AgentGroupDetailDto(
            group.Id,
            group.Name,
            group.Description,
            group.CreatedAt,
            group.UpdatedAt,
            members,
            group.IsTemplate);
    }

    private static AgentGroupMemberDto ToMemberDto(AgentGroupMember member) =>
        new(
            member.Id,
            member.GroupId,
            member.DisplayName,
            member.SystemPrompt,
            member.DefaultModel,
            member.FallbackModels,
            member.Provider.ToString(),
            member.ApiCredentialId,
            member.MaxTokens,
            member.Temperature,
            member.TimeoutSeconds,
            member.RetryCount,
            member.SortOrder,
            member.Enabled,
            member.Role.ToString(),
            member.Stance.ToString(),
            member.StanceLabel,
            member.CreatedAt);
}
