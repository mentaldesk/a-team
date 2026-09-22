namespace ATeam.Dashboard.Tests;

public class AgentPaneTests
{
    [Theory]
    [InlineData(false, false, true, false, "○ a-team · dev")]
    [InlineData(true, false, true, false, "▶ ○ a-team · dev")]
    [InlineData(true, true, true, false, "▶ ● a-team · dev")]
    [InlineData(true, true, false, false, "▶ ● a-team · dev [scrolled]")]
    [InlineData(true, true, true, true, "▶ ● a-team · dev [tool calls]")]
    [InlineData(true, true, false, true, "▶ ● a-team · dev [tool calls] [scrolled]")]
    [InlineData(false, false, false, true, "○ a-team · dev [tool calls] [scrolled]")]
    public void The_title_says_which_state_the_pane_is_in(
        bool selected, bool running, bool following, bool expanded, string expected) =>
        Assert.Equal(expected, AgentPane.Header("a-team · dev", selected, running, false, following, expanded));

    [Theory]
    [InlineData(false, "⏸ a-team · dev")]
    [InlineData(true, "⏸ a-team · dev")]
    public void A_paused_team_shows_the_pause_glyph_whatever_its_agents_are_doing(bool running, string expected) =>
        Assert.Equal(expected, AgentPane.Header("a-team · dev", false, running, true, following: true, expanded: false));

    [Fact]
    public void A_paused_pane_says_so_where_it_would_count_down_to_the_next_check()
    {
        var now = DateTimeOffset.UnixEpoch + TimeSpan.FromHours(1);
        var idle = new AgentState(Running: false, now - TimeSpan.FromMinutes(5), [], null);

        Assert.Equal("ran 5m ago · next check 2:00", AgentPane.Describe(idle, now, now.AddMinutes(2), paused: false));
        Assert.Equal("ran 5m ago · paused", AgentPane.Describe(idle, now, now.AddMinutes(2), paused: true));
    }
}
