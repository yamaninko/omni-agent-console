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
    TaskCancelled = 15
}
