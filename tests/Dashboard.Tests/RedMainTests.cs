namespace ATeam.Dashboard.Tests;

/// <summary>A team's red default branch, as <c>waiting</c> reports it beside the items.</summary>
public class RedMainTests
{
    [Fact]
    public void The_entry_with_no_number_is_the_red_main_and_the_items_pass_over_it()
    {
        const string json = """
            [{"number": 12, "title": "A task", "status": "In review", "url": "u", "team": "a-team", "fix": true},
             {"team": "a-team", "red": {"branch": "main", "sha": "c0ffee1234", "since": "b8f5bdd0c0ffee", "failing": ["build"],
              "fix": {"number": 12, "status": "In review"}}}]
            """;

        Assert.Equal([new RedMain("a-team", "main", "b8f5bdd0c0ffee", 12, "In review")], RedMain.Parse(json));
        Assert.True(Assert.Single(WaitingItem.Parse(json)).Fix);
    }

    [Theory]
    [InlineData(0, "", "⚠ main is red since b8f5bdd")]
    [InlineData(624, "Ready", "⚠ main is red since b8f5bdd · fix #624 is with the Dev")]
    [InlineData(624, "In progress", "⚠ main is red since b8f5bdd · fix #624 is with the Dev")]
    [InlineData(624, "In review", "⚠ main is red since b8f5bdd · fix #624 is waiting for you")]
    public void The_line_names_the_fix_and_whose_move_it_is(int fix, string status, string line) =>
        Assert.Equal(line, new RedMain("a-team", "main", "b8f5bdd0c0ffee", fix, status).Line);

    [Fact]
    public void A_green_main_or_output_that_isn_t_a_list_has_none()
    {
        Assert.Empty(RedMain.Parse("""[{"number": 12, "title": "A task", "team": "a-team"}]"""));
        Assert.Empty(RedMain.Parse("""{"red": {}}"""));
        Assert.Empty(RedMain.Parse("not json"));
    }
}
