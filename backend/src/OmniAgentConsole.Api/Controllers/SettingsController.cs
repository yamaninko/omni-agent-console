using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OmniAgentConsole.Api.Middleware;
using OmniAgentConsole.Application.Configuration;
using OmniAgentConsole.Application.Providers;
using OmniAgentConsole.Application.Secrets;
using OmniAgentConsole.Application.Settings;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Api.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly IOptions<OmniAgentProviderOptions> omniAgentOptions;
    private readonly IProviderSecretResolver providerSecretResolver;
    private readonly IProviderHealthCheck providerHealthCheck;
    private readonly ISecretStore secretStore;
    private readonly IApiCredentialKeyResolver credentialKeys;
    private readonly AgentConsoleDbContext dbContext;
    private readonly SharedLabOptions sharedLab;

    public SettingsController(
        IOptions<OmniAgentProviderOptions> omniAgentOptions,
        IProviderSecretResolver providerSecretResolver,
        IProviderHealthCheck providerHealthCheck,
        ISecretStore secretStore,
        IApiCredentialKeyResolver credentialKeys,
        AgentConsoleDbContext dbContext,
        IOptions<SharedLabOptions> sharedLab)
    {
        this.omniAgentOptions = omniAgentOptions;
        this.providerSecretResolver = providerSecretResolver;
        this.providerHealthCheck = providerHealthCheck;
        this.secretStore = secretStore;
        this.credentialKeys = credentialKeys;
        this.dbContext = dbContext;
        this.sharedLab = sharedLab.Value;
    }

    [HttpGet]
    public async Task<ActionResult<OmniAgentSettingsDto>> Get(CancellationToken cancellationToken)
    {
        var options = omniAgentOptions.Value;
        var apiKeyConfigured = await providerSecretResolver.HasOmniAgentApiKeyAsync(cancellationToken);
        // When shared-lab is off, everyone is effectively "admin" (full nav).
        // When on, only console-key holders get IsAdmin = true (students get false).
        var isAdmin = !sharedLab.Enabled || SharedLabHttp.IsAdmin(HttpContext);

        return Ok(new OmniAgentSettingsDto(
            "OmniAgent",
            options.BaseUrl,
            options.DefaultModel,
            options.ApiKeySecretName,
            apiKeyConfigured,
            secretStore.StoreName,
            options.TimeoutSeconds,
            options.RetryCount,
            sharedLab.Enabled,
            isAdmin));
    }

    [HttpPut("omniagent/api-key")]
    public async Task<ActionResult<UpdateOmniAgentApiKeyResponse>> UpdateOmniAgentApiKey(
        UpdateOmniAgentApiKeyRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return BadRequest("OMNIAGENT API key is required.");
        }

        var trimmed = request.ApiKey.Trim();
        await providerSecretResolver.SetOmniAgentApiKeyAsync(trimmed, cancellationToken);

        // Also re-seed the default NVIDIA / OmniAgent credential Vault path so
        // Studio + Panel (which resolve via apiCredentialId) work after a Vault
        // dev-mode restart wiped secret/providers/credentials/*.
        var defaultCredential = await dbContext.ApiCredentials
            .Where(c => c.IsDefault
                || c.Provider == "OmniAgent"
                || c.Provider == "NVIDIA"
                || c.Provider == "Nvidia")
            .OrderByDescending(c => c.IsDefault)
            .FirstOrDefaultAsync(cancellationToken);
        if (defaultCredential is not null)
        {
            await credentialKeys.PersistKeyAsync(defaultCredential, trimmed, cancellationToken);
            defaultCredential.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var configured = await providerSecretResolver.HasOmniAgentApiKeyAsync(cancellationToken);

        return Ok(new UpdateOmniAgentApiKeyResponse(
            configured,
            secretStore.StoreName,
            omniAgentOptions.Value.ApiKeySecretName));
    }

    [HttpPost("omniagent/health")]
    public async Task<ActionResult<ProviderHealthStatusDto>> CheckOmniAgentHealth(CancellationToken cancellationToken)
    {
        return Ok(await providerHealthCheck.CheckOmniAgentAsync(cancellationToken));
    }
}
