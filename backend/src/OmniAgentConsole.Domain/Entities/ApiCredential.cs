using System;

namespace OmniAgentConsole.Domain.Entities;

public sealed class ApiCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty; // e.g. "OmniAgent", "OpenAI", "Anthropic", "Gemini", "Ollama", "Custom"
    public string? BaseUrl { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
