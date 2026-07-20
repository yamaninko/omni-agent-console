namespace OmniAgentConsole.Application.Agents;

public sealed record CreateModelDefinitionRequest(
    string Model,
    string DisplayName,
    int? ContextWindow);
