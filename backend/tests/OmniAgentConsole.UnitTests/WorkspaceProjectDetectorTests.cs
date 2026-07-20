using OmniAgentConsole.Application.Runtime;

namespace OmniAgentConsole.UnitTests;

public sealed class WorkspaceProjectDetectorTests
{
    [Fact]
    public void Detect_FindsComposeInParentDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "omni-ws-detect-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "demo");
        var nested = Path.Combine(project, "app");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(project, "Dockerfile"), "FROM alpine\nEXPOSE 8080\n");
        File.WriteAllText(Path.Combine(project, "docker-compose.yml"), "services:\n  app:\n    build: .\n");
        File.WriteAllText(Path.Combine(nested, "main.py"), "print('hi')\n");

        try
        {
            var layout = WorkspaceProjectDetector.Detect(root, "demo/app/main.py");
            Assert.NotNull(layout);
            Assert.True(layout!.HasCompose);
            Assert.True(layout.HasDockerfile);
            Assert.Equal("demo", layout.RelativeRoot);
            Assert.Equal(8080, WorkspaceProjectDetector.GuessContainerPort(layout.FullRoot));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SuggestHostPort_IsStableWithinRange()
    {
        var a = WorkspaceProjectDetector.SuggestHostPort("fastapi", 18000, 1000);
        var b = WorkspaceProjectDetector.SuggestHostPort("fastapi", 18000, 1000);
        Assert.Equal(a, b);
        Assert.InRange(a, 18000, 18999);
    }

    [Fact]
    public void ComposeProjectName_IsPathSafe()
    {
        var name = WorkspaceProjectDetector.ComposeProjectName("my project/v1");
        Assert.StartsWith("omni-", name);
        Assert.DoesNotContain(" ", name);
        Assert.DoesNotContain("/", name);
    }

    [Fact]
    public void BuildUpCommand_UsesHostPortEnvForCompose()
    {
        var layout = new ProjectLayout("/tmp/x", "fastapi", true, true, "docker-compose.yml");
        var cmd = WorkspaceProjectDetector.BuildUpCommand(layout, "omni-fastapi", 18321);
        Assert.Contains("HOST_PORT=18321", cmd);
        Assert.Contains("docker compose -p omni-fastapi up -d --build", cmd);
    }
}
