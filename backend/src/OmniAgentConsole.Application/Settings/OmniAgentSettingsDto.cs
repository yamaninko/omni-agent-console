namespace OmniAgentConsole.Application.Settings;

public sealed record OmniAgentSettingsDto(
    string Provider,
    string BaseUrl,
    string DefaultModel,
    string ApiKeySecretName,
    bool ApiKeyConfigured,
    string SecretStore,
    int TimeoutSeconds,
    int RetryCount);
