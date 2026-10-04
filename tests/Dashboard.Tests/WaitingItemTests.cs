namespace ATeam.Dashboard.Tests;

public class WaitingItemTests
{
    [Fact]
    public void Every_field_the_board_prints_reaches_the_card()
    {
        var items = WaitingItem.Parse("""
            [{"number": 106, "title": "Both gates are mine", "status": "Pitched",
              "url": "https://github.com/mentaldesk/a-team/issues/106", "team": "a-team",
              "turn": "lead", "reason": "answering your feedback since 08:14", "priority": "Urgent",
              "pitch": true}]
            """);

        Assert.Equal(
            new WaitingItem(106, "Both gates are mine", "Pitched", "https://github.com/mentaldesk/a-team/issues/106",
                "a-team", "lead", "answering your feedback since 08:14", Priority: "Urgent", Pitch: true),
            Assert.Single(items));
    }

    [Fact]
    public void A_task_handed_back_with_a_question_brings_the_question()
    {
        var item = Assert.Single(WaitingItem.Parse("""
            [{"number": 192, "title": "I can reply to a pitch", "status": "Ready", "team": "a-team",
              "turn": "you", "reason": "asked you since 08:23", "question": "Which marker?\n\nThe Dev's or none?"}]
            """));

        Assert.Equal("Which marker?\n\nThe Dev's or none?", item.Question);
        Assert.Equal("#192 · asked you since 08:23", item.Line);
    }

    [Theory]
    [InlineData(true, "Pitched", true)]
    [InlineData(false, "Pitched", false)]
    [InlineData(true, "In review", false)]
    [InlineData(false, "Idea", false)]
    public void Only_a_pitch_in_Pitched_can_be_approved(bool pitch, string status, bool approvable)
    {
        Assert.Equal(approvable, new WaitingItem(1, "", status, "", "", Pitch: pitch).Approvable);
    }

    [Theory]
    [InlineData(true, "In review", 0, true)]
    [InlineData(true, "Idea", 0, false)]
    [InlineData(true, "Exploring", 0, false)]
    [InlineData(true, "Pitched", 0, false)]
    [InlineData(true, "Approved", 0, false)]
    [InlineData(true, "Building", 0, false)]
    [InlineData(false, "Idea", 0, false)]
    [InlineData(false, "In review", 0, false)]
    [InlineData(false, "In review", 131, true)]
    public void Only_a_task_with_a_PR_or_a_pitch_in_Review_can_be_tried(bool pitch, string status, int pr, bool triable)
    {
        Assert.Equal(triable, new WaitingItem(1, "", status, "", "", Pr: pr, Pitch: pitch).Triable);
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
