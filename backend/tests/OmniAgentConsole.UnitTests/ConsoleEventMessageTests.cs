using OmniAgentConsole.Infrastructure.Runtime;

namespace OmniAgentConsole.UnitTests;

public sealed class ConsoleEventMessageTests
{
    [Fact]
    public void TruncateMessage_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ConsoleEventService.TruncateMessage(null));
        Assert.Equal(string.Empty, ConsoleEventService.TruncateMessage(string.Empty));
    }

    [Fact]
    public void TruncateMessage_UnderLimit_Unchanged()
    {
        var msg = new string('a', ConsoleEventService.MaxMessageLength);
        Assert.Equal(msg, ConsoleEventService.TruncateMessage(msg));
    }

    [Fact]
    public void TruncateMessage_OverLimit_FitsColumnAndMarksTruncated()
    {
        var msg = new string('x', ConsoleEventService.MaxMessageLength + 500);
        var truncated = ConsoleEventService.TruncateMessage(msg);

        Assert.Equal(ConsoleEventService.MaxMessageLength, truncated.Length);
        Assert.EndsWith("…[truncated]", truncated);
        Assert.StartsWith("xxx", truncated);
    }

    [Fact]
    public void TruncateMessage_FixPackagingStylePrompt_DoesNotExceedColumn()
    {
        // Reproduces the ee60d1fc failure: full prompt echoed into console_events.Message.
        var dockerLog = new string('e', 4500);
        var prompt =
            $"Task execution started with prompt: \"Workspace projesinin Docker packaging hatasını düzelt.\n\n" +
            $"DOCKER LOG:\n```\n{dockerLog}\n```\"";

        Assert.True(prompt.Length > ConsoleEventService.MaxMessageLength);
        var truncated = ConsoleEventService.TruncateMessage(prompt);
        Assert.Equal(ConsoleEventService.MaxMessageLength, truncated.Length);
    }
}
