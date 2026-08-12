using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Domain.Entities;

/// <summary>
/// Stream + replay log for a panel session (mirrors ConsoleEvent without TaskRun FK).
/// </summary>
public sealed class PanelConsoleEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PanelSessionId { get; set; }
    public Guid? PanelTurnId { get; set; }
    public ConsoleEventType EventType { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public PanelSession? PanelSession { get; set; }
    public PanelTurn? PanelTurn { get; set; }
}
