namespace OmniAgentConsole.Application.Secrets;

public interface ISecretStore
{
    string StoreName { get; }
    Task<bool> ExistsAsync(string path, string key, CancellationToken cancellationToken);
    Task<string?> GetSecretAsync(string path, string key, CancellationToken cancellationToken);
    Task SetSecretAsync(string path, string key, string value, CancellationToken cancellationToken);
}
