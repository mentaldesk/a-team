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
        Assert.Equal(expected, AgentPane.Header("a-team · dev", selected, running, following, expanded));
}
