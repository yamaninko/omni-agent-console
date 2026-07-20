using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Application.Providers;

public interface IModelProvider
{
    ProviderType ProviderType { get; }
    bool Supports(string model);
    Task<ModelResponse> CreateChatCompletionAsync(ModelRequest request, CancellationToken cancellationToken);
}
