using System.Threading;
using System.Threading.Tasks;
using OmniAgentConsole.Application.Realtime;

namespace OmniAgentConsole.Infrastructure.Runtime;

public sealed class NullConsoleEventPublisher : IConsoleEventPublisher
{
    public Task PublishAsync(ConsoleEventEnvelope envelope, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
