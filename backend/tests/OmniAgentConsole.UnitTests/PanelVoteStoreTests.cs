using OmniAgentConsole.Application.Panels;

namespace OmniAgentConsole.UnitTests;

public sealed class PanelVoteStoreTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Parse_Empty_ReturnsEmpty()
    {
        Assert.Empty(PanelVoteStore.Parse(null));
        Assert.Empty(PanelVoteStore.Parse(""));
        Assert.Empty(PanelVoteStore.Parse("{}"));
    }

    [Fact]
    public void Cast_IncrementsAndRoundTrips()
    {
        var once = PanelVoteStore.Cast(null, A);
        Assert.Equal(1, once[A]);

        var json = PanelVoteStore.Serialize(once);
        var twice = PanelVoteStore.Cast(json, A);
        twice = PanelVoteStore.Cast(PanelVoteStore.Serialize(twice), B);

        Assert.Equal(2, twice[A]);
        Assert.Equal(1, twice[B]);
    }

    [Fact]
    public void ToTallies_OrdersByVotesThenName()
    {
        var map = new Dictionary<Guid, int> { [A] = 1, [B] = 3 };
        var names = new Dictionary<Guid, string> { [A] = "Ada", [B] = "Baran" };
        var tallies = PanelVoteStore.ToTallies(map, names);

        Assert.Equal(2, tallies.Count);
        Assert.Equal(B, tallies[0].MemberId);
        Assert.Equal(3, tallies[0].Votes);
        Assert.Equal("Baran", tallies[0].DisplayName);
    }
}
