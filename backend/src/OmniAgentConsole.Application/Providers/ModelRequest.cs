using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Application.Providers;

public sealed record ModelRequest(
    ProviderType Provider,
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    decimal Temperature,
    int MaxTokens,
    int TimeoutSeconds,
    IReadOnlyDictionary<string, string>? Metadata = null,
    IReadOnlyList<ToolDefinition>? Tools = null);
