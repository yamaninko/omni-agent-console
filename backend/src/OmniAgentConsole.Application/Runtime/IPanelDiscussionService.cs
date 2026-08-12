using System;
using System.Threading;
using System.Threading.Tasks;

namespace OmniAgentConsole.Application.Runtime;

public interface IPanelDiscussionService
{
    Task RunSessionAsync(Guid panelSessionId, CancellationToken cancellationToken);
}
