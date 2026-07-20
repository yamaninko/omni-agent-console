using System;

namespace OmniAgentConsole.Application.Agents;

public sealed record ModelDefinitionDto(
    Guid Id,
    string Model,
    string DisplayName,
    int? ContextWindow);
