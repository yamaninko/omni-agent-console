using System;
using System.Text.RegularExpressions;

namespace OmniAgentConsole.Application.Runtime;

public static class InputSanitizer
{
    // Well-known token shapes: NVIDIA, OpenAI, Anthropic, Google, GitHub, Slack,
    // JWTs, and generic Bearer headers. Order matters: more specific prefixes
    // (sk-ant-, sk-proj-) must come before the generic sk- pattern.
    private static readonly Regex TokenRegex = new(
        @"(nvapi-[A-Za-z0-9\-_]{64})" +
        @"|(sk-ant-[A-Za-z0-9\-_]{20,})" +
        @"|(sk-proj-[A-Za-z0-9\-_]{64,})" +
        @"|(sk-[A-Za-z0-9]{32,})" +
        @"|(AIza[0-9A-Za-z\-_]{35})" +
        @"|(ghp_[A-Za-z0-9]{36,})" +
        @"|(github_pat_[A-Za-z0-9_]{22,})" +
        @"|(xox[baprs]-[A-Za-z0-9\-]{10,})" +
        @"|(eyJ[A-Za-z0-9\-_]{10,}\.[A-Za-z0-9\-_]{10,}\.[A-Za-z0-9\-_]{5,})" +
        @"|(Bearer\s+[A-Za-z0-9\-_\.\+]{20,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Key=value / key: value assignments (connection strings, .env lines, JSON):
    // only the value is replaced, the key stays readable in logs. The \w* prefix
    // covers compound keys (JWT_SECRET, DB_PASSWORD); the optional quote covers
    // JSON keys ("api_key": "...").
    private static readonly Regex KeyValueRegex = new(
        @"(?<key>\b\w*(password|passwd|pwd|secret|api[_-]?key|access[_-]?token|client[_-]?secret)\b[""']?\s*[=:]\s*[""']?)(?<value>[^\s;,&""']{4,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var redacted = TokenRegex.Replace(value, "[REDACTED_SECRET]");
        return KeyValueRegex.Replace(redacted, "${key}[REDACTED_SECRET]");
    }
}
