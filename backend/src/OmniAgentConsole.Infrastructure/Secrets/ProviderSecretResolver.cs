using Microsoft.Extensions.Options;
using OmniAgentConsole.Application.Configuration;
using OmniAgentConsole.Application.Secrets;

namespace OmniAgentConsole.Infrastructure.Secrets;

public sealed class ProviderSecretResolver : IProviderSecretResolver
{
    private readonly ISecretStore secretStore;
    private readonly OmniAgentProviderOptions omniAgentOptions;
    private readonly VaultOptions vaultOptions;

    public ProviderSecretResolver(
        ISecretStore secretStore,
        IOptions<OmniAgentProviderOptions> omniAgentOptions,
        IOptions<VaultOptions> vaultOptions)
    {
        this.secretStore = secretStore;
        this.omniAgentOptions = omniAgentOptions.Value;
        this.vaultOptions = vaultOptions.Value;
    }

    public Task<string?> GetOmniAgentApiKeyAsync(CancellationToken cancellationToken)
    {
        var reference = GetOmniAgentSecretReference();
        return GetSecretWithEnvironmentFallbackAsync(reference, cancellationToken);
    }

    public async Task<bool> HasOmniAgentApiKeyAsync(CancellationToken cancellationToken)
    {
        var reference = GetOmniAgentSecretReference();
        if (await secretStore.ExistsAsync(reference.Path, reference.Key, cancellationToken))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(omniAgentOptions.ApiKeyEnvironmentVariable));
    }

    public Task SetOmniAgentApiKeyAsync(string apiKey, CancellationToken cancellationToken)
    {
        var reference = GetOmniAgentSecretReference();
        return secretStore.SetSecretAsync(reference.Path, reference.Key, apiKey, cancellationToken);
    }

    private SecretReference GetOmniAgentSecretReference()
    {
        if (vaultOptions.Enabled)
        {
            return new SecretReference(vaultOptions.OmniAgentApiKeyPath, "apiKey");
        }

        return new SecretReference(string.Empty, omniAgentOptions.ApiKeyEnvironmentVariable);
    }

    private async Task<string?> GetSecretWithEnvironmentFallbackAsync(
        SecretReference reference,
        CancellationToken cancellationToken)
    {
        var secret = await secretStore.GetSecretAsync(reference.Path, reference.Key, cancellationToken);
        if (!string.IsNullOrWhiteSpace(secret))
        {
            return secret;
        }

        return Environment.GetEnvironmentVariable(omniAgentOptions.ApiKeyEnvironmentVariable);
    }

    private sealed record SecretReference(string Path, string Key);
}
