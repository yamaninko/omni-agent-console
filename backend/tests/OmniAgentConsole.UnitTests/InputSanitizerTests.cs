using OmniAgentConsole.Application.Runtime;
using Xunit;

namespace OmniAgentConsole.UnitTests;

public sealed class InputSanitizerTests
{
    [Theory]
    [InlineData("Hello world, no secrets here.", "Hello world, no secrets here.")]
    [InlineData("My key is nvapi-1234567890123456789012345678901234567890123456789012345678901234", "My key is [REDACTED_SECRET]")]
    [InlineData("My key is sk-123456789012345678901234567890123456", "My key is [REDACTED_SECRET]")]
    [InlineData("Authorization: Bearer my-super-secret-auth-token-12345", "Authorization: [REDACTED_SECRET]")]
    [InlineData("Empty string is fine.", "Empty string is fine.")]
    [InlineData("Anthropic: sk-ant-api03-abcdefghijklmnopqrstuvwx", "Anthropic: [REDACTED_SECRET]")]
    [InlineData("Google key AIzaSyA1234567890abcdefghijklmnopqrstuv", "Google key [REDACTED_SECRET]")]
    [InlineData("GitHub ghp_abcdefghijklmnopqrstuvwxyz0123456789", "GitHub [REDACTED_SECRET]")]
    [InlineData("PAT github_pat_11ABCDEFG0123456789abcdef", "PAT [REDACTED_SECRET]")]
    [InlineData("token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4", "token [REDACTED_SECRET]")]
    [InlineData("Host=db;Username=app;Password=Sup3rS3cret;", "Host=db;Username=app;Password=[REDACTED_SECRET];")]
    [InlineData("JWT_SECRET=change-me-in-prod", "JWT_SECRET=[REDACTED_SECRET]")]
    [InlineData("\"api_key\": \"abc123def456\"", "\"api_key\": \"[REDACTED_SECRET]\"")]
    public void Redact_ShouldMaskSecretsCorrectly(string input, string expected)
    {
        var result = InputSanitizer.Redact(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Use bcrypt to hash the password before storing it.")]
    [InlineData("Read the secret from an environment variable.")]
    [InlineData("The api key field is masked in responses.")]
    public void Redact_ProseAboutSecrets_IsLeftUntouched(string input)
    {
        Assert.Equal(input, InputSanitizer.Redact(input));
    }

    [Fact]
    public void Redact_NullOrWhitespace_ShouldReturnEmptyString()
    {
        Assert.Equal(string.Empty, InputSanitizer.Redact(null));
        Assert.Equal(string.Empty, InputSanitizer.Redact("   "));
    }
}
