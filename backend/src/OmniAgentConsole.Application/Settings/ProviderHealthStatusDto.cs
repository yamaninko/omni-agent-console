namespace OmniAgentConsole.Application.Settings;

public sealed record ProviderHealthStatusDto(
    string Provider,
    string Model,
    bool ApiKeyConfigured,
    bool Healthy,
    string Status,
    string Message,
    long LatencyMs,
    DateTimeOffset CheckedAt);
