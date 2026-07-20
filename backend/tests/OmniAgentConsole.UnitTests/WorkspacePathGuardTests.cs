using System;
using System.IO;
using OmniAgentConsole.Application.Runtime;
using Xunit;

namespace OmniAgentConsole.UnitTests;

public sealed class WorkspacePathGuardTests : IDisposable
{
    private readonly string root;
    private readonly string outside;

    public WorkspacePathGuardTests()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "wpg-tests-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(baseDir, "workspace");
        outside = Path.Combine(baseDir, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void RelativePath_ResolvesUnderRoot()
    {
        Assert.True(WorkspacePathGuard.TryResolve(root, "proje/main.py", out var fullPath));
        Assert.Equal(Path.Combine(root, "proje", "main.py"), fullPath);
    }

    [Fact]
    public void AbsolutePathInsideRoot_IsAllowed()
    {
        var inside = Path.Combine(root, "proje");
        Assert.True(WorkspacePathGuard.TryResolve(root, inside, out var fullPath));
        Assert.Equal(inside, fullPath);
    }

    [Fact]
    public void RootItself_IsAllowed()
    {
        Assert.True(WorkspacePathGuard.TryResolve(root, ".", out var fullPath));
        Assert.Equal(WorkspacePathGuard.NormalizeRoot(root), fullPath);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("a/../../escape.txt")]
    [InlineData("..")]
    [InlineData("a/b/../../../etc/passwd")]
    public void ParentTraversal_IsRejected(string path)
    {
        Assert.False(WorkspacePathGuard.TryResolve(root, path, out _));
    }

    [Theory]
    [InlineData("..\\escape.txt")]
    [InlineData("a\\..\\..\\escape.txt")]
    public void BackslashTraversal_IsRejected(string path)
    {
        Assert.False(WorkspacePathGuard.TryResolve(root, path, out _));
    }

    [Fact]
    public void AbsolutePathOutsideRoot_IsRejected()
    {
        Assert.False(WorkspacePathGuard.TryResolve(root, Path.Combine(outside, "f.txt"), out _));
        Assert.False(WorkspacePathGuard.TryResolve(root, "/etc/passwd", out _));
    }

    [Fact]
    public void SiblingWithRootNamePrefix_IsRejected()
    {
        var sibling = root + "-evil";
        Directory.CreateDirectory(sibling);
        Assert.False(WorkspacePathGuard.TryResolve(root, Path.Combine(sibling, "f.txt"), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingPath_IsRejected(string? path)
    {
        Assert.False(WorkspacePathGuard.TryResolve(root, path, out _));
    }

    [Fact]
    public void SymlinkedDirectoryInsideRoot_IsRejected()
    {
        var link = Path.Combine(root, "link");
        Directory.CreateSymbolicLink(link, outside);

        Assert.False(WorkspacePathGuard.TryResolve(root, "link/f.txt", out _));
    }

    [Fact]
    public void DanglingSymlinkInsideRoot_IsRejected()
    {
        var link = Path.Combine(root, "dangling.txt");
        File.CreateSymbolicLink(link, Path.Combine(outside, "missing.txt"));

        Assert.False(WorkspacePathGuard.TryResolve(root, "dangling.txt", out _));
    }

    [Fact]
    public void RegularNestedPath_WithExistingDirectories_IsAllowed()
    {
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        Assert.True(WorkspacePathGuard.TryResolve(root, "a/b/c.txt", out var fullPath));
        Assert.Equal(Path.Combine(root, "a", "b", "c.txt"), fullPath);
    }
}
