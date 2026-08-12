namespace OmniAgentConsole.Domain.Enums;

public enum ConsoleEventType
{
    TaskCreated = 0,
    TaskStarted = 1,
    PlannerStarted = 2,
    PlanCreated = 3,
    AgentStarted = 4,
    AgentStep = 5,
    ModelCallStarted = 6,
    ModelCallCompleted = 7,
    UsageRecorded = 8,
    AgentCompleted = 9,
    AgentFailed = 10,
    TaskCompleted = 11,
    TaskFailed = 12,
    Warning = 13,
    Error = 14,
    TaskCancelled = 15,

    /// <summary>A prompt typed by the user. Rendered as a right-aligned chat bubble.</summary>
    UserMessage = 16,

    /// <summary>Panel session started (topic announced).</summary>
    PanelStarted = 20,

    /// <summary>Moderator (system) granted the floor to a guest persona.</summary>
    PanelFloorGranted = 21,

    /// <summary>A guest finished their turn; message is the speech (truncated for stream).</summary>
    PanelTurnCompleted = 22,

    /// <summary>Panel session finished all single-round turns.</summary>
    PanelCompleted = 23
}
