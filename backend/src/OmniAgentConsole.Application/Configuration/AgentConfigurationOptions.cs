namespace OmniAgentConsole.Application.Configuration;

public sealed class AgentConfigurationOptions
{
    public const string SectionName = "Agents";

    public List<AgentDefinitionOptions> Defaults { get; set; } = new();
}

public sealed class AgentDefinitionOptions
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string DefaultModel { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 4096;
    public decimal Temperature { get; set; } = 0.2m;
    public int TimeoutSeconds { get; set; } = 120;
    public int RetryCount { get; set; } = 2;
}
