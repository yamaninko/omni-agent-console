namespace OmniAgentConsole.Application.Providers;

/// <summary>
/// One chat turn. <paramref name="ToolCalls"/> is set on assistant messages that
/// request tool execution; <paramref name="ToolCallId"/> is set on "tool" role
/// messages carrying the result of a specific call back to the model.
/// </summary>
public sealed record ChatMessage(
    string Role,
    string Content,
    IReadOnlyList<ChatToolCall>? ToolCalls = null,
    string? ToolCallId = null);

/// <summary>A tool invocation requested by the model (OpenAI-compatible shape).</summary>
public sealed record ChatToolCall(string Id, string Name, string ArgumentsJson);
