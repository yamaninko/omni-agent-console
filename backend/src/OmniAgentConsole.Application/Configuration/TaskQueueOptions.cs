namespace OmniAgentConsole.Application.Configuration;

public sealed class TaskQueueOptions
{
    public const string SectionName = "TaskQueue";

    public string Mode { get; set; } = "InMemory";
    public string QueueName { get; set; } = "omniagent-console.task-runs";
    public int PollIntervalMilliseconds { get; set; } = 500;
}
