using Microsoft.AspNetCore.SignalR;
using OmniAgentConsole.Api.Hubs;
using OmniAgentConsole.Application.Realtime;

namespace OmniAgentConsole.Api.Realtime;

public sealed class SignalRConsoleEventPublisher : IConsoleEventPublisher
{
    private readonly IHubContext<ConsoleHub> hubContext;

    public SignalRConsoleEventPublisher(IHubContext<ConsoleHub> hubContext)
    {
        this.hubContext = hubContext;
    }

    public Task PublishAsync(ConsoleEventEnvelope envelope, CancellationToken cancellationToken)
    {
        return hubContext.Clients
            .Group(ConsoleHub.TaskGroup(envelope.TaskRunId))
            .SendAsync("ReceiveConsoleEvent", envelope, cancellationToken);
    }
}
