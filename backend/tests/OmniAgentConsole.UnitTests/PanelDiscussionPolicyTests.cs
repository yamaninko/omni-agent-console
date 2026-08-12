using System;
using System.Collections.Generic;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Domain.Enums;
using Xunit;

namespace OmniAgentConsole.UnitTests;

public sealed class PanelDiscussionPolicyTests
{
    private sealed record M(Guid Id, string Name, int Order, bool Enabled, PanelMemberRole Role = PanelMemberRole.Commentator);

    [Fact]
    public void OrderSpeakers_SortsByOrderThenName_SkipsDisabled()
    {
        var a = new M(Guid.Parse("10000000-0000-0000-0000-000000000001"), "Alice", 1, true);
        var b = new M(Guid.Parse("10000000-0000-0000-0000-000000000002"), "Bob", 0, true);
        var c = new M(Guid.Parse("10000000-0000-0000-0000-000000000003"), "Cara", 0, false);
        var d = new M(Guid.Parse("10000000-0000-0000-0000-000000000004"), "Dave", 2, true);

        var ordered = PanelDiscussionPolicy.OrderSpeakers(
            new[] { a, b, c, d },
            m => m.Enabled,
            m => m.Order,
            m => m.Name,
            m => m.Id);

        Assert.Equal(new[] { "Bob", "Alice", "Dave" }, ordered.Select(m => m.Name).ToArray());
    }

    [Fact]
    public void OrderSpeakers_ModeratorsBeforeCommentators()
    {
        var guest = new M(Guid.Parse("10000000-0000-0000-0000-000000000001"), "Alice", 0, true, PanelMemberRole.Commentator);
        var mod = new M(Guid.Parse("10000000-0000-0000-0000-000000000002"), "Mod", 5, true, PanelMemberRole.Moderator);
        var guest2 = new M(Guid.Parse("10000000-0000-0000-0000-000000000003"), "Bob", 1, true, PanelMemberRole.Commentator);

        var ordered = PanelDiscussionPolicy.OrderSpeakers(
            new[] { guest, mod, guest2 },
            m => m.Enabled,
            m => m.Role,
            m => m.Order,
            m => m.Name,
            m => m.Id);

        Assert.Equal(new[] { "Mod", "Alice", "Bob" }, ordered.Select(m => m.Name).ToArray());
    }

    [Theory]
    [InlineData(PanelStance.For, "Remote first", "FOR")]
    [InlineData(PanelStance.Against, "Office culture", "AGAINST")]
    [InlineData(PanelStance.Neutral, null, "NEUTRAL")]
    public void DescribeStance_IncludesSideAndOptionalLabel(PanelStance stance, string? label, string expectedToken)
    {
        var text = PanelDiscussionPolicy.DescribeStance(stance, label);
        Assert.Contains(expectedToken, text, StringComparison.OrdinalIgnoreCase);
        if (label is not null)
        {
            Assert.Contains(label, text);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    public void CanStart_RequiresAtLeastOneEnabledMember(int count, bool expected)
    {
        Assert.Equal(expected, PanelDiscussionPolicy.CanStart(count));
    }

    [Fact]
    public void ContinueAfterTurnFailure_IsTrue_FailForwardPolicy()
    {
        Assert.True(PanelDiscussionPolicy.ContinueAfterTurnFailure);
    }

    [Fact]
    public void BuildGuestUserPrompt_FirstSpeakerHasNoPriorTurns()
    {
        var prompt = PanelDiscussionPolicy.BuildGuestUserPrompt("Climate policy", Array.Empty<(string, string)>());
        Assert.Contains("Climate policy", prompt);
        Assert.Contains("THIS SESSION", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("first commentator", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildGuestUserPrompt_IncludesPriorTurns()
    {
        var prior = new List<(string, string)> { ("Alice", "We should act now.") };
        var prompt = PanelDiscussionPolicy.BuildGuestUserPrompt("Climate", prior);
        Assert.Contains("[Alice]", prompt);
        Assert.Contains("We should act now.", prompt);
        Assert.Contains("your turn", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildGuestSystemPrompt_InjectsPersonaAndOneMinuteRule()
    {
        var system = PanelDiscussionPolicy.BuildGuestSystemPrompt("You are a skeptical economist.");
        Assert.Contains("skeptical economist", system);
        Assert.Contains("150–250", system);
        Assert.Contains("COMMENTATOR", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("panel", system, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMemberSystemPrompt_ModeratorAndStanceForCommentator()
    {
        var mod = PanelDiscussionPolicy.BuildMemberSystemPrompt(
            "You host debates.",
            PanelMemberRole.Moderator,
            PanelStance.Neutral,
            null);
        Assert.Contains("MODERATOR", mod, StringComparison.OrdinalIgnoreCase);

        var guest = PanelDiscussionPolicy.BuildMemberSystemPrompt(
            "You are an engineer.",
            PanelMemberRole.Commentator,
            PanelStance.Against,
            "Return-to-office mandates");
        Assert.Contains("COMMENTATOR", guest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AGAINST", guest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Return-to-office mandates", guest);
    }

    [Fact]
    public void BuildRosterBriefing_ListsRealSeatsAndTopic()
    {
        var roster = new List<PanelDiscussionPolicy.RosterEntry>
        {
            new("Ada", PanelMemberRole.Moderator, PanelStance.Neutral, "Host", "Ada hosts fairly."),
            new("Selin", PanelMemberRole.Commentator, PanelStance.For, "Pro thesis", "Selin argues pro."),
            new("Baran", PanelMemberRole.Commentator, PanelStance.Against, "Con thesis", "Baran argues con.")
        };

        var text = PanelDiscussionPolicy.BuildRosterBriefing("Anunnakiler", roster);
        Assert.Contains("Anunnakiler", text);
        Assert.Contains("Ada", text);
        Assert.Contains("Selin", text);
        Assert.Contains("Baran", text);
        Assert.Contains("do not invent", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMemberSystemPrompt_ForbidsInventingGuests_AndIncludesRoster()
    {
        var roster = new List<PanelDiscussionPolicy.RosterEntry>
        {
            new("Moderator Ada", PanelMemberRole.Moderator, PanelStance.Neutral, null, "Hosts"),
            new("Commentator Selin", PanelMemberRole.Commentator, PanelStance.For, "Pro", "Argues pro")
        };

        var mod = PanelDiscussionPolicy.BuildMemberSystemPrompt(
            "You host debates.",
            PanelMemberRole.Moderator,
            PanelStance.Neutral,
            null,
            "Moderator Ada",
            roster);

        Assert.Contains("NEVER invent", mod, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Moderator Ada", mod);
        Assert.Contains("Commentator Selin", mod);
        Assert.Contains("OFFICIAL ROSTER", mod);
    }

    [Fact]
    public void BuildMemberUserPrompt_StatesTopicAndSpeakerMission()
    {
        var roster = new List<PanelDiscussionPolicy.RosterEntry>
        {
            new("Selin", PanelMemberRole.Commentator, PanelStance.For, "Remote-first", "Product lens")
        };

        var prompt = PanelDiscussionPolicy.BuildMemberUserPrompt(
            "Anunnakiler kimdir?",
            PanelMemberRole.Commentator,
            "Selin",
            PanelStance.For,
            "Remote-first",
            roster,
            Array.Empty<(string, string)>());

        Assert.Contains("Anunnakiler kimdir?", prompt);
        Assert.Contains("Selin", prompt);
        Assert.Contains("Remote-first", prompt);
        Assert.Contains("THIS SESSION", prompt);
    }
}
