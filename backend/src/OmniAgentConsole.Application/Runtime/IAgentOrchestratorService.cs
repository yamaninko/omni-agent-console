namespace OmniAgentConsole.Application.Runtime;

public interface IAgentOrchestratorService
{
    Task RunTaskAsync(Guid taskRunId, CancellationToken cancellationToken);
}
