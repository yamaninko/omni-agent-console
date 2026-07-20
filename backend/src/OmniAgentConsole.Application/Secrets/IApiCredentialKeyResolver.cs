using OmniAgentConsole.Domain.Entities;

namespace OmniAgentConsole.Application.Secrets;

public interface IApiCredentialKeyResolver
{
    /// <summary>
    /// Resolves the usable API key for a credential (Vault path first, then legacy column).
    /// Optional <paramref name="agentLegacyCustomKey"/> covers agent-level CustomApiKey leftovers.
    /// </summary>
    Task<string?> ResolveAsync(
        ApiCredential? credential,
        string? agentLegacyCustomKey,
        CancellationToken cancellationToken);

    Task<string?> ResolveByIdAsync(Guid credentialId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists a new key: Vault when writable, otherwise the legacy ApiKey column.
    /// Updates secret path / last-four metadata on the entity (caller saves).
    /// </summary>
    Task PersistKeyAsync(ApiCredential credential, string apiKey, CancellationToken cancellationToken);

    Task DeleteKeyAsync(ApiCredential credential, CancellationToken cancellationToken);

    /// <summary>
    /// One-shot: move real plaintext keys from the DB column into the writable secret store.
    /// No-op when the store is not writable.
    /// </summary>
    Task MigratePlaintextKeysAsync(CancellationToken cancellationToken);
}