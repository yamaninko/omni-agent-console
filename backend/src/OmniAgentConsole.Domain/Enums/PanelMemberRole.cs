namespace OmniAgentConsole.Domain.Enums;

/// <summary>
/// Role of a persona inside an agent group panel.
/// </summary>
public enum PanelMemberRole
{
    /// <summary>Panel guest / commentator — argues a stance on the topic.</summary>
    Commentator = 0,

    /// <summary>Moderator — opens the panel, keeps order; not required to pick a side.</summary>
    Moderator = 1
}
