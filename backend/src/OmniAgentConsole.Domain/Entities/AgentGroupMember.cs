using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Domain.Entities;

/// <summary>
/// Panel persona: display name + system prompt + model config. Not a pipeline AgentType.
/// Role (moderator/commentator) and stance (for/against/…) are chosen in the Groups UI.
/// </summary>
public sealed class AgentGroupMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string DefaultModel { get; set; } = string.Empty;
    public string? FallbackModels { get; set; }
    public ProviderType Provider { get; set; } = ProviderType.OmniAgent;
    public Guid? ApiCredentialId { get; set; }
    public int MaxTokens { get; set; } = 800;
    public decimal Temperature { get; set; } = 0.7m;
    /// <summary>Wall-clock budget for the model call (~1 minute speaking slot).</summary>
    public int TimeoutSeconds { get; set; } = 60;
    public int RetryCount { get; set; } = 1;
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Moderator opens the panel; commentators debate with a stance.</summary>
    public PanelMemberRole Role { get; set; } = PanelMemberRole.Commentator;

    /// <summary>Side of the debate this persona defends (moderators usually Neutral).</summary>
    public PanelStance Stance { get; set; } = PanelStance.Neutral;

    /// <summary>
    /// Short label for the position, e.g. "Remote work is better" or a custom thesis.
    /// Optional for Neutral; recommended for For/Against/Custom.
    /// </summary>
    public string? StanceLabel { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AgentGroup? Group { get; set; }
    public ApiCredential? ApiCredential { get; set; }
}
