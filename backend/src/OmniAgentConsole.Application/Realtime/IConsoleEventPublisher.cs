namespace OmniAgentConsole.Application.Realtime;

public interface IConsoleEventPublisher
{
    Task PublishAsync(ConsoleEventEnvelope envelope, CancellationToken cancellationToken);
}
