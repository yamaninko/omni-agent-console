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
        Assert.Contains("docker compose -p omni-fastapi up -d --build --force-recreate --remove-orphans", cmd);
    }

    [Fact]
    public void ClassifyProjectKind_DetectsApiFromFastApiLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "omni-kind-" + Guid.NewGuid().ToString("N"));
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "main.py"), "from fastapi import FastAPI\napp = FastAPI()\n");
        File.WriteAllText(Path.Combine(root, "requirements.txt"), "fastapi>=0.110\nuvicorn\n");
        try
        {
            Assert.Equal("api", WorkspaceProjectDetector.ClassifyProjectKind(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ClassifyProjectKind_DetectsWebFromIndexHtml()
    {
        var root = Path.Combine(Path.GetTempPath(), "omni-web-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.html"), "<html></html>");
        try
        {
            Assert.Equal("web", WorkspaceProjectDetector.ClassifyProjectKind(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("http://localhost:18786/health", true)]
    [InlineData("http://127.0.0.1:18000/x", true)]
    [InlineData("http://evil.com:18786/x", false)]
    [InlineData("http://localhost:80/x", false)]
    public void IsAllowedProxyTarget_OnlyLocalRunnerPorts(string url, bool expected)
    {
        Assert.Equal(
            expected,
            WorkspaceProjectDetector.IsAllowedProxyTarget(new Uri(url), 18000, 1000));
    }

    [Fact]
    public void TryLoadRoutesFromOpenApi_ReadsPathsAndExamples()
    {
        var root = Path.Combine(Path.GetTempPath(), "omni-oa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "openapi.json"),
            """
            {
              "openapi": "3.0.0",
              "paths": {
                "/notes": {
                  "get": { "summary": "List notes" },
                  "post": {
                    "summary": "Create note",
                    "requestBody": {
                      "content": {
                        "application/json": {
                          "example": { "title": "t", "body": "b" }
                        }
                      }
                    }
                  }
                }
              }
            }
            """);

        try
        {
            var routes = WorkspaceProjectDetector.TryLoadRoutesFromOpenApi(root);
            Assert.Contains(routes, r => r.Method == "GET" && r.Path == "/notes");
            var post = Assert.Single(routes, r => r.Method == "POST" && r.Path == "/notes");
            Assert.Contains("title", post.ExampleBody);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
