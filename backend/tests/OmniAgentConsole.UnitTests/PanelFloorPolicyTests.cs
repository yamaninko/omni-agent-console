using OmniAgentConsole.Application.Runtime;

namespace OmniAgentConsole.UnitTests;

public sealed class PanelFloorPolicyTests
{
    [Fact]
    public void ApplyModeratorOrder_JsonArray()
    {
        var remaining = new[] { "Ada", "Bob", "Cara" };
        var ordered = PanelFloorPolicy.ApplyModeratorOrder(
            remaining,
            n => n,
            """["Cara","Ada"]""");
        Assert.Equal(new[] { "Cara", "Ada", "Bob" }, ordered);
    }

    [Fact]
    public void NormalizeMode_LlmAliases()
    {
        Assert.Equal(PanelFloorPolicy.Llm, PanelFloorPolicy.NormalizeMode("moderator"));
        Assert.Equal(PanelFloorPolicy.Fixed, PanelFloorPolicy.NormalizeMode(null));
    }
}
