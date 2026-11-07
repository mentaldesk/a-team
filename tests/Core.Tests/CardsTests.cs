using System.Text.Json.Nodes;
using Xunit;

namespace ATeam.Core.Tests;

public class CardsTests
{
    private static JsonNode Card(long n, string status, string? priority = null, string? rank = null, params string[] labels)
    {
        var list = new JsonArray();
        foreach (var label in labels)
            list.Add((JsonNode?)label);
        return new JsonObject
        {
            ["number"] = n,
            ["status"] = status,
            ["type"] = "Issue",
            ["priority"] = priority,
            ["labels"] = list,
            ["marks"] = rank is null ? new JsonObject() : new JsonObject { ["rank"] = rank },
            ["blockedBy"] = 0,
        };
    }

    private static JsonNode Pitch(long n, string status, string? priority = null) => Card(n, status, priority, null, "pitch");

    private static long[] Numbers(IEnumerable<JsonNode> cards) => cards.Select(card => card["number"]!.GetValue<long>()).ToArray();

    [Fact]
    public void Priority_comes_first_then_the_Leads_recommendation_and_unset_goes_last()
    {
        var sorted = Cards.ByPriority([Card(1, "Ready"), Card(2, "Ready", "Low"), Card(3, "Ready", "High", "Low"), Card(4, "Ready", "High", "Urgent"), Card(5, "Ready", null, "High")]);
        Assert.Equal([4, 3, 2, 5, 1], Numbers(sorted));
    }

    [Fact]
    public void Below_the_limit_drafts_go_up_and_nothing_is_displaced()
    {
        var (promote, demote) = Cards.PitchSwap([Pitch(1, "Pitched", "High"), Pitch(2, "Exploring", "Low"), Pitch(3, "Exploring")], 2);
        Assert.Equal([2], Numbers(promote));
        Assert.Empty(demote);
    }

    [Fact]
    public void A_higher_priority_draft_displaces_the_lowest_pitch()
    {
        var (promote, demote) = Cards.PitchSwap([Pitch(1, "Pitched", "Low"), Pitch(2, "Pitched", "Medium"), Pitch(3, "Exploring", "High")], 2);
        Assert.Equal([3], Numbers(promote));
        Assert.Equal([1], Numbers(demote));
    }

    [Fact]
    public void A_pitch_keeps_its_place_against_a_draft_of_the_same_priority()
    {
        var (promote, demote) = Cards.PitchSwap([Pitch(1, "Exploring", "High"), Pitch(2, "Pitched", "High")], 1);
        Assert.Empty(promote);
        Assert.Empty(demote);
    }

    [Fact]
    public void Only_issues_with_the_pitch_label_count_as_drafts()
    {
        var (promote, demote) = Cards.PitchSwap([Card(1, "Exploring", "High"), Pitch(2, "Ready", "High")], 1);
        Assert.Empty(promote);
        Assert.Empty(demote);
    }

    [Fact]
    public void A_card_with_no_blockedBy_in_its_record_isnt_blocked()
    {
        var card = Card(1, "Ready");
        card.AsObject().Remove("blockedBy");
        Assert.True(Cards.Startable(card));
    }

    [Theory]
    [InlineData("Ready", false, 0, true)]
    [InlineData("Ready", true, 0, false)]
    [InlineData("Ready", false, 2, false)]
    [InlineData("In progress", false, 0, false)]
    public void A_task_can_start_only_when_Ready_and_nothing_holds_it(string status, bool held, int blockedBy, bool startable)
    {
        var card = Card(1, status);
        card["blockedBy"] = blockedBy;
        if (held) card["marks"]!["held"] = new JsonObject { ["at"] = null, ["by"] = null };
        Assert.Equal(startable, Cards.Startable(card));
    }
}
