using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OmniAgentConsole.Application.Configuration;
using OmniAgentConsole.Application.Providers;
using OmniAgentConsole.Application.Secrets;
using OmniAgentConsole.Application.Settings;

namespace OmniAgentConsole.Api.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly IOptions<OmniAgentProviderOptions> omniAgentOptions;
    private readonly IProviderSecretResolver providerSecretResolver;
    private readonly IProviderHealthCheck providerHealthCheck;
    private readonly ISecretStore secretStore;

    public SettingsController(
        IOptions<OmniAgentProviderOptions> omniAgentOptions,
        IProviderSecretResolver providerSecretResolver,
        IProviderHealthCheck providerHealthCheck,
        ISecretStore secretStore)
    {
        this.omniAgentOptions = omniAgentOptions;
        this.providerSecretResolver = providerSecretResolver;
        this.providerHealthCheck = providerHealthCheck;
        this.secretStore = secretStore;
    }

    [HttpGet]
    public async Task<ActionResult<OmniAgentSettingsDto>> Get(CancellationToken cancellationToken)
    {
        var options = omniAgentOptions.Value;
        var apiKeyConfigured = await providerSecretResolver.HasOmniAgentApiKeyAsync(cancellationToken);

        return Ok(new OmniAgentSettingsDto(
            "OmniAgent",
            options.BaseUrl,
            options.DefaultModel,
            options.ApiKeySecretName,
            apiKeyConfigured,
            secretStore.StoreName,
            options.TimeoutSeconds,
            options.RetryCount));
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

        await providerSecretResolver.SetOmniAgentApiKeyAsync(request.ApiKey.Trim(), cancellationToken);
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
