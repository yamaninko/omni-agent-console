using System;
using System.Collections.Generic;
using OmniAgentConsole.Application.Providers;
using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Providers.Common;
using Xunit;

namespace OmniAgentConsole.UnitTests;

public sealed class TokenUsageExtractorTests
{
    private readonly DefaultTokenUsageExtractor extractor = new();

    [Fact]
    public void Extract_WithTokenValues_ShouldReturnExactValues()
    {
        var response = new ModelResponse(ProviderType.OmniAgent, "model", "Content", null, 10, 20, 30, null);
        var usage = extractor.Extract(response);

        Assert.Equal(10, usage.InputTokens);
        Assert.Equal(20, usage.OutputTokens);
        Assert.Equal(30, usage.TotalTokens);
    }

    [Fact]
    public void Extract_WithoutTokenValues_ShouldEstimate()
    {
        var response = new ModelResponse(ProviderType.OmniAgent, "model", "Hello, world!", null, null, null, null, null);
        var usage = extractor.Extract(response);

        // "Hello, world!" is 13 chars -> ceiling(13 / 4) = 4 tokens
        Assert.Equal(0, usage.InputTokens);
        Assert.Equal(4, usage.OutputTokens);
        Assert.Equal(4, usage.TotalTokens);
    }

    [Fact]
    public void Estimate_ShouldCalculateCharacterBasedTokens()
    {
        var messages = new List<ChatMessage>
        {
            new("system", "Test system prompt"), // 18 chars -> 5 tokens
            new("user", "Hello") // 5 chars -> 2 tokens
        };
        var request = new ModelRequest(ProviderType.OmniAgent, "model", messages, 0.5m, 100, 30);
        var response = new ModelResponse(ProviderType.OmniAgent, "model", "Hi", null, null, null, null, null); // 2 chars -> 1 token

        var usage = extractor.Estimate(request, response);

        Assert.Equal(7, usage.InputTokens);
        Assert.Equal(1, usage.OutputTokens);
        Assert.Equal(8, usage.TotalTokens);
    }
}
