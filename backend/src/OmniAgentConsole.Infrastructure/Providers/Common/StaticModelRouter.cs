using Microsoft.Extensions.Options;
using OmniAgentConsole.Application.Configuration;
using OmniAgentConsole.Application.Providers;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Infrastructure.Providers.Common;

public sealed class StaticModelRouter : IModelRouter
{
    private readonly OmniAgentProviderOptions options;

    public StaticModelRouter(IOptions<OmniAgentProviderOptions> options)
    {
        this.options = options.Value;
    }

    public ModelRoute Resolve(AgentDefinition agentDefinition, string? requestedModel = null)
    {
        var model = requestedModel;

        if (string.IsNullOrWhiteSpace(model))
        {
            model = string.IsNullOrWhiteSpace(agentDefinition.DefaultModel)
                ? options.DefaultModel
                : agentDefinition.DefaultModel;
        }

        var provider = agentDefinition.Provider;
        if (agentDefinition.ApiCredential != null)
        {
            provider = ParseProvider(agentDefinition.ApiCredential.Provider, provider);
        }

        return new ModelRoute(provider, model);
    }

    private static ProviderType ParseProvider(string providerStr, ProviderType fallback)
    {
        if (string.IsNullOrWhiteSpace(providerStr)) return fallback;
        if (Enum.TryParse<ProviderType>(providerStr, true, out var result))
        {
            return result;
        }
        if (providerStr.Equals("openai", StringComparison.OrdinalIgnoreCase)) return ProviderType.OpenAi;
        if (providerStr.Equals("azure", StringComparison.OrdinalIgnoreCase)) return ProviderType.AzureOpenAi;
        if (providerStr.Equals("gemini", StringComparison.OrdinalIgnoreCase)) return ProviderType.OpenAi;
        return fallback;
    }
}
