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
    bool IsAdmin = true);
