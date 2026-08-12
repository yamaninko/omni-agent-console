namespace OmniAgentConsole.Application.Configuration;

public sealed class FileSecretStoreOptions
{
    public const string SectionName = "FileSecretStore";

    /// <summary>Root directory for durable secret files (survives Vault -dev restarts).</summary>
    public string RootPath { get; set; } = "/var/omni/secrets";

    /// <summary>When true with Vault enabled, writes also go to disk and reads fall back to disk.</summary>
    public bool MirrorEnabled { get; set; } = true;
}
