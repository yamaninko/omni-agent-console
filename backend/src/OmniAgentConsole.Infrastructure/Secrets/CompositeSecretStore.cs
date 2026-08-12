using OmniAgentConsole.Application.Secrets;

namespace OmniAgentConsole.Infrastructure.Secrets;

/// <summary>
/// Primary (Vault) + durable mirror (file). Reads prefer primary, then mirror.
/// Writes go to both so Vault -dev wipe does not lose lab keys.
/// </summary>
public sealed class CompositeSecretStore : ISecretStore
{
    private readonly ISecretStore primary;
    private readonly ISecretStore mirror;

    public CompositeSecretStore(ISecretStore primary, ISecretStore mirror)
    {
        this.primary = primary;
        this.mirror = mirror;
    }

    public string StoreName => $"{primary.StoreName} + durable mirror";

    public bool IsWritable => primary.IsWritable || mirror.IsWritable;

    public async Task<bool> ExistsAsync(string path, string key, CancellationToken cancellationToken)
    {
        if (await primary.ExistsAsync(path, key, cancellationToken))
        {
            return true;
        }

        return await mirror.ExistsAsync(path, key, cancellationToken);
    }

    public async Task<string?> GetSecretAsync(string path, string key, CancellationToken cancellationToken)
    {
        var fromPrimary = await primary.GetSecretAsync(path, key, cancellationToken);
        if (!string.IsNullOrWhiteSpace(fromPrimary))
        {
            return fromPrimary;
        }

        return await mirror.GetSecretAsync(path, key, cancellationToken);
    }

    public async Task SetSecretAsync(string path, string key, string value, CancellationToken cancellationToken)
    {
        if (primary.IsWritable)
        {
            await primary.SetSecretAsync(path, key, value, cancellationToken);
        }

        if (mirror.IsWritable)
        {
            await mirror.SetSecretAsync(path, key, value, cancellationToken);
        }
    }

    public async Task DeleteAsync(string path, string key, CancellationToken cancellationToken)
    {
        if (primary.IsWritable)
        {
            await primary.DeleteAsync(path, key, cancellationToken);
        }

        if (mirror.IsWritable)
        {
            await mirror.DeleteAsync(path, key, cancellationToken);
        }
    }
}
