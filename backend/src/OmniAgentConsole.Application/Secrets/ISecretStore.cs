namespace OmniAgentConsole.Application.Secrets;

public interface ISecretStore
{
    string StoreName { get; }

    /// <summary>
    /// When false (e.g. environment-only lab mode), credential secrets stay in the DB
    /// legacy column. When true (Vault), secrets are written only to the store.
    /// </summary>
    bool IsWritable { get; }

    Task<bool> ExistsAsync(string path, string key, CancellationToken cancellationToken);
    Task<string?> GetSecretAsync(string path, string key, CancellationToken cancellationToken);
    Task SetSecretAsync(string path, string key, string value, CancellationToken cancellationToken);
    Task DeleteAsync(string path, string key, CancellationToken cancellationToken);
}
