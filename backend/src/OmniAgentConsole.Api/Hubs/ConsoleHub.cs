using Microsoft.AspNetCore.SignalR;

namespace OmniAgentConsole.Api.Hubs;

public sealed class ConsoleHub : Hub
{
    public static string TaskGroup(Guid taskRunId) => $"task:{taskRunId:N}";

    public Task SubscribeTask(Guid taskRunId)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, TaskGroup(taskRunId));
    }

    public Task UnsubscribeTask(Guid taskRunId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, TaskGroup(taskRunId));
    }
}
