namespace OmniAgentConsole.Application.Secrets;

/// <summary>
/// Pure helpers for credential secret paths, placeholder detection, and masking.
/// </summary>
public static class ApiCredentialSecretPolicy
{
    public const string SecretKeyName = "apiKey";
    public const string SecretPathPrefix = "providers/credentials";

    public static string BuildSecretPath(Guid credentialId) =>
        $"{SecretPathPrefix}/{credentialId:D}";

    /// <summary>Seed rows use YOUR_…_HERE placeholders; treat as unconfigured.</summary>
    public static bool IsPlaceholderKey(string? apiKey) =>
        !string.IsNullOrWhiteSpace(apiKey)
        && apiKey.StartsWith("YOUR_", StringComparison.Ordinal)
        && apiKey.EndsWith("_HERE", StringComparison.Ordinal);

    public static bool IsRealKey(string? apiKey) =>
        !string.IsNullOrWhiteSpace(apiKey) && !IsPlaceholderKey(apiKey);

    public static string? ExtractLastFour(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return null;
        }

        return apiKey.Length <= 4 ? apiKey : apiKey[^4..];
    }

    public static string? BuildMaskedPreview(string apiKey)
    {
        if (!IsRealKey(apiKey))
        {
            return null;
        }

        return apiKey.Length <= 8 ? "****" : $"{apiKey[..4]}...{apiKey[^4..]}";
    }

    public static string? BuildMaskedPreviewFromLastFour(string? lastFour)
    {
        if (string.IsNullOrWhiteSpace(lastFour))
        {
            return null;
        }

        return lastFour.Length <= 4 ? $"****{lastFour}" : $"...{lastFour[^4..]}";
    }

    public static bool IsConfigured(string? secretPath, string? legacyApiKey) =>
        !string.IsNullOrWhiteSpace(secretPath) || IsRealKey(legacyApiKey);
}
