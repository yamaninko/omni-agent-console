using OmniAgentConsole.Application.Settings;

namespace OmniAgentConsole.Application.Providers;

public interface IProviderHealthCheck
{
    Task<ProviderHealthStatusDto> CheckOmniAgentAsync(CancellationToken cancellationToken);
}
