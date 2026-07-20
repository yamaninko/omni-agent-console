using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Api.Controllers;

[ApiController]
[Route("api/credentials")]
public sealed class CredentialsController : ControllerBase
{
    private readonly AgentConsoleDbContext dbContext;

    public CredentialsController(AgentConsoleDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApiCredentialDto>>> List(CancellationToken cancellationToken)
    {
        var credentials = await dbContext.ApiCredentials
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return Ok(credentials.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ApiCredentialDto>> Create(
        [FromBody] CreateCredentialRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Provider))
        {
            return BadRequest("Provider is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return BadRequest("API Key is required.");
        }

        if (request.IsDefault)
        {
            var defaults = await dbContext.ApiCredentials.Where(c => c.IsDefault).ToListAsync(cancellationToken);
            foreach (var d in defaults)
            {
                d.IsDefault = false;
            }
        }

        var credential = new ApiCredential
        {
            Name = request.Name.Trim(),
            Provider = request.Provider.Trim(),
            BaseUrl = request.BaseUrl?.Trim(),
            ApiKey = request.ApiKey.Trim(),
            IsDefault = request.IsDefault,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ApiCredentials.Add(credential);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(credential));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiCredentialDto>> Update(
        Guid id,
        [FromBody] CreateCredentialRequest request,
        CancellationToken cancellationToken)
    {
        var credential = await dbContext.ApiCredentials
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (credential is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Provider))
        {
            return BadRequest("Provider is required.");
        }

        if (request.IsDefault)
        {
            var defaults = await dbContext.ApiCredentials.Where(c => c.IsDefault && c.Id != id).ToListAsync(cancellationToken);
            foreach (var d in defaults)
            {
                d.IsDefault = false;
            }
        }

        credential.Name = request.Name.Trim();
        credential.Provider = request.Provider.Trim();
        credential.BaseUrl = request.BaseUrl?.Trim();

        // Empty API key on update means "keep the stored key" — the raw key is never
        // returned to the client, so the edit form cannot round-trip it.
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            credential.ApiKey = request.ApiKey.Trim();
        }

        credential.IsDefault = request.IsDefault;
        credential.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(credential));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var credential = await dbContext.ApiCredentials
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (credential is null)
        {
            return NotFound();
        }

        if (credential.IsDefault)
        {
            var next = await dbContext.ApiCredentials
                .Where(x => x.Id != id)
                .FirstOrDefaultAsync(cancellationToken);
            if (next != null)
            {
                next.IsDefault = true;
            }
        }

        dbContext.ApiCredentials.Remove(credential);
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private static ApiCredentialDto ToDto(ApiCredential credential)
    {
        var configured = IsRealKey(credential.ApiKey);

        return new ApiCredentialDto(
            credential.Id,
            credential.Name,
            credential.Provider,
            credential.BaseUrl,
            credential.IsDefault,
            configured,
            configured ? MaskKey(credential.ApiKey) : null,
            credential.CreatedAt,
            credential.UpdatedAt);
    }

    // Seed rows use "YOUR_<PROVIDER>_API_KEY_HERE" placeholders; treat them as unconfigured.
    private static bool IsRealKey(string? apiKey) =>
        !string.IsNullOrWhiteSpace(apiKey)
        && !(apiKey.StartsWith("YOUR_", StringComparison.Ordinal) && apiKey.EndsWith("_HERE", StringComparison.Ordinal));

    private static string MaskKey(string apiKey) =>
        apiKey.Length <= 8 ? "****" : $"{apiKey[..4]}...{apiKey[^4..]}";
}

public sealed record ApiCredentialDto(
    Guid Id,
    string Name,
    string Provider,
    string? BaseUrl,
    bool IsDefault,
    bool ApiKeyConfigured,
    string? MaskedApiKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CreateCredentialRequest(
    string Name,
    string Provider,
    string? BaseUrl,
    string? ApiKey,
    bool IsDefault);
