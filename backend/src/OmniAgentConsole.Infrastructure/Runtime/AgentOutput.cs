using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Infrastructure.Runtime;

/// <summary>One agent's contribution, passed forward through the pipeline.</summary>
public sealed record AgentOutput(string Name, AgentType Type, string Content);
