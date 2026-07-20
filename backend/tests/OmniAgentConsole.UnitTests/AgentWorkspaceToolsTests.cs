using System;
using System.IO;
using System.Text.Json;
using OmniAgentConsole.Application.Runtime;
using Xunit;

namespace OmniAgentConsole.UnitTests;

public sealed class AgentWorkspaceToolsTests : IDisposable
{
    private readonly string root;
    private readonly AgentWorkspaceTools tools;

    public AgentWorkspaceToolsTests()
    {
        root = Path.Combine(Path.GetTempPath(), "tool-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        tools = new AgentWorkspaceTools(root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch { }
    }

    private static string Args(object value) => JsonSerializer.Serialize(value);

    [Fact]
    public void WriteFile_CreatesFileWithNestedDirectories()
    {
        var result = tools.Execute("write_file", Args(new { path = "src/app/main.py", content = "print('hi')" }));

        Assert.True(result.Success);
        Assert.Equal("print('hi')", File.ReadAllText(Path.Combine(root, "src", "app", "main.py")));
        Assert.Contains("src/app/main.py", tools.WrittenFiles);
    }

    [Fact]
    public void WriteFile_TraversalPath_IsRejected()
    {
        var result = tools.Execute("write_file", Args(new { path = "../evil.txt", content = "x" }));

        Assert.False(result.Success);
        Assert.Contains("outside the workspace", result.Output);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(root)!, "evil.txt")));
        Assert.Empty(tools.WrittenFiles);
    }

    [Fact]
    public void WriteFile_OverwritingSameFile_CountsOnce()
    {
        tools.Execute("write_file", Args(new { path = "a.txt", content = "v1" }));
        tools.Execute("write_file", Args(new { path = "a.txt", content = "v2" }));

        Assert.Single(tools.WrittenFiles);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(root, "a.txt")));
    }

    [Fact]
    public void WriteFile_ContentOverLimit_IsRejected()
    {
        var huge = new string('x', AgentWorkspaceTools.MaxFileChars + 1);
        var result = tools.Execute("write_file", Args(new { path = "big.txt", content = huge }));

        Assert.False(result.Success);
        Assert.Contains("character limit", result.Output);
    }

    [Fact]
    public void WriteFile_MissingArguments_FailsWithMessage()
    {
        var result = tools.Execute("write_file", Args(new { path = "x.txt" }));

        Assert.False(result.Success);
        Assert.Contains("content", result.Output);
    }

    [Fact]
    public void ReadFile_ReturnsWrittenContent()
    {
        tools.Execute("write_file", Args(new { path = "notes.md", content = "# hello" }));

        var result = tools.Execute("read_file", Args(new { path = "notes.md" }));

        Assert.True(result.Success);
        Assert.Equal("# hello", result.Output);
    }

    [Fact]
    public void ReadFile_MissingFile_Fails()
    {
        var result = tools.Execute("read_file", Args(new { path = "ghost.txt" }));

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.Output);
    }

    [Fact]
    public void ListFiles_ReturnsRelativePathsRecursively()
    {
        tools.Execute("write_file", Args(new { path = "a.txt", content = "1" }));
        tools.Execute("write_file", Args(new { path = "src/b.txt", content = "2" }));

        var result = tools.Execute("list_files", "{}");

        Assert.True(result.Success);
        Assert.Contains("a.txt", result.Output);
        Assert.Contains("src/b.txt", result.Output);
    }

    [Fact]
    public void ListFiles_EmptyWorkspace_SaysEmpty()
    {
        var result = tools.Execute("list_files", "{}");

        Assert.True(result.Success);
        Assert.Equal("(empty)", result.Output);
    }

    [Fact]
    public void UnknownTool_FailsWithAvailableToolList()
    {
        var result = tools.Execute("delete_everything", "{}");

        Assert.False(result.Success);
        Assert.Contains("write_file, read_file, list_files", result.Output);
    }

    [Fact]
    public void InvalidArgumentsJson_FailsGracefully()
    {
        var result = tools.Execute("write_file", "not-json{");

        Assert.False(result.Success);
        Assert.Contains("valid JSON", result.Output);
    }

    [Fact]
    public void FileBudget_IsEnforced()
    {
        for (var i = 0; i < AgentWorkspaceTools.MaxFilesPerTask; i++)
        {
            var ok = tools.Execute("write_file", Args(new { path = $"f{i}.txt", content = "x" }));
            Assert.True(ok.Success);
        }

        var overBudget = tools.Execute("write_file", Args(new { path = "one-too-many.txt", content = "x" }));

        Assert.False(overBudget.Success);
        Assert.Contains("budget", overBudget.Output);
    }

    [Fact]
    public void Definitions_ContainAllThreeToolsWithValidSchemas()
    {
        Assert.Equal(3, AgentWorkspaceTools.Definitions.Count);
        foreach (var definition in AgentWorkspaceTools.Definitions)
        {
            using var schema = JsonDocument.Parse(definition.ParametersJsonSchema);
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        }
    }
}
