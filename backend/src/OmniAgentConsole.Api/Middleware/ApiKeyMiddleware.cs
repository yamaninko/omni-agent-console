using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace OmniAgentConsole.Api.Middleware;

public sealed class ApiKeyMiddleware
{
    private readonly RequestDelegate next;
    private readonly string? apiKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        this.next = next;
        this.apiKey = configuration["Console:ApiKey"];
        if (string.IsNullOrWhiteSpace(this.apiKey))
        {
            this.apiKey = Environment.GetEnvironmentVariable("CONSOLE_API_KEY");
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // If API key is not configured in settings or environment, allow anonymous access (local dev fallback)
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value;

        // Skip auth for health check
        if (path == "/health" || path == "/")
        {
            await next(context);
            return;
        }

        // 1. Check custom X-Api-Key header
        if (context.Request.Headers.TryGetValue("X-Api-Key", out var headerApiKey) && KeysMatch(headerApiKey.ToString()))
        {
            await next(context);
            return;
        }

        // 2. Check Authorization Bearer header
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var authHeaderStr = authHeader.ToString();
            if (authHeaderStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeaderStr.Substring("Bearer ".Length).Trim();
                if (KeysMatch(token))
                {
                    await next(context);
                    return;
                }
            }
        }

        // 3. Check access_token query parameter (SignalR WebSocket handshake fallback)
        if (context.Request.Query.TryGetValue("access_token", out var queryToken) && KeysMatch(queryToken.ToString()))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync("Unauthorized: Invalid or missing Console API Key.");
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
