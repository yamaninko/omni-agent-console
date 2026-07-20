namespace OmniAgentConsole.Application.Providers;

public interface ITokenUsageExtractor
{
    TokenUsage Extract(ModelResponse response);
    TokenUsage Estimate(ModelRequest request, ModelResponse response);
}
