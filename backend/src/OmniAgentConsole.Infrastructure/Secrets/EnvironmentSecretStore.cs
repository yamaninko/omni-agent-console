using OmniAgentConsole.Application.Secrets;

namespace OmniAgentConsole.Infrastructure.Secrets;

public sealed class EnvironmentSecretStore : ISecretStore
{
    public string StoreName => "Environment";

    public Task<bool> ExistsAsync(string path, string key, CancellationToken cancellationToken)
    {
        return Task.FromResult(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)));
    }

    public Task<string?> GetSecretAsync(string path, string key, CancellationToken cancellationToken)
    {
        return Task.FromResult(Environment.GetEnvironmentVariable(key));
    }

    public Task SetSecretAsync(string path, string key, string value, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Environment-backed secrets are read-only. Enable Vault to update secrets from Settings.");
    }
}
