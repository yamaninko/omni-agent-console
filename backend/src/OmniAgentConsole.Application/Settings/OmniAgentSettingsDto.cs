namespace OmniAgentConsole.Application.Settings;

public sealed record OmniAgentSettingsDto(
    string Provider,
    string BaseUrl,
    string DefaultModel,
    string ApiKeySecretName,
    bool ApiKeyConfigured,
    string SecretStore,
    int TimeoutSeconds,
    int RetryCount,
    /// <summary>True when SHARED_LAB / SharedLab:Enabled is on.</summary>
    bool SharedLabEnabled = false,
    /// <summary>True for console-key holders (instructor); students are false when shared-lab is on.</summary>
    bool IsAdmin = true,
    /// <summary>Present for shared-lab students (and instructors if useful).</summary>
    StudentQuotaDto? Quota = null);

/// <summary>Soft lab quotas for the caller's studio session.</summary>
public sealed record StudentQuotaDto(
    int MaxConcurrent,
    int UsedConcurrent,
    int MaxDailyTasks,
    int UsedDailyTasks,
    int MaxDailyTokens,
    long UsedDailyTokens);
