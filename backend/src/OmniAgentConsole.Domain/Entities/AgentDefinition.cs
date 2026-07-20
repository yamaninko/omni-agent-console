using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Domain.Entities;

public sealed class AgentDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public AgentType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string DefaultModel { get; set; } = string.Empty;
    public string? FallbackModels { get; set; } // comma-separated; tried in order when the primary model fails
    public string SystemPrompt { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 4096;
    public decimal Temperature { get; set; } = 0.2m;
    public int TimeoutSeconds { get; set; } = 120;
    public int RetryCount { get; set; } = 2;
    public ProviderType Provider { get; set; } = ProviderType.OmniAgent;
    public string? CustomApiUrl { get; set; }
    public string? CustomApiKey { get; set; }
    public Guid? ApiCredentialId { get; set; }
    public ApiCredential? ApiCredential { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
