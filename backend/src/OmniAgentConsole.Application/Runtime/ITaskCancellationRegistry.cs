namespace OmniAgentConsole.Application.Runtime;

public interface ITaskCancellationRegistry
{
    CancellationToken CreateToken(Guid taskRunId, CancellationToken parentToken);
    bool Cancel(Guid taskRunId);
    void Complete(Guid taskRunId);
}
