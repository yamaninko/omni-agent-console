using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Application.Panels;

public sealed record AgentGroupSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    int MemberCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record AgentGroupMemberDto(
    Guid Id,
    Guid GroupId,
    string DisplayName,
    string SystemPrompt,
    string DefaultModel,
    string? FallbackModels,
    string Provider,
    Guid? ApiCredentialId,
    int MaxTokens,
    decimal Temperature,
    int TimeoutSeconds,
    int RetryCount,
    int SortOrder,
    bool Enabled,
    string Role,
    string Stance,
    string? StanceLabel,
    DateTimeOffset CreatedAt);

public sealed record AgentGroupDetailDto(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<AgentGroupMemberDto> Members);

public sealed record CreateAgentGroupRequest(string Name, string? Description);

public sealed record UpdateAgentGroupRequest(string Name, string? Description);

public sealed record UpsertAgentGroupMemberRequest(
    string DisplayName,
    string SystemPrompt,
    string DefaultModel,
    string? FallbackModels,
    ProviderType Provider,
    Guid? ApiCredentialId,
    int MaxTokens,
    decimal Temperature,
    int TimeoutSeconds,
    int RetryCount,
    int SortOrder,
    bool Enabled,
    PanelMemberRole Role = PanelMemberRole.Commentator,
    PanelStance Stance = PanelStance.Neutral,
    string? StanceLabel = null);

public sealed record ReorderMembersRequest(IReadOnlyList<Guid> MemberIdsInOrder);

public sealed record CreatePanelSessionRequest(
    Guid GroupId,
    string Topic,
    string? Title,
    int MaxRounds = 1);

public sealed record ContinuePanelRequest(string Message, int ExtraRounds = 1);

/// <summary>Audience question injected while a panel is live (next turns see it in context).</summary>
public sealed record InjectPanelMessageRequest(string Message);

public sealed record CastPanelVoteRequest(Guid MemberId);

public sealed record PanelVoteTallyDto(Guid MemberId, string DisplayName, int Votes);

public sealed record PanelTurnDto(
    Guid Id,
    Guid MemberId,
    string MemberDisplayName,
    int TurnOrder,
    string? Output,
    string Status,
    string? ModelUsed,
    int InputTokens,
    int OutputTokens,
    int TotalTokens,
    long LatencyMs,
    string? ErrorMessage,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record PanelConsoleEventDto(
    Guid Id,
    Guid TaskRunId,
    Guid? AgentRunId,
    string EventType,
    string Message,
    string? PayloadJson,
    DateTimeOffset CreatedAt);

public sealed record PanelSessionSummaryDto(
    Guid Id,
    Guid GroupId,
    string GroupName,
    string Title,
    string Topic,
    string Status,
    int MaxRounds,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int TotalTokens,
    long TotalLatencyMs);

public sealed record PanelSessionDetailDto(
    Guid Id,
    Guid GroupId,
    string GroupName,
    string Title,
    string Topic,
    string Status,
    int MaxRounds,
    Guid? CurrentMemberId,
    DateTimeOffset? FloorDeadline,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalTokens,
    long TotalLatencyMs,
    string? ErrorMessage,
    IReadOnlyList<PanelTurnDto> Turns,
    IReadOnlyList<PanelConsoleEventDto> ConsoleEvents,
    IReadOnlyList<PanelVoteTallyDto> Votes);
