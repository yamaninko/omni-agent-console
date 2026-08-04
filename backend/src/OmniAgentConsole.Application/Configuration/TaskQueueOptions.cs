namespace OmniAgentConsole.Application.Configuration;

public sealed class TaskQueueOptions
{
    public const string SectionName = "TaskQueue";

    public string Mode { get; set; } = "InMemory";
    public string QueueName { get; set; } = "omniagent-console.task-runs";
    // BasicGet empty-poll interval. 500ms kept the worker spinning; 1500ms is
    // still snappy for task start while cutting idle wakeups ~3× (helps Windows
    // Docker Desktop hosts with limited CPU).
    public int PollIntervalMilliseconds { get; set; } = 1500;
}
