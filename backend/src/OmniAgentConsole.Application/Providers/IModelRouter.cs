using OmniAgentConsole.Domain.Entities;

namespace OmniAgentConsole.Application.Providers;

public interface IModelRouter
{
    ModelRoute Resolve(AgentDefinition agentDefinition, string? requestedModel = null);
}
