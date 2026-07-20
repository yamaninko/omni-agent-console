using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using OmniAgentConsole.Application.Configuration;
using OmniAgentConsole.Application.Secrets;

namespace OmniAgentConsole.Infrastructure.Secrets;

public sealed class VaultSecretStore : ISecretStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly VaultOptions options;

    public VaultSecretStore(HttpClient httpClient, IOptions<VaultOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.httpClient.BaseAddress = new Uri(this.options.Address.TrimEnd('/') + "/");
        this.httpClient.DefaultRequestHeaders.Remove("X-Vault-Token");
        this.httpClient.DefaultRequestHeaders.Add("X-Vault-Token", this.options.Token);
    }

    public string StoreName => "HashiCorp Vault";

    public bool IsWritable => true;

    public async Task<bool> ExistsAsync(string path, string key, CancellationToken cancellationToken)
    {
        var value = await GetSecretAsync(path, key, cancellationToken);
        return !string.IsNullOrWhiteSpace(value);
    }

    public async Task<string?> GetSecretAsync(string path, string key, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(BuildKv2Path(path), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<VaultKv2ReadResponse>(JsonOptions, cancellationToken);
        if (payload?.Data?.Data is null)
        {
            return null;
        }

        return payload.Data.Data.TryGetValue(key, out var value) ? value.GetString() : null;
    }

    public async Task SetSecretAsync(string path, string key, string value, CancellationToken cancellationToken)
    {
        var payload = new VaultKv2WriteRequest(new Dictionary<string, string>
        {
            [key] = value,
            ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O")
        });

        var response = await httpClient.PostAsJsonAsync(BuildKv2Path(path), payload, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(string path, string key, CancellationToken cancellationToken)
    {
        // KV v2 metadata delete removes all versions of the secret.
        var response = await httpClient.DeleteAsync(BuildKv2MetadataPath(path), cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    private string BuildKv2Path(string path)
    {
        var mount = Uri.EscapeDataString(options.Mount.Trim('/'));
        var normalizedPath = path.Trim('/');
        return $"v1/{mount}/data/{normalizedPath}";
    }

    private string BuildKv2MetadataPath(string path)
    {
        var mount = Uri.EscapeDataString(options.Mount.Trim('/'));
        var normalizedPath = path.Trim('/');
        return $"v1/{mount}/metadata/{normalizedPath}";
    }

    private sealed record VaultKv2WriteRequest(
        [property: JsonPropertyName("data")] Dictionary<string, string> Data);

    private sealed record VaultKv2ReadResponse(
        [property: JsonPropertyName("data")] VaultKv2Data? Data);

    private sealed record VaultKv2Data(
        [property: JsonPropertyName("data")] Dictionary<string, JsonElement>? Data);
}
