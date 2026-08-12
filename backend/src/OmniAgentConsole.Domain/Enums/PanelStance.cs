namespace OmniAgentConsole.Domain.Enums;

/// <summary>
/// Which side of the debate a persona defends. Multiple members may share the same stance;
/// at least one may take the opposite side.
/// </summary>
public enum PanelStance
{
    /// <summary>No forced side (typical for moderators).</summary>
    Neutral = 0,

    /// <summary>Defends the “for / pro / affirmative” side of the topic.</summary>
    For = 1,

    /// <summary>Defends the “against / con / negative” side of the topic.</summary>
    Against = 2,

    /// <summary>Free-form position described in <c>StanceLabel</c>.</summary>
    Custom = 3
}
