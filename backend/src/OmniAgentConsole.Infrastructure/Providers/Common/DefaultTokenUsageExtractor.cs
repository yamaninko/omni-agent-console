using OmniAgentConsole.Application.Providers;
using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Infrastructure.Providers.Common;

public sealed class DefaultTokenUsageExtractor : ITokenUsageExtractor
{
    public TokenUsage Extract(ModelResponse response)
    {
        if (response.InputTokens.HasValue && response.OutputTokens.HasValue && response.TotalTokens.HasValue)
        {
            return new TokenUsage(response.InputTokens.Value, response.OutputTokens.Value, response.TotalTokens.Value);
        }

        return Estimate(ModelRequestForEstimate.Empty, response);
    }

    public TokenUsage Estimate(ModelRequest request, ModelResponse response)
    {
        var input = request.Messages.Sum(message => EstimateTokens(message.Content));
        var output = EstimateTokens(response.Content);

        return new TokenUsage(input, output, input + output);
    }

    private static int EstimateTokens(string text)
    {
        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0d));
    }

    private static class ModelRequestForEstimate
    {
        public static readonly ModelRequest Empty = new(
            ProviderType.OmniAgent,
            string.Empty,
            Array.Empty<ChatMessage>(),
            0,
            0,
            0);
    }
}
