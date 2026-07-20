using OmniAgentConsole.Application.Secrets;

namespace OmniAgentConsole.Infrastructure.Secrets;

public sealed class EnvironmentSecretStore : ISecretStore
{
    public string StoreName => "Environment";

    /// <summary>Not writable — credential keys stay in the DB legacy column in this mode.</summary>
    public bool IsWritable => false;

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

    public Task DeleteAsync(string path, string key, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
