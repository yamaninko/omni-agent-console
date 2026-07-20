using System;
using System.IO;
using OmniAgentConsole.Infrastructure.Runtime;
using Xunit;

namespace OmniAgentConsole.UnitTests;

public sealed class ExportCodeBlocksTests : IDisposable
{
    private readonly string root;

    public ExportCodeBlocksTests()
    {
        root = Path.Combine(Path.GetTempPath(), "export-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void FencelessMultiFileOutput_SplitsOnFilepathMarkers()
    {
        var content = string.Join('\n',
            "// filepath: src/index.ts",
            "console.log('boot');",
            "",
            "// filepath: src/app.ts",
            "export const app = 1;",
            "",
            "# filepath: .env.example",
            "JWT_SECRET=change-me",
            "",
            "<!-- filepath: README.md -->",
            "# JWT Auth API");

        var (written, skipped) = CodeBlockExporter.Export(content, root);

        Assert.Equal(4, written);
        Assert.Equal(0, skipped);
        Assert.Equal("console.log('boot');\n", File.ReadAllText(Path.Combine(root, "src", "index.ts")));
        Assert.Equal("export const app = 1;\n", File.ReadAllText(Path.Combine(root, "src", "app.ts")));
        Assert.Equal("JWT_SECRET=change-me\n", File.ReadAllText(Path.Combine(root, ".env.example")));
        Assert.Equal("# JWT Auth API\n", File.ReadAllText(Path.Combine(root, "README.md")));
    }

    [Fact]
    public void FencedBlocks_WithFirstLineFilepath_WriteSeparateFiles()
    {
        var content = string.Join('\n',
            "Some explanation.",
            "```typescript",
            "// filepath: src/main.ts",
            "const x = 1;",
            "```",
            "```json",
            "// filepath: package.json",
            "{ \"name\": \"demo\" }",
            "```");

        var (written, skipped) = CodeBlockExporter.Export(content, root);

        Assert.Equal(2, written);
        Assert.Equal(0, skipped);
        Assert.True(File.Exists(Path.Combine(root, "src", "main.ts")));
        Assert.True(File.Exists(Path.Combine(root, "package.json")));
    }

    [Fact]
    public void TraversalFilepathMarker_IsSkippedNotWritten()
    {
        var content = string.Join('\n',
            "// filepath: ../evil.ts",
            "malicious();",
            "",
            "// filepath: src/ok.ts",
            "fine();");

        var (written, skipped) = CodeBlockExporter.Export(content, root);

        Assert.Equal(1, written);
        Assert.Equal(1, skipped);
        Assert.True(File.Exists(Path.Combine(root, "src", "ok.ts")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(root)!, "evil.ts")));
    }

    [Fact]
    public void FencedBlocks_WithoutFilename_FallBackToOutputFolder()
    {
        var content = string.Join('\n',
            "Neither block names a file, so both fall back.",
            "```sql",
            "SELECT 1;",
            "```",
            "Some prose in between.",
            "```",
            "plain text block",
            "```");

        var (written, skipped) = CodeBlockExporter.Export(content, root);

        Assert.Equal(2, written);
        Assert.Equal(0, skipped);
        Assert.True(File.Exists(Path.Combine(root, "output", "output_1.sql")));
        Assert.True(File.Exists(Path.Combine(root, "output", "output_2.txt")));
    }

    [Fact]
    public void SingleFilepathMarker_WritesWholeContentToThatFile()
    {
        var content = string.Join('\n',
            "// filepath: src/main.ts",
            "const only = true;");

        var (written, skipped) = CodeBlockExporter.Export(content, root);

        Assert.Equal(1, written);
        Assert.Equal(0, skipped);
        Assert.True(File.Exists(Path.Combine(root, "main.ts")));
    }
}
