namespace ATeam.Dashboard.Tests;

public class TerminalIconsTests
{
    [Theory]
    [InlineData("TERM", "xterm-kitty")]
    [InlineData("KITTY_WINDOW_ID", "1")]
    [InlineData("TERM_PROGRAM", "WezTerm")]
    [InlineData("WEZTERM_PANE", "0")]
    [InlineData("TERM", "xterm-ghostty")]
    [InlineData("GHOSTTY_RESOURCES_DIR", "/Applications/Ghostty.app/Contents/Resources")]
    public void A_terminal_that_bundles_a_Nerd_Font_gets_the_Nerd_Font_style(string variable, string value) =>
        Assert.Equal(IconStyle.NerdFont, TerminalIcons.Detect(Environment(variable, value)));

    [Fact]
    public void A_bare_environment_gets_the_plain_style() =>
        Assert.Equal(IconStyle.Unicode, TerminalIcons.Detect(_ => null));

    [Theory]
    [InlineData("TERM", "xterm-256color")]
    [InlineData("TERM", "screen-256color")]
    [InlineData("TERM_PROGRAM", "Apple_Terminal")]
    [InlineData("TERM_PROGRAM", "iTerm.app")]
    [InlineData("TERM_PROGRAM", "vscode")]
    [InlineData("TERM_PROGRAM", "tmux")]
    public void A_terminal_we_know_nothing_about_gets_the_plain_style(string variable, string value) =>
        Assert.Equal(IconStyle.Unicode, TerminalIcons.Detect(Environment(variable, value)));

    /// <summary>tmux and ssh drop the terminal's own variables, and the answer has to be the safe one.</summary>
    [Fact]
    public void A_session_whose_variables_have_been_masked_gets_the_plain_style() =>
        Assert.Equal(
            IconStyle.Unicode,
            TerminalIcons.Detect(name => name switch
            {
                "TERM" => "screen-256color",
                "KITTY_WINDOW_ID" => "",
                "GHOSTTY_RESOURCES_DIR" => "",
                _ => null,
            }));

    [Fact]
    public void The_terminal_is_recognised_whatever_case_its_variable_is_written_in() =>
        Assert.Equal(IconStyle.NerdFont, TerminalIcons.Detect(Environment("TERM_PROGRAM", "wezterm")));

    private static Func<string, string?> Environment(string variable, string value) =>
        name => name == variable ? value : null;
}
