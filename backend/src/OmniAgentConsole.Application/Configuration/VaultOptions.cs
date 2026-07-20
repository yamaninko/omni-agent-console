namespace OmniAgentConsole.Application.Configuration;

public sealed class VaultOptions
{
    public const string SectionName = "Vault";

    public bool Enabled { get; set; }
    public string Address { get; set; } = "http://localhost:8201";
    public string Token { get; set; } = "dev-root-token";
    public string Mount { get; set; } = "secret";
    public string OmniAgentApiKeyPath { get; set; } = "providers/omniagent";
}
