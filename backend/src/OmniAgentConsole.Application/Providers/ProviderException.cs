using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Application.Providers;

public sealed class ProviderException : Exception
{
    public ProviderException(ProviderErrorCode errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public ProviderErrorCode ErrorCode { get; }
}
