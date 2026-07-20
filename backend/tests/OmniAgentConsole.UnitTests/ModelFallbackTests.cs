using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Runtime;
using Xunit;

namespace OmniAgentConsole.UnitTests;

public sealed class ModelFallbackTests
{
    [Fact]
    public void Chain_IsPrimaryPlusFallbacksInOrder()
    {
        var chain = AgentOrchestratorService.BuildModelChain(
            "qwen/qwen3.5-122b-a10b",
            "deepseek-ai/deepseek-v4-flash, openai/gpt-oss-120b");

        Assert.Equal(
            ["qwen/qwen3.5-122b-a10b", "deepseek-ai/deepseek-v4-flash", "openai/gpt-oss-120b"],
            chain);
    }

    [Fact]
    public void Chain_DeduplicatesCaseInsensitively()
    {
        var chain = AgentOrchestratorService.BuildModelChain(
            "meta/llama-3.1-8b-instruct",
            "META/LLAMA-3.1-8B-INSTRUCT, stepfun-ai/step-3.7-flash,, stepfun-ai/step-3.7-flash");

        Assert.Equal(["meta/llama-3.1-8b-instruct", "stepfun-ai/step-3.7-flash"], chain);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Chain_WithoutFallbacks_IsJustThePrimary(string? fallbacks)
    {
        var chain = AgentOrchestratorService.BuildModelChain("meta/llama-3.1-8b-instruct", fallbacks);
        Assert.Equal(["meta/llama-3.1-8b-instruct"], chain);
    }

    [Theory]
    [InlineData(ProviderErrorCode.Timeout)]
    [InlineData(ProviderErrorCode.RateLimit)]
    [InlineData(ProviderErrorCode.ProviderUnavailable)]
    [InlineData(ProviderErrorCode.InvalidModel)]
    [InlineData(ProviderErrorCode.InvalidRequest)]
    [InlineData(ProviderErrorCode.UnknownError)]
    public void TransientOrModelSpecificErrors_TriggerFallback(ProviderErrorCode code)
    {
        Assert.True(AgentOrchestratorService.ShouldFallbackToNextModel(code));
    }

    [Fact]
    public void AuthFailure_DoesNotTriggerFallback()
    {
        // The whole chain uses the same key; a 401 would fail on every model.
        Assert.False(AgentOrchestratorService.ShouldFallbackToNextModel(ProviderErrorCode.Unauthorized));
    }
}
