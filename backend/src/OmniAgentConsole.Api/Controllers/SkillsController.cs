using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Api.Controllers;

[ApiController]
[Route("api/skills")]
public sealed class SkillsController : ControllerBase
{
    private readonly AgentConsoleDbContext dbContext;

    public SkillsController(AgentConsoleDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SkillDto>>> List(CancellationToken cancellationToken)
    {
        var skills = await dbContext.SkillDefinitions
            .AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return Ok(skills.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<SkillDto>> Create(
        [FromBody] SaveSkillRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var skill = new SkillDefinition
        {
            Name = request.Name.Trim(),
            Category = string.IsNullOrWhiteSpace(request.Category) ? "General" : request.Category.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            Instructions = request.Instructions.Trim(),
            Keywords = request.Keywords?.Trim() ?? string.Empty,
            Enabled = request.Enabled,
            SortOrder = request.SortOrder,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.SkillDefinitions.Add(skill);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(skill));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SkillDto>> Update(
        Guid id,
        [FromBody] SaveSkillRequest request,
        CancellationToken cancellationToken)
    {
        var skill = await dbContext.SkillDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (skill is null)
        {
            return NotFound();
        }

        var validationError = Validate(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        skill.Name = request.Name.Trim();
        skill.Category = string.IsNullOrWhiteSpace(request.Category) ? "General" : request.Category.Trim();
        skill.Description = request.Description?.Trim() ?? string.Empty;
        skill.Instructions = request.Instructions.Trim();
        skill.Keywords = request.Keywords?.Trim() ?? string.Empty;
        skill.Enabled = request.Enabled;
        skill.SortOrder = request.SortOrder;
        skill.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(skill));
    }

    /// <summary>
    /// Suggests skills for a prompt via keyword matching; also returns follow-up
    /// questions when the prompt leaves the stack or datastore ambiguous.
    /// </summary>
    [HttpPost("suggest")]
    public async Task<ActionResult<SuggestSkillsResponse>> Suggest(
        [FromBody] SuggestSkillsRequest request,
        CancellationToken cancellationToken)
    {
        var enabledSkills = await dbContext.SkillDefinitions
            .AsNoTracking()
            .Where(x => x.Enabled)
            .ToListAsync(cancellationToken);

        var suggestion = SkillSuggestionEngine.Suggest(request.Prompt, enabledSkills);

        return Ok(new SuggestSkillsResponse(suggestion.SkillIds, suggestion.Questions));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var skill = await dbContext.SkillDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (skill is null)
        {
            return NotFound();
        }

        dbContext.SkillDefinitions.Remove(skill);
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private static string? Validate(SaveSkillRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Skill name is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Instructions))
        {
            return "Skill instructions are required.";
        }

        if (request.Instructions.Length > 8000)
        {
            return "Skill instructions must be at most 8000 characters.";
        }

        return null;
    }

    private static SkillDto ToDto(SkillDefinition skill) =>
        new(skill.Id, skill.Name, skill.Category, skill.Description, skill.Instructions,
            skill.Keywords, skill.Enabled, skill.SortOrder, skill.CreatedAt, skill.UpdatedAt);
}

public sealed record SkillDto(
    Guid Id,
    string Name,
    string Category,
    string Description,
    string Instructions,
    string Keywords,
    bool Enabled,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record SaveSkillRequest(
    string Name,
    string? Category,
    string? Description,
    string Instructions,
    string? Keywords = null,
    bool Enabled = true,
    int SortOrder = 0);

public sealed record SuggestSkillsRequest(string Prompt);

public sealed record SuggestSkillsResponse(
    IReadOnlyList<Guid> SkillIds,
    IReadOnlyList<string> Questions);
