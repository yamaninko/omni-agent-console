using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OmniAgentConsole.Application.Runtime;

namespace OmniAgentConsole.Api.Middleware;

/// <summary>
/// Accessors for the per-request shared-lab identity that
/// <see cref="ApiKeyMiddleware"/> stamps onto HttpContext.Items.
/// </summary>
public static class SharedLabHttp
{
    public const string SessionIdItem = "StudioSessionId";
    public const string IsAdminItem = "StudioIsAdmin";

    /// <summary>
    /// Session id comes from the X-Studio-Session-Id header, or from the
    /// session_id query parameter for transports that cannot set headers
    /// (SignalR WebSocket upgrade).
    /// </summary>
    public static string? ReadSessionId(HttpRequest request)
    {
        if (request.Headers.TryGetValue(SharedLabPolicy.SessionHeaderName, out var header)
            && !StringValues.IsNullOrEmpty(header))
        {
            return header.ToString();
        }

        if (request.Query.TryGetValue(SharedLabPolicy.SessionQueryName, out var query)
            && !StringValues.IsNullOrEmpty(query))
        {
            return query.ToString();
        }

        return null;
    }

    public static string? GetSessionId(HttpContext context) =>
        context.Items.TryGetValue(SessionIdItem, out var value) ? value as string : null;

    public static bool IsAdmin(HttpContext context) =>
        context.Items.TryGetValue(IsAdminItem, out var value) && value is true;
}
