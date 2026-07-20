using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Domain.Entities;

public sealed class ModelProviderSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ProviderType Provider { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKeySecretName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string DefaultModel { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 120;
    public int RetryCount { get; set; } = 2;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
