using OmniAgentConsole.Application.Secrets;

namespace OmniAgentConsole.UnitTests;

public sealed class ApiCredentialSecretPolicyTests
{
    [Fact]
    public void BuildSecretPath_IsStableAndPathSafe()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Assert.Equal(
            "providers/credentials/11111111-1111-1111-1111-111111111111",
            ApiCredentialSecretPolicy.BuildSecretPath(id));
    }

    [Theory]
    [InlineData("YOUR_NVIDIA_API_KEY_HERE", true)]
    [InlineData("YOUR_OPENAI_API_KEY_HERE", true)]
    [InlineData("nvapi-real-key-value-here", false)]
    [InlineData("sk-abcdefghijklmnopqrstuvwxyz", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsPlaceholderKey_DetectsSeedPlaceholders(string? key, bool expected)
    {
        Assert.Equal(expected, ApiCredentialSecretPolicy.IsPlaceholderKey(key));
        Assert.Equal(!expected && !string.IsNullOrWhiteSpace(key), ApiCredentialSecretPolicy.IsRealKey(key));
    }

    [Fact]
    public void IsConfigured_TrueWhenSecretPathOrRealLegacyKey()
    {
        Assert.True(ApiCredentialSecretPolicy.IsConfigured("providers/credentials/x", null));
        Assert.True(ApiCredentialSecretPolicy.IsConfigured(null, "sk-abc1234567890"));
        Assert.False(ApiCredentialSecretPolicy.IsConfigured(null, "YOUR_OPENAI_API_KEY_HERE"));
        Assert.False(ApiCredentialSecretPolicy.IsConfigured(null, null));
    }

    [Fact]
    public void MaskHelpers_HideMiddleOfKey()
    {
        Assert.Equal("sk-a...cdef", ApiCredentialSecretPolicy.BuildMaskedPreview("sk-abcdefghijcdef"));
        Assert.Equal("****cdef", ApiCredentialSecretPolicy.BuildMaskedPreviewFromLastFour("cdef"));
        Assert.Equal("cdef", ApiCredentialSecretPolicy.ExtractLastFour("cdef"));
        Assert.Equal("cdef", ApiCredentialSecretPolicy.ExtractLastFour("sk-abcdefghijcdef"));
    }
}
