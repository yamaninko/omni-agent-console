using OmniAgentConsole.Application.Workspace;

namespace OmniAgentConsole.UnitTests;

public sealed class WorkspaceFolderPolicyTests
{
    [Theory]
    [InlineData("fastapi", true)]
    [InlineData("my-api", true)]
    [InlineData("My_App.v2", true)]
    [InlineData("a", true)]
    [InlineData("", false)]
    [InlineData("../etc", false)]
    [InlineData("foo/bar", false)]
    [InlineData(".hidden", false)]
    [InlineData("-leading", false)]
    [InlineData("has space", false)]
    public void TryNormalizeProjectName_validates(string raw, bool ok)
    {
        var result = WorkspaceFolderPolicy.TryNormalizeProjectName(raw, out var name, out var error);
        Assert.Equal(ok, result);
        if (ok)
        {
            Assert.Equal(raw.Trim(), name);
            Assert.Empty(error);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    [Fact]
    public void TryNormalizeImportRelativePath_strips_matching_root_folder()
    {
        Assert.True(WorkspaceFolderPolicy.TryNormalizeImportRelativePath(
            "my-api/src/main.py", "my-api", out var rel, out _));
        Assert.Equal("src/main.py", rel);
    }

    [Fact]
    public void TryNormalizeImportRelativePath_keeps_path_when_no_root_prefix()
    {
        Assert.True(WorkspaceFolderPolicy.TryNormalizeImportRelativePath(
            "src/main.py", "my-api", out var rel, out _));
        Assert.Equal("src/main.py", rel);
    }

    [Fact]
    public void TryNormalizeImportRelativePath_skips_node_modules()
    {
        Assert.False(WorkspaceFolderPolicy.TryNormalizeImportRelativePath(
            "my-api/node_modules/x/index.js", "my-api", out _, out var reason));
        Assert.Contains("node_modules", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryNormalizeImportRelativePath_rejects_traversal()
    {
        Assert.False(WorkspaceFolderPolicy.TryNormalizeImportRelativePath(
            "my-api/../secret.txt", "my-api", out _, out _));
        Assert.False(WorkspaceFolderPolicy.TryNormalizeImportRelativePath(
            "/etc/passwd", "my-api", out _, out _));
    }

    [Fact]
    public void ShouldRejectFileSize_over_limit()
    {
        Assert.True(WorkspaceFolderPolicy.ShouldRejectFileSize(
            WorkspaceFolderPolicy.MaxBytesPerFile + 1, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.False(WorkspaceFolderPolicy.ShouldRejectFileSize(100, out _));
    }

    [Fact]
    public void CopyDirectoryIntoWorkspace_copies_files_and_skips_node_modules()
    {
        var root = Path.Combine(Path.GetTempPath(), "oa-ws-copy-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(Path.GetTempPath(), "oa-src-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(source, "src"));
            Directory.CreateDirectory(Path.Combine(source, "node_modules", "pkg"));
            File.WriteAllText(Path.Combine(source, "src", "main.py"), "print(1)");
            File.WriteAllText(Path.Combine(source, "node_modules", "pkg", "x.js"), "/* skip */");
            File.WriteAllText(Path.Combine(source, "README.md"), "# hi");

            var dest = Path.Combine(root, "copied-app");
            var (files, bytes, skippedDirs) = WorkspaceFolderPolicy.CopyDirectoryIntoWorkspace(
                root, "copied-app", source, dest);

            Assert.Equal(2, files);
            Assert.True(bytes > 0);
            Assert.True(skippedDirs >= 1);
            Assert.True(File.Exists(Path.Combine(dest, "src", "main.py")));
            Assert.True(File.Exists(Path.Combine(dest, "README.md")));
            Assert.False(Directory.Exists(Path.Combine(dest, "node_modules")));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { /* */ }
            try { if (Directory.Exists(source)) Directory.Delete(source, true); } catch { /* */ }
        }
    }
}
