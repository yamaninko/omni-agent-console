using System.Text.Json;
using OmniAgentConsole.Application.Providers;
using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Infrastructure.Runtime;

/// <summary>Small serialization helpers shared by the run components.</summary>
internal static class RunTelemetry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string BuildErrorPayload(Exception exception)
    {
        return JsonSerializer.Serialize(new
        {
            error = exception.Message,
            errorCode = exception is ProviderException providerException
                ? providerException.ErrorCode.ToString()
                : ProviderErrorCode.UnknownError.ToString()
        }, JsonOptions);
    }
}
