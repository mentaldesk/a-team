namespace ATeam.Dashboard.Tests;

public class WaitingItemTests
{
    [Fact]
    public void Every_field_the_board_prints_reaches_the_card()
    {
        var items = WaitingItem.Parse("""
            [{"number": 106, "title": "Both gates are mine", "status": "Pitched",
              "url": "https://github.com/mentaldesk/a-team/issues/106", "team": "a-team"}]
            """);

        Assert.Equal(
            new WaitingItem(106, "Both gates are mine", "Pitched", "https://github.com/mentaldesk/a-team/issues/106", "a-team"),
            Assert.Single(items));
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
    }
}
