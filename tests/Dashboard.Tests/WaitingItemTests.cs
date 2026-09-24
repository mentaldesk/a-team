namespace ATeam.Dashboard.Tests;

public class WaitingItemTests
{
    [Fact]
    public void Every_field_the_board_prints_reaches_the_card()
    {
        var items = WaitingItem.Parse("""
            [{"number": 106, "title": "Both gates are mine", "status": "Pitched",
              "url": "https://github.com/mentaldesk/a-team/issues/106", "team": "a-team",
              "turn": "lead", "reason": "answering your feedback since 08:14"}]
            """);

        Assert.Equal(
            new WaitingItem(106, "Both gates are mine", "Pitched", "https://github.com/mentaldesk/a-team/issues/106",
                "a-team", "lead", "answering your feedback since 08:14"),
            Assert.Single(items));
    }

    [Fact]
    public void An_item_whose_PR_is_in_trouble_brings_the_PR_with_it()
    {
        var items = WaitingItem.Parse("""
            [{"number": 124, "title": "A finished task", "status": "In review",
              "url": "https://github.com/mentaldesk/a-team/issues/124", "team": "a-team",
              "turn": "dev", "reason": "CI failing since 09:02", "trouble": "CI failing",
              "pr": 131, "prUrl": "https://github.com/mentaldesk/a-team/pull/131",
              "checks": "fail", "conflicting": false, "draft": false}]
            """);

        var item = Assert.Single(items);
        Assert.Equal(131, item.Pr);
        Assert.Equal("https://github.com/mentaldesk/a-team/pull/131", item.PrUrl);
        Assert.Equal("CI failing", item.Trouble);
    }

    [Fact]
    public void An_item_with_no_open_PR_has_no_PR_to_open()
    {
        var item = Assert.Single(WaitingItem.Parse("""[{"number": 107, "turn": "you"}]"""));

        Assert.Equal(0, item.Pr);
        Assert.Equal("", item.PrUrl);
        Assert.Equal("", item.Trouble);
    }

    [Fact]
    public void A_team_with_nothing_at_a_gate_has_no_cards()
    {
        Assert.Empty(WaitingItem.Parse("[]"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[42]")]
    [InlineData("[{\"title\": \"no number\"}]")]
    public void Anything_that_isn_t_an_item_is_left_out_instead_of_throwing(string json)
    {
        Assert.Empty(WaitingItem.Parse(json));
    }

    [Fact]
    public void A_field_the_board_left_out_reads_as_empty()
    {
        var item = Assert.Single(WaitingItem.Parse("""[{"number": 12}]"""));

        Assert.Equal(new WaitingItem(12, "", "", "", ""), item);
        Assert.True(item.Mine);
    }

    [Theory]
    [InlineData("you", "awaiting your approval since 08:14", "#116 · awaiting your approval since 08:14")]
    [InlineData("dev", "answering your feedback since 08:14", "#116 · dev · answering your feedback since 08:14")]
    [InlineData("", "", "#116")]
    public void The_message_bar_line_names_the_role_only_where_the_move_isn_t_yours(
        string turn, string reason, string expected)
    {
        Assert.Equal(expected, new WaitingItem(116, "A title", "In review", "https://github.com/x/1", "a-team", turn, reason).Line);
    }

    [Fact]
    public void The_message_bar_line_ends_with_the_PR_the_p_key_would_open()
    {
        var item = new WaitingItem(124, "A title", "In review", "https://github.com/x/1", "a-team",
            "dev", "CI failing since 09:02", Pr: 131, PrUrl: "https://github.com/x/pull/131", Trouble: "CI failing");

        Assert.Equal("#124 · dev · CI failing since 09:02 · PR #131", item.Line);
    }
}
