using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniAgentConsole.Application.Configuration;
using OmniAgentConsole.Application.Secrets;

namespace OmniAgentConsole.Infrastructure.Secrets;

/// <summary>
/// Durable on-disk secret store for lab/local: Vault -dev loses memory on restart;
/// this keeps keys under a mounted volume so Panel/Studio survive compose recreate.
/// </summary>
public sealed class FileSecretStore : ISecretStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string rootPath;
    private readonly ILogger<FileSecretStore> logger;
    private readonly object gate = new();

    public FileSecretStore(IOptions<FileSecretStoreOptions> options, ILogger<FileSecretStore> logger)
    {
        this.rootPath = string.IsNullOrWhiteSpace(options.Value.RootPath)
            ? "/var/omni/secrets"
            : options.Value.RootPath.Trim();
        this.logger = logger;
        Directory.CreateDirectory(this.rootPath);
    }

    public string StoreName => $"File ({rootPath})";

    public bool IsWritable => true;

    public Task<bool> ExistsAsync(string path, string key, CancellationToken cancellationToken)
    {
        return Task.FromResult(!string.IsNullOrWhiteSpace(ReadKey(path, key)));
    }

    public Task<string?> GetSecretAsync(string path, string key, CancellationToken cancellationToken)
    {
        return Task.FromResult(ReadKey(path, key));
    }

    public Task SetSecretAsync(string path, string key, string value, CancellationToken cancellationToken)
    {
        WriteKey(path, key, value);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string path, string key, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var map = LoadFile(path);
            if (map.Remove(key))
            {
                SaveFile(path, map);
            }
        }

        return Task.CompletedTask;
    }

    private string? ReadKey(string path, string key)
    {
        lock (gate)
        {
            var map = LoadFile(path);
            return map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
        }
    }

    private void WriteKey(string path, string key, string value)
    {
        lock (gate)
        {
            var map = LoadFile(path);
            map[key] = value;
            SaveFile(path, map);
        }
    }

    private Dictionary<string, string> LoadFile(string path)
    {
        var file = ResolveFile(path);
        if (!File.Exists(file))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var json = File.ReadAllText(file);
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            return data is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(data, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read secret file {File}", file);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private void SaveFile(string path, Dictionary<string, string> map)
    {
        var file = ResolveFile(path);
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(map, JsonOptions);
        File.WriteAllText(file, json);
    }

    private string ResolveFile(string path)
    {
        var safe = string.Join(
            '_',
            (path ?? string.Empty)
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(segment =>
                {
                    var cleaned = new string(segment.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
                    return string.IsNullOrEmpty(cleaned) ? "x" : cleaned;
                }));
        if (string.IsNullOrEmpty(safe))
        {
            safe = "root";
        }

        return Path.Combine(rootPath, safe + ".json");
    }
}
