using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard.Tests;

public class TurnIconsTests : StaticConfigurationTest
{
    private static readonly WaitingItem Mine =
        new(107, "When the dashboard goes quiet", "Pitched", "https://github.com/x/1", "a-team", "you", "awaiting you");

    private static readonly WaitingItem Theirs =
        new(118, "A worktree sits idle", "Pitched", "https://github.com/x/2", "a-team", "lead", "answering you");

    [Fact]
    public void A_card_that_is_your_move_wears_the_check_and_one_that_isn_t_wears_the_agent()
    {
        Assert.Equal("\U000F05E0", TurnIcons.For(Mine, nerdFont: true).Glyph);
        Assert.Equal("\U000F0D70", TurnIcons.For(Theirs, nerdFont: true).Glyph);
        Assert.Equal(LogSchemes.Success, TurnIcons.For(Mine, nerdFont: true).Scheme);
        Assert.Equal(LogSchemes.Dimmed, TurnIcons.For(Theirs, nerdFont: true).Scheme);
    }

    [Fact]
    public void Without_a_Nerd_Font_the_same_two_states_read_in_plain_text()
    {
        Assert.Equal("✓", TurnIcons.For(Mine, nerdFont: false).Glyph);
        Assert.Equal("·", TurnIcons.For(Theirs, nerdFont: false).Glyph);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Either_style_costs_a_card_the_same_cells(bool nerdFont)
    {
        foreach (var item in new[] { Mine, Theirs })
            Assert.Equal(TurnIcons.Width, TurnIcons.For(item, nerdFont).Glyph.GetColumns() + 1);
    }

    [Fact]
    public void An_icon_is_drawn_in_its_own_colour_over_the_rows_own_background()
    {
        BundledThemes.Load();
        var row = new Attribute(StandardColor.White, StandardColor.Blue);

        var colour = CardSource.Colour(TurnIcons.For(Mine, nerdFont: true), row);

        Assert.Equal(SchemeManager.GetScheme(LogSchemes.Success).Normal.Foreground, colour.Foreground);
        Assert.Equal(row.Background, colour.Background);
    }

    [Fact]
    public void A_scheme_that_isn_t_registered_leaves_the_row_as_it_was()
    {
        var row = new Attribute(StandardColor.White, StandardColor.Blue);

        Assert.Equal(row, CardSource.Colour(new TurnIcon("?", "NoSuchScheme"), row));
    }
}
