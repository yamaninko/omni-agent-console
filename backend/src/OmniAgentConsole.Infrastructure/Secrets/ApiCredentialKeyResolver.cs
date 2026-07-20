using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniAgentConsole.Application.Secrets;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Infrastructure.Secrets;

public sealed class ApiCredentialKeyResolver : IApiCredentialKeyResolver
{
    private readonly AgentConsoleDbContext dbContext;
    private readonly ISecretStore secretStore;
    private readonly ILogger<ApiCredentialKeyResolver> logger;

    public ApiCredentialKeyResolver(
        AgentConsoleDbContext dbContext,
        ISecretStore secretStore,
        ILogger<ApiCredentialKeyResolver> logger)
    {
        this.dbContext = dbContext;
        this.secretStore = secretStore;
        this.logger = logger;
    }

    public async Task<string?> ResolveAsync(
        ApiCredential? credential,
        string? agentLegacyCustomKey,
        CancellationToken cancellationToken)
    {
        if (credential is not null)
        {
            if (!string.IsNullOrWhiteSpace(credential.ApiKeySecretPath))
            {
                var keyName = string.IsNullOrWhiteSpace(credential.ApiKeySecretKey)
                    ? ApiCredentialSecretPolicy.SecretKeyName
                    : credential.ApiKeySecretKey;
                var fromStore = await secretStore.GetSecretAsync(
                    credential.ApiKeySecretPath,
                    keyName,
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(fromStore))
                {
                    return fromStore;
                }

                logger.LogWarning(
                    "Credential {CredentialId} references secret path {Path} but the store returned empty.",
                    credential.Id,
                    credential.ApiKeySecretPath);
            }

            if (ApiCredentialSecretPolicy.IsRealKey(credential.ApiKey))
            {
                logger.LogWarning(
                    "Credential {CredentialId} is still using the legacy plaintext ApiKey column.",
                    credential.Id);
                return credential.ApiKey;
            }
        }

        if (ApiCredentialSecretPolicy.IsRealKey(agentLegacyCustomKey))
        {
            return agentLegacyCustomKey;
        }

        return null;
    }

    public async Task<string?> ResolveByIdAsync(Guid credentialId, CancellationToken cancellationToken)
    {
        var credential = await dbContext.ApiCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == credentialId, cancellationToken);
        return await ResolveAsync(credential, null, cancellationToken);
    }

    public async Task PersistKeyAsync(ApiCredential credential, string apiKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var trimmed = apiKey.Trim();
        credential.KeyLastFour = ApiCredentialSecretPolicy.ExtractLastFour(trimmed);

        if (secretStore.IsWritable)
        {
            var path = ApiCredentialSecretPolicy.BuildSecretPath(credential.Id);
            await secretStore.SetSecretAsync(
                path,
                ApiCredentialSecretPolicy.SecretKeyName,
                trimmed,
                cancellationToken);
            credential.ApiKeySecretPath = path;
            credential.ApiKeySecretKey = ApiCredentialSecretPolicy.SecretKeyName;
            credential.ApiKey = null;
            return;
        }

        // Lab mode without Vault: keep the legacy column.
        credential.ApiKey = trimmed;
        credential.ApiKeySecretPath = null;
        credential.ApiKeySecretKey = null;
    }

    public async Task DeleteKeyAsync(ApiCredential credential, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(credential.ApiKeySecretPath) && secretStore.IsWritable)
        {
            var keyName = string.IsNullOrWhiteSpace(credential.ApiKeySecretKey)
                ? ApiCredentialSecretPolicy.SecretKeyName
                : credential.ApiKeySecretKey;
            try
            {
                await secretStore.DeleteAsync(credential.ApiKeySecretPath, keyName, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to delete secret for credential {CredentialId} at {Path}.",
                    credential.Id,
                    credential.ApiKeySecretPath);
            }
        }

        credential.ApiKey = null;
        credential.ApiKeySecretPath = null;
        credential.ApiKeySecretKey = null;
        credential.KeyLastFour = null;
    }

    public async Task MigratePlaintextKeysAsync(CancellationToken cancellationToken)
    {
        if (!secretStore.IsWritable)
        {
            return;
        }

        var candidates = await dbContext.ApiCredentials
            .Where(c => c.ApiKeySecretPath == null && c.ApiKey != null && c.ApiKey != "")
            .ToListAsync(cancellationToken);

        var migrated = 0;
        foreach (var credential in candidates)
        {
            if (!ApiCredentialSecretPolicy.IsRealKey(credential.ApiKey))
            {
                continue;
            }

            var plaintext = credential.ApiKey!;
            await PersistKeyAsync(credential, plaintext, cancellationToken);
            credential.UpdatedAt = DateTimeOffset.UtcNow;
            migrated++;
        }

        if (migrated > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Migrated {Count} API credential key(s) from the plaintext column into {Store}.",
                migrated,
                secretStore.StoreName);
        }
    }
}
