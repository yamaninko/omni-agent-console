using System;

namespace OmniAgentConsole.Domain.Entities;

public sealed class ApiCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty; // e.g. "OmniAgent", "OpenAI", "Anthropic", "Gemini", "Ollama", "Custom"
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Legacy plaintext column. Cleared after migration to <see cref="ApiKeySecretPath"/>.
    /// Seed placeholders may still live here (YOUR_…_HERE) and count as unconfigured.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Vault (or secret store) path, e.g. providers/credentials/{id}.</summary>
    public string? ApiKeySecretPath { get; set; }

    /// <summary>Key name inside the secret payload (default: apiKey).</summary>
    public string? ApiKeySecretKey { get; set; }

    /// <summary>Last four characters for UI masking without reading the secret store.</summary>
    public string? KeyLastFour { get; set; }

    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
