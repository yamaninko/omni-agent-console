namespace OmniAgentConsole.Application.Secrets;

public interface IProviderSecretResolver
{
    Task<string?> GetOmniAgentApiKeyAsync(CancellationToken cancellationToken);
    Task<bool> HasOmniAgentApiKeyAsync(CancellationToken cancellationToken);
    Task SetOmniAgentApiKeyAsync(string apiKey, CancellationToken cancellationToken);
}
