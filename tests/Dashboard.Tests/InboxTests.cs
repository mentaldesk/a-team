namespace ATeam.Dashboard.Tests;

public class InboxTests
{
    internal static readonly WaitingItem UpstreamPr =
        new(130, "Pane width follows the window", "In review", "https://github.com/mentaldesk/tui-style-guide/pull/130", "tui",
            "you", "awaiting your acceptance", Pr: 130);

    internal static readonly WaitingItem Waiting =
        new(672, "Resizable Assistant", "Ready", "https://github.com/mentaldesk/a-team/issues/672", "a-team",
            "upstream", "waiting on tui-style-guide#130: open", Upstream: "tui-style-guide#130: open",
            Chains: [[new("tui", [130], "tui-style-guide#130", "open")]], Frees: [673]);

    internal static readonly WaitingItem Review =
        new(828, "Worth considering", "In review", "https://github.com/mentaldesk/a-team/issues/828", "a-team",
            "you", "awaiting your acceptance", Pr: 829, Frees: [831]);

    [Fact]
    public void A_card_freeing_a_longer_chain_ranks_above_one_freeing_a_single_task_and_a_link_on_another_team_counts()
    {
        var rows = Inbox.Rows(Holds.Tag([Review, Waiting, UpstreamPr]));

        Assert.Equal(["tui     PR #130  review  frees a-team #672 · #673", "a-team  #828     review  frees a-team #831"],
            Inbox.Lines(rows));
        Assert.Equal("tui PR #130 → a-team #672 → a-team #673", rows[0].Chain);
    }

    [Fact]
    public void An_agent_s_card_and_a_card_of_mine_that_nothing_waits_on_are_left_out()
    {
        var theDevs = Review with { Number = 900, Turn = "dev" };
        var nothingWaits = Review with { Number = 901, Frees = null };

        var rows = Inbox.Rows(Holds.Tag([theDevs, nothingWaits, Review]));

        Assert.Equal([828], rows.Select(row => row.Item.Number));
    }

    [Fact]
    public void A_Dependabot_bump_and_an_upstream_wait_held_up_by_the_same_card_both_count()
    {
        var bump = new WaitingItem(95, "Bump MentalDesk.Tui", "In review", "https://github.com/mentaldesk/a-team/pull/95", "a-team",
            "upstream", "", Pr: 95, Dependency: true, Chains: [[new("tui", [130], "tui-style-guide#130", "open")]]);

        var row = Inbox.Rows(Holds.Tag([bump, Waiting, UpstreamPr, Review])).First();

        Assert.Equal(130, row.Item.Number);
        Assert.Equal([("a-team", 95), ("a-team", 672), ("a-team", 673)], row.Frees.OrderBy(freed => freed.Number));
    }

    [Theory]
    [InlineData("In review", "", false, "review")]
    [InlineData("Pitched", "High", false, "approve")]
    [InlineData("Pitched", "", false, "rank")]
    [InlineData("Ready", "", false, "answer")]
    [InlineData("Pitched", "High", true, "answer")]
    public void Each_row_says_what_my_move_on_it_is(string status, string priority, bool asks, string move)
    {
        var item = new WaitingItem(5, "", status, "", "a-team", "you", "", Priority: priority, Pitch: status == "Pitched",
            Question: asks ? "Which?" : "", HeldBy: status == "Ready" ? "dev" : "");

        Assert.Equal(move, Inbox.Move(item));
    }
}
