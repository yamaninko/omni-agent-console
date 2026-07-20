namespace OmniAgentConsole.Application.Settings;

public sealed record UpdateOmniAgentApiKeyResponse(bool ApiKeyConfigured, string SecretStore, string SecretName);
