namespace OmniAgentConsole.Application.Agents;

using OmniAgentConsole.Domain.Enums;

public sealed record UpdateAgentDefinitionRequest(
    bool Enabled,
    string DefaultModel,
    string SystemPrompt,
    int MaxTokens,
    decimal Temperature,
    int TimeoutSeconds,
    int RetryCount,
    ProviderType Provider,
    string? CustomApiUrl,
    string? CustomApiKey,
    Guid? ApiCredentialId = null,
    string? FallbackModels = null,
    string? Name = null,
    string? Description = null,
    AgentType? Type = null);
