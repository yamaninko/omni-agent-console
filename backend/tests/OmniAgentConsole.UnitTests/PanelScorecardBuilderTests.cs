using OmniAgentConsole.Application.Panels;

namespace OmniAgentConsole.UnitTests;

public sealed class PanelScorecardBuilderTests
{
    [Fact]
    public void Build_IncludesSpeakersAndVotes()
    {
        var votes = new[] { new PanelVoteTallyDto(Guid.NewGuid(), "Ada", 3) };
        var card = PanelScorecardBuilder.Build(
            "T",
            "Remote work?",
            "Completed",
            [("Ada", "Hello world speech"), ("Bob", "Short")],
            votes);

        Assert.Contains("Ada", card.Markdown);
        Assert.Contains("Audience lead: Ada", card.ClosingBlurb);
        Assert.Equal(2, card.Speakers.Count);
    }
}
