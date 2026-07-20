namespace OmniAgentConsole.Domain.Enums;

public enum ProviderErrorCode
{
    None = 0,
    RateLimit = 1,
    Unauthorized = 2,
    Timeout = 3,
    ProviderUnavailable = 4,
    InvalidModel = 5,
    InvalidRequest = 6,
    UnknownError = 7
}
