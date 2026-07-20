using OmniAgentConsole.Application.Runtime;

namespace OmniAgentConsole.UnitTests;

public sealed class ReviewerFixLoopPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Looks fine.")]
    public void ShouldRunFixLoop_False_ForEmptyOrTooShort(string? content)
    {
        Assert.False(ReviewerFixLoopPolicy.ShouldRunFixLoop(content));
    }

    [Fact]
    public void ShouldRunFixLoop_False_ForExplicitCleanBill()
    {
        var review =
            """
            Overall the project looks good. No findings of concern.
            The structure follows REST conventions and security looks fine.
            LGTM — approved for merge with nothing to fix.
            """;
        Assert.False(ReviewerFixLoopPolicy.ShouldRunFixLoop(review));
    }

    [Fact]
    public void ShouldRunFixLoop_True_ForSeverityFindings()
    {
        var review =
            """
            Review summary with prioritized findings:
            1. CRITICAL: hardcoded JWT secret in config.go
            2. HIGH: missing input validation on /users
            3. Medium severity: no health check endpoint
            Concrete fixes: move secret to env, add validation middleware, add /health.
            """;
        Assert.True(ReviewerFixLoopPolicy.ShouldRunFixLoop(review));
    }

    [Fact]
    public void ShouldRunFixLoop_True_ForTurkishFindingMarkers()
    {
        var review =
            """
            İnceleme sonucu birkaç bulgu var:
            - Güvenlik: API key kaynak kodda gömülü
            - Eksik health check endpoint'i
            - Hata zarfı tutarsız; 500'lerde stack trace sızıyor
            Lütfen bu maddeleri düzeltin.
            """;
        Assert.True(ReviewerFixLoopPolicy.ShouldRunFixLoop(review));
    }

    [Fact]
    public void ShouldRunFixLoop_True_ForLongBulletListWithoutKeywords()
    {
        var review =
            """
            Review notes for the generated service:
            - Add pagination to the list endpoint
            - Document environment variables in README
            - Align error envelope across handlers
            - Extract repository interface for testability
            These are concrete follow-ups for the coder pass.
            """;
        Assert.True(ReviewerFixLoopPolicy.ShouldRunFixLoop(review));
    }

    [Fact]
    public void BuildFixLoopObjective_IncludesFindingsAndSinglePassRules()
    {
        var objective = ReviewerFixLoopPolicy.BuildFixLoopObjective(
            "CRITICAL: SQL injection in user handler\nHIGH: missing auth middleware");

        Assert.Contains("FIX LOOP", objective, StringComparison.Ordinal);
        Assert.Contains("single pass", objective, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SQL injection", objective, StringComparison.Ordinal);
        Assert.Contains("write_file", objective, StringComparison.Ordinal);
    }
}
