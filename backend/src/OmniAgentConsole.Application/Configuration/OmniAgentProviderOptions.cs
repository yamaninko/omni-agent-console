namespace OmniAgentConsole.Application.Configuration;

public sealed class OmniAgentProviderOptions
{
    public const string SectionName = "Providers:OmniAgent";

    public string BaseUrl { get; set; } = "https://integrate.api.nvidia.com/v1";
    public string ApiKeyEnvironmentVariable { get; set; } = "OMNIAGENT_API_KEY";
    public string ApiKeySecretName { get; set; } = "secret/providers/omniagent#apiKey";
    public string DefaultModel { get; set; } = "meta/llama-3.1-8b-instruct";
    public int TimeoutSeconds { get; set; } = 120;
    public int RetryCount { get; set; } = 2;
}
