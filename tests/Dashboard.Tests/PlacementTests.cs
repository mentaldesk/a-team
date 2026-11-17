namespace ATeam.Dashboard.Tests;

/// <summary>Every card the board sends shows in one of Overseer's lanes, and every one of yours in one of Work's columns,
/// over cards generated from every status, kind, mark and turn.</summary>
public class PlacementTests
{
    private static readonly string[] AnyStatus =
        ["Idea", "Exploring", "Pitched", "Approved", "Building", "Ready", "In progress", "In review", "None", "?Foo", ""];

    public static TheoryData<string, string?> Lanes()
    {
        var data = new TheoryData<string, string?>();
        foreach (var status in AnyStatus)
            foreach (var lane in new[] { null, "In review", "Idea", "Somewhere" })
                data.Add(status, lane);
        return data;
    }

    [Theory]
    [MemberData(nameof(Lanes))]
    public void Every_card_shows_in_an_Overseer_lane(string status, string? lane)
    {
        var said = lane is null ? "" : $", \"lane\": \"{lane}\"";
        var card = Assert.Single(BoardCard.Parse($$"""[{"number": 8, "team": "t", "status": "{{status}}"{{said}}}]"""));
        Assert.Contains(card.Status, OverseerBoard.Columns);
        if (lane is not null && OverseerBoard.Columns.Contains(lane)) Assert.Equal(lane, card.Status);
    }

    public static IEnumerable<object[]> Cards()
    {
        foreach (var status in AnyStatus.Where(status => status.Length > 0))
            foreach (var turn in new[] { "you", "", "dev", "lead", "upstream" })
                foreach (var held in new[] { "", "you", "lead", "dev", "dispatcher" })
                    foreach (var priority in new[] { "", "High" })
                        foreach (var question in new[] { "", "Which?" })
                            foreach (var flags in new[] { "", "pitch", "stuck", "dependency", "document", "docs", "upstream", "pr", "onhold" })
                                yield return [status, turn, held, priority, question, flags];
    }

    [Theory]
    [MemberData(nameof(Cards))]
    public void Every_card_of_yours_shows_in_a_Work_column(string status, string turn, string held, string priority, string question, string flags)
    {
        var item = new WaitingItem(8, "A card", status, "u", "t", turn, "why", Pr: flags == "pr" ? 12 : 0, Priority: priority,
            Pitch: flags == "pitch", Question: question, Unready: flags == "pr" ? "CI failing" : "", Role: flags == "docs" ? "customer" : "",
            Document: flags == "document", Dependency: flags == "dependency", Upstream: flags == "upstream" ? "tui#1: open" : "", HeldBy: held)
        {
            Stuck = flags == "stuck",
            OnHold = flags == "onhold" || held.Length > 0,
        };
        var ranked = status == "Idea" && priority.Length > 0 && !item.OnHold;
        if (!item.Mine || item.WaitsUpstream || ranked) return;
        Assert.Contains(WorkView.Gates, gate => gate.Holds(item) || gate.Aside?.Invoke(item) == true);
    }

    [Fact]
    public void A_stuck_turn_waits_under_Blocked_with_its_reason_not_in_Review()
    {
        var item = new WaitingItem(130, "Theme tokens", "In review", "u", "tui", "you",
            "dev answered on tui-style-guide#130, but nothing will start a run for it", Pr: 130, Unready: "")
        { Stuck = true };
        Assert.Equal("Blocked", WorkView.GateOf(item));
        Assert.Contains("dev answered on tui-style-guide#130, but nothing will start a run for it", item.Line);
    }

    [Theory]
    [InlineData("Idea", "High")]
    [InlineData("Exploring", "")]
    [InlineData("Approved", "")]
    [InlineData("In progress", "")]
    public void A_held_card_no_other_column_takes_waits_under_Blocked(string status, string priority) =>
        Assert.Equal("Blocked", WorkView.GateOf(new WaitingItem(8, "", status, "", "t", "you", "held by you", Priority: priority, HeldBy: "you") { OnHold = true }));

    [Fact]
    public void Every_lane_a_board_can_print_is_one_Overseer_has()
    {
        var parsed = BoardCard.Parse(string.Join(",", AnyStatus.Select((status, i) => $$"""{"number": {{i + 1}}, "team": "t", "status": "{{status}}"}"""))
            .Insert(0, "[") + "]");
        Assert.Equal(AnyStatus.Length, parsed.Count);
        Assert.All(parsed, card => Assert.Contains(card.Status, OverseerBoard.Columns));
    }
}
