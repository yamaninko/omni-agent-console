using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Api.Controllers;

[ApiController]
[Route("api/providers")]
public sealed class ModelProviderController : ControllerBase
{
    private readonly AgentConsoleDbContext dbContext;

    public ModelProviderController(AgentConsoleDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var providers = await dbContext.ModelProviderSettings
            .AsNoTracking()
            .OrderBy(x => x.Provider)
            .ToListAsync(cancellationToken);

        return Ok(providers);
    }
}
