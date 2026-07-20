using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using OmniAgentConsole.Application.Runtime;

namespace OmniAgentConsole.Api.Middleware;

public sealed class ApiKeyMiddleware
{
    private readonly RequestDelegate next;
    private readonly string? apiKey;
    private readonly bool sharedLab;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        this.next = next;
        this.apiKey = configuration["Console:ApiKey"];
        if (string.IsNullOrWhiteSpace(this.apiKey))
        {
            this.apiKey = Environment.GetEnvironmentVariable("CONSOLE_API_KEY");
        }

        this.sharedLab = configuration.GetValue<bool>("SharedLab:Enabled");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip auth for health check
        if (path == "/health" || path == "/")
        {
            await next(context);
            return;
        }

        var isAdmin = HasValidKey(context);
        context.Items[SharedLabHttp.IsAdminItem] = isAdmin;

        if (!sharedLab)
        {
            // Laptop profile (default): a configured key gates everything;
            // no key means anonymous local-dev access. Today's behavior.
            if (string.IsNullOrWhiteSpace(apiKey) || isAdmin)
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("Unauthorized: Invalid or missing Console API Key.");
            return;
        }

        // Shared-lab profile: the console key is the *instructor/admin* credential
        // (startup fail-fast guarantees it exists); students identify with a
        // session id instead and are confined to session-scoped resources.
        if (isAdmin)
        {
            await next(context);
            return;
        }

        var sessionId = SharedLabHttp.ReadSessionId(context.Request);
        if (!SharedLabPolicy.IsValidSessionId(sessionId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync(
                $"Shared-lab mode requires a valid {SharedLabPolicy.SessionHeaderName} header (8-64 chars, [A-Za-z0-9-_]).");
            return;
        }

        context.Items[SharedLabHttp.SessionIdItem] = sessionId;

        if (SharedLabPolicy.IsAdminGated(context.Request.Method, path))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("This operation is locked in shared-lab mode; ask the instructor.");
            return;
        }

        await next(context);
    }

    private bool HasValidKey(HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        // 1. Custom X-Api-Key header
        if (context.Request.Headers.TryGetValue("X-Api-Key", out var headerApiKey) && KeysMatch(headerApiKey.ToString()))
        {
            return true;
        }

        // 2. Authorization Bearer header
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var authHeaderStr = authHeader.ToString();
            if (authHeaderStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                && KeysMatch(authHeaderStr.Substring("Bearer ".Length).Trim()))
            {
                return true;
            }
        }

        // 3. access_token query parameter (SignalR WebSocket handshake fallback)
        return context.Request.Query.TryGetValue("access_token", out var queryToken) && KeysMatch(queryToken.ToString());
    }

    // Hash both sides before FixedTimeEquals so neither content nor length leaks via timing.
    private bool KeysMatch(string provided)
    {
        if (apiKey is null || string.IsNullOrEmpty(provided))
        {
            return false;
        }

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }
}
