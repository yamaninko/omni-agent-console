using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Application.Providers;

public sealed record ModelResponse(
    ProviderType Provider,
    string Model,
    string Content,
    string? FinishReason,
    int? InputTokens,
    int? OutputTokens,
    int? TotalTokens,
    string? RawMetadataJson,
    IReadOnlyList<ChatToolCall>? ToolCalls = null);
