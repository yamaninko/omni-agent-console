using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Application.Agents;

public sealed record AgentDefinitionDto(
    Guid Id,
    string Name,
    AgentType Type,
    string Description,
    bool Enabled,
    string DefaultModel,
    string SystemPrompt,
    int MaxTokens,
    decimal Temperature,
    int TimeoutSeconds,
    int RetryCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    ProviderType Provider,
    string? CustomApiUrl,
    bool CustomApiKeyConfigured,
    Guid? ApiCredentialId,
    string? FallbackModels);
