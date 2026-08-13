using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniAgentConsole.Application.Panels;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Api.Controllers;

/// <summary>
/// One-click sample cast for demos / first-run (idempotent by group name).
/// </summary>
[ApiController]
[Route("api/demo")]
public sealed class DemoController : ControllerBase
{
    public const string SampleDebateGroupName = "Demo: 3-for / 1-against";

    private readonly AgentConsoleDbContext dbContext;

    public DemoController(AgentConsoleDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public sealed record DemoSeedResult(
        Guid GroupId,
        string GroupName,
        bool Created,
        string SuggestedTopic,
        string StudioPrompt,
        string StudioPipeline,
        string WorkspacePath);

    [HttpPost("seed-debate")]
    public async Task<ActionResult<DemoSeedResult>> SeedDebate(CancellationToken cancellationToken)
    {
        var existing = await dbContext.AgentGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Name == SampleDebateGroupName, cancellationToken);

        if (existing is not null && existing.Members.Count > 0)
        {
            return Ok(new DemoSeedResult(
                existing.Id,
                existing.Name,
                Created: false,
                SuggestedTopic: "Should remote-first be the default for product engineering teams?",
                StudioPrompt: SampleStudioPrompt,
                StudioPipeline: "coder",
                WorkspacePath: "/workspace/demo-notes-api"));
        }

        var group = existing ?? new AgentGroup
        {
            Name = SampleDebateGroupName,
            Description = "Seeded cast for demos (3 For + 1 Against + Moderator)."
        };
        if (existing is null)
        {
            dbContext.AgentGroups.Add(group);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // Clear empty shell members if re-seeded.
        if (existing is not null)
        {
            dbContext.AgentGroupMembers.RemoveRange(existing.Members);
        }

        var members = new[]
        {
            Member(group.Id, "Moderator", PanelMemberRole.Moderator, PanelStance.Neutral, null, 0,
                "You moderate a live panel. Introduce roster and topic; keep speakers on-mission; close with a short synthesis. Do not invent guests."),
            Member(group.Id, "Advocate A", PanelMemberRole.Commentator, PanelStance.For, "remote-first", 1,
                "Argue FOR remote-first work. Concrete reasons and examples; stay on the actual topic."),
            Member(group.Id, "Advocate B", PanelMemberRole.Commentator, PanelStance.For, "remote-first", 2,
                "Second FOR voice — different angle (tools, hiring, retention). Do not invent guests."),
            Member(group.Id, "Advocate C", PanelMemberRole.Commentator, PanelStance.For, "remote-first", 3,
                "Third FOR voice — ethics or long-term culture. Stay specific."),
            Member(group.Id, "Critic", PanelMemberRole.Commentator, PanelStance.Against, "office-first", 4,
                "Argue AGAINST default remote-first. Risks, mentorship, collaboration. Stay civil.")
        };
        dbContext.AgentGroupMembers.AddRange(members);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new DemoSeedResult(
            group.Id,
            group.Name,
            Created: true,
            SuggestedTopic: "Should remote-first be the default for product engineering teams?",
            StudioPrompt: SampleStudioPrompt,
            StudioPipeline: "coder",
            WorkspacePath: "/workspace/demo-notes-api"));
    }

    [HttpGet("studio-preset")]
    public ActionResult<object> StudioPreset([FromQuery] string? id = "fastapi-notes")
    {
        var preset = id switch
        {
            "angular-dashboard" => new
            {
                id = "angular-dashboard",
                name = "Angular dashboard shell",
                pipeline = "plan-code-review",
                workspacePath = "/workspace/demo-angular-dash",
                prompt = "Create a minimal Angular standalone dashboard shell with a home page, a simple stats card list, and a README. Use TypeScript and clear folder structure.",
                skillKeywords = new[] { "Angular", "README" }
            },
            "dotnet-api" => new
            {
                id = "dotnet-api",
                name = ".NET minimal API",
                pipeline = "full",
                workspacePath = "/workspace/demo-dotnet-api",
                prompt = "Produce a complete ASP.NET Core minimal API with /health, one CRUD resource (notes), appsettings, Dockerfile, docker-compose, and README.",
                skillKeywords = new[] { ".NET", "Docker", "README", "Health" }
            },
            _ => new
            {
                id = "fastapi-notes",
                name = "FastAPI notes API",
                pipeline = "coder",
                workspacePath = "/workspace/demo-notes-api",
                prompt = SampleStudioPrompt,
                skillKeywords = new[] { "FastAPI", "Tests", "Docker", "README", "Health" }
            }
        };
        return Ok(preset);
    }

    private const string SampleStudioPrompt =
        "Build a small FastAPI notes API with POST/GET notes, GET /health, tests/, Dockerfile, docker-compose.yml, and README.md. Keep it runnable.";

    private static AgentGroupMember Member(
        Guid groupId,
        string name,
        PanelMemberRole role,
        PanelStance stance,
        string? stanceLabel,
        int sort,
        string prompt) => new()
    {
        GroupId = groupId,
        DisplayName = name,
        Role = role,
        Stance = stance,
        StanceLabel = stanceLabel,
        SystemPrompt = prompt,
        DefaultModel = sort == 0 ? "meta/llama-3.1-8b-instruct" : "meta/llama-3.1-8b-instruct",
        Provider = ProviderType.OmniAgent,
        MaxTokens = 800,
        Temperature = 0.7m,
        TimeoutSeconds = 60,
        RetryCount = 1,
        SortOrder = sort,
        Enabled = true
    };
}
