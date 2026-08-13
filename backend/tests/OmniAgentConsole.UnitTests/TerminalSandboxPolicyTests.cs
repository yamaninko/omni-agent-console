using OmniAgentConsole.Application.Runtime;

namespace OmniAgentConsole.UnitTests;

public sealed class TerminalSandboxPolicyTests
{
    [Theory]
    [InlineData("pytest", true)]
    [InlineData("npm test", true)]
    [InlineData("dotnet test", true)]
    [InlineData("go test ./...", true)]
    [InlineData("ruff check .", true)]
    [InlineData("tsc --noEmit", true)]
    [InlineData("rm -rf /", false)]
    [InlineData("pytest && cat /etc/passwd", false)]
    [InlineData("python -c 'print(1)'", false)]
    public void AllowList(string cmd, bool ok)
        => Assert.Equal(ok, TerminalSandboxPolicy.IsAllowed(cmd));
}
