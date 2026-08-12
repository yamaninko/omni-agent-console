using System;
using OmniAgentConsole.Application.Runtime;
using Xunit;

namespace OmniAgentConsole.UnitTests;

public sealed class SharedLabPolicyTests
{
    [Theory]
    [InlineData("3f2b8c1a-9d4e-4f6a-b7c8-1234567890ab", true)] // browser crypto.randomUUID()
    [InlineData("student_42-laptop", true)]
    [InlineData("abcdefgh", true)]  // minimum length
    [InlineData("short", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("has spaces here", false)]
    [InlineData("dots.are.paths", false)]
    [InlineData("slash/attack", false)]
    [InlineData("back\\slash00", false)]
    public void IsValidSessionId_EnforcesPathSafeCharset(string? sessionId, bool expected)
    {
        Assert.Equal(expected, SharedLabPolicy.IsValidSessionId(sessionId));
    }

    [Fact]
    public void IsValidSessionId_RejectsOverlongIds()
    {
        Assert.False(SharedLabPolicy.IsValidSessionId(new string('a', 65)));
        Assert.True(SharedLabPolicy.IsValidSessionId(new string('a', 64)));
    }

    [Theory]
    [InlineData("/workspace/foo", "/workspace/sessions/sid-12345678/foo")]
    [InlineData("foo", "/workspace/sessions/sid-12345678/foo")]
    [InlineData("/workspace/foo/bar", "/workspace/sessions/sid-12345678/foo/bar")]
    [InlineData("/workspace", "/workspace/sessions/sid-12345678")]
    [InlineData("/workspace/", "/workspace/sessions/sid-12345678")]
    public void MapWorkspacePath_ForcesSessionSubtree(string requested, string expected)
    {
        Assert.Equal(expected, SharedLabPolicy.MapWorkspacePath("/workspace", "sid-12345678", requested));
    }

    [Fact]
    public void MapWorkspacePath_IsIdempotent()
    {
        var once = SharedLabPolicy.MapWorkspacePath("/workspace", "sid-12345678", "/workspace/demo");
        var twice = SharedLabPolicy.MapWorkspacePath("/workspace", "sid-12345678", once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void MapWorkspacePath_DoesNotAcceptForeignSessionPrefix()
    {
        // A path aimed at another session's subtree gets re-prefixed under the
        // caller's own session instead of passing through.
        var mapped = SharedLabPolicy.MapWorkspacePath("/workspace", "sid-victim99", "/workspace/sessions/sid-attacker/x");
        Assert.StartsWith("/workspace/sessions/sid-victim99/", mapped);
    }

    [Theory]
    [InlineData("GET", "/api/credentials", false)]
    [InlineData("POST", "/api/credentials", true)]
    [InlineData("PUT", "/api/agents/123", true)]
    [InlineData("DELETE", "/api/skills/456", true)]
    [InlineData("POST", "/api/skills/suggest", false)] // Studio auto-suggest stays open
    [InlineData("POST", "/api/settings", true)]
    [InlineData("POST", "/api/providers", true)]
    [InlineData("POST", "/api/agent-groups", true)]
    [InlineData("PUT", "/api/agent-groups/abc/members", true)]
    [InlineData("POST", "/api/panels", false)]
    [InlineData("POST", "/api/panels/abc/start", false)]
    [InlineData("POST", "/api/tasks", false)]
    [InlineData("DELETE", "/api/workspace", false)]
    [InlineData("GET", "/api/skills", false)]
    public void IsAdminGated_LocksConfigurationWritesOnly(string method, string path, bool expected)
    {
        Assert.Equal(expected, SharedLabPolicy.IsAdminGated(method, path));
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "")]
    [InlineData(true, "instructor-key")]
    public void ValidateStartup_AllowsValidCombinations(bool sharedLab, string? apiKey)
    {
        SharedLabPolicy.ValidateStartup(sharedLab, apiKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateStartup_FailsFastForSharedLabWithoutAdminKey(string? apiKey)
    {
        Assert.Throws<InvalidOperationException>(() => SharedLabPolicy.ValidateStartup(true, apiKey));
    }
}
