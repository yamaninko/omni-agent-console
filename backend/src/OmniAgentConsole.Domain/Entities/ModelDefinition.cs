using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Domain.Entities;

public sealed class ModelDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ProviderType Provider { get; set; }
    public string Model { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool SupportsChat { get; set; } = true;
    public bool SupportsEmbeddings { get; set; }
    public int? ContextWindow { get; set; }
    public bool Enabled { get; set; } = true;
}
