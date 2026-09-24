using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard.Tests;

public class IconsTests : StaticConfigurationTest
{
    private static readonly WaitingItem Mine =
        new(107, "When the dashboard goes quiet", "Pitched", "https://github.com/x/1", "a-team", "you", "awaiting you");

    private static readonly WaitingItem Theirs =
        new(118, "A worktree sits idle", "Pitched", "https://github.com/x/2", "a-team", "lead", "answering you");

    public static TheoryData<IconStyle> Styles => [IconStyle.Auto, IconStyle.NerdFont, IconStyle.Unicode];

    [Theory]
    [MemberData(nameof(Styles))]
    public void Every_style_has_a_glyph_for_every_meaning(IconStyle style) =>
        Assert.All(Enum.GetValues<Icon>(), icon => Assert.NotEmpty(Icons.Glyph(icon, style)));

    [Theory]
    [MemberData(nameof(Styles))]
    public void An_icon_costs_the_same_cells_whichever_style_it_is_drawn_in(IconStyle style) =>
        Assert.All(Enum.GetValues<Icon>(), icon => Assert.Equal(Icons.Width, Icons.Field(icon, style).GetColumns()));

    [Theory]
    [InlineData("世")]
    [InlineData("🙂")]
    public void A_glyph_the_runtime_calls_double_width_fills_the_field_rather_than_pushing_the_row_along(string wide)
    {
        Assert.Equal(Icons.Width, wide.GetColumns());

        Assert.Equal(wide, Icons.Field(wide));
        Assert.Equal(Icons.Width, Icons.Field(wide).GetColumns());
    }

    [Fact]
    public void The_Unicode_style_is_the_glyphs_the_dashboard_draws_today() =>
        Assert.Equal(
            new Dictionary<Icon, string>
            {
                [Icon.Running] = "●",
                [Icon.NeverRun] = "○",
                [Icon.Paused] = "⏸",
                [Icon.Ok] = "✓",
                [Icon.Failed] = "✗",
                [Icon.CutShort] = "✗",
                [Icon.Selected] = "▶",
                [Icon.ToolCall] = "▸",
                [Icon.ToolError] = "✗",
                [Icon.Finished] = "■",
                [Icon.YourMove] = "✓",
                [Icon.TheirMove] = "·",
            },
            Enum.GetValues<Icon>().ToDictionary(icon => icon, icon => Icons.Glyph(icon, IconStyle.Unicode)));

    [Fact]
    public void Every_Nerd_Font_glyph_is_one_of_the_private_use_codepoints_that_font_fills()
    {
        Assert.All(
            Enum.GetValues<Icon>().Select(icon => Icons.Glyph(icon, IconStyle.NerdFont)),
            glyph => Assert.InRange(char.ConvertToUtf32(glyph, 0), 0xF0001, 0xF1AF0));
    }

    [Fact]
    public void Auto_draws_the_style_the_terminal_answered_with()
    {
        Assert.Equal(IconStyle.NerdFont, Icons.Resolve(IconStyle.Auto, IconStyle.NerdFont));
        Assert.Equal(IconStyle.Unicode, Icons.Resolve(IconStyle.Auto, IconStyle.Unicode));
    }

    [Fact]
    public void A_style_of_its_own_is_drawn_whatever_the_terminal_answered()
    {
        Assert.Equal(IconStyle.NerdFont, Icons.Resolve(IconStyle.NerdFont, IconStyle.Unicode));
        Assert.Equal(IconStyle.Unicode, Icons.Resolve(IconStyle.Unicode, IconStyle.NerdFont));
    }

    [Fact]
    public void Every_pane_status_names_the_meaning_it_shares_its_name_with() =>
        Assert.All(
            Enum.GetValues<PaneStatus>(),
            status => Assert.Equal(status.ToString(), Icons.For(status).ToString()));

    [Theory]
    [MemberData(nameof(Styles))]
    public void A_run_cut_short_wears_the_icon_a_failed_one_wears(IconStyle style) =>
        Assert.Equal(Icons.Glyph(Icon.Failed, style), Icons.Glyph(Icon.CutShort, style));

    [Fact]
    public void A_card_that_is_your_move_wears_the_check_and_one_that_isn_t_wears_the_agent()
    {
        Assert.Equal(Icons.Glyph(Icon.YourMove, IconStyle.NerdFont), Icons.For(Mine, IconStyle.NerdFont).Glyph);
        Assert.Equal(Icons.Glyph(Icon.TheirMove, IconStyle.NerdFont), Icons.For(Theirs, IconStyle.NerdFont).Glyph);
        Assert.Equal(LogSchemes.Success, Icons.For(Mine, IconStyle.NerdFont).Scheme);
        Assert.Equal(LogSchemes.Dimmed, Icons.For(Theirs, IconStyle.NerdFont).Scheme);
    }

    [Fact]
    public void Without_a_Nerd_Font_the_same_two_states_read_in_plain_text()
    {
        Assert.Equal("✓", Icons.For(Mine, IconStyle.Unicode).Glyph);
        Assert.Equal("·", Icons.For(Theirs, IconStyle.Unicode).Glyph);
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void Either_style_costs_a_card_the_same_cells(IconStyle style)
    {
        foreach (var item in new[] { Mine, Theirs })
            Assert.Equal(Icons.Width, Icons.Field(Icons.For(item, style).Glyph).GetColumns());
    }

    [Fact]
    public void A_styles_sample_reads_in_that_style_and_costs_the_same_cells_either_way()
    {
        Assert.StartsWith(Icons.Field(Icon.Running, IconStyle.Unicode), Icons.Sample(IconStyle.Unicode));
        Assert.EndsWith(Icons.Field(Icon.ToolCall, IconStyle.NerdFont), Icons.Sample(IconStyle.NerdFont));
        Assert.Equal(Icons.Sample(IconStyle.Unicode).GetColumns(), Icons.Sample(IconStyle.NerdFont).GetColumns());
    }

    [Fact]
    public void An_icon_is_drawn_in_its_own_colour_over_the_rows_own_background()
    {
        BundledThemes.Load();
        var row = new Attribute(StandardColor.White, StandardColor.Blue);

        var colour = CardCells.Colour(Icons.For(Mine, IconStyle.NerdFont).Scheme, row);

        Assert.Equal(SchemeManager.GetScheme(LogSchemes.Success).Normal.Foreground, colour.Foreground);
        Assert.Equal(row.Background, colour.Background);
    }

    [Fact]
    public void A_scheme_that_isn_t_registered_leaves_the_row_as_it_was()
    {
        var row = new Attribute(StandardColor.White, StandardColor.Blue);

        Assert.Equal(row, CardCells.Colour("NoSuchScheme", row));
    }

    [Fact]
    public void A_field_is_a_cell_per_column_and_a_glyph_that_fills_it_leaves_the_second_one_empty()
    {
        Assert.Equal(["✓", " "], CardCells.Field("✓"));
        Assert.Equal(["└", " "], CardCells.Field("└"));
    }

    [Fact]
    public void The_icon_and_the_number_are_painted_over_the_cells_the_tree_laid_out()
    {
        BundledThemes.Load();
        var row = new Attribute(StandardColor.White, StandardColor.Blue);
        var cells = Row($"  {new Card(Mine, false).Text(0)}", row);

        CardCells.Paint(cells, 0, Icons.For(Mine, IconStyle.Unicode), new PriorityMark(4, Priorities.Scheme("Urgent")));

        Assert.Equal("✓ #107  When the dashboard goes quiet", Text(cells));
        Assert.Equal(SchemeManager.GetScheme(LogSchemes.Success).Normal.Foreground, cells[0].Attribute?.Foreground);
        Assert.Equal(SchemeManager.GetScheme(Priorities.Scheme("Urgent")).Normal.Foreground, cells[2].Attribute?.Foreground);
        Assert.Equal(row.Foreground, cells[^1].Attribute?.Foreground);
        Assert.All(cells, cell => Assert.Equal(row.Background, cell.Attribute?.Background));
    }

    [Fact]
    public void A_row_scrolled_past_its_field_is_left_as_the_tree_drew_it()
    {
        var row = new Attribute(StandardColor.White, StandardColor.Blue);
        var cells = Row("dashboard goes quiet", row);

        CardCells.Paint(cells, -3, Icons.For(Mine, IconStyle.Unicode), new PriorityMark(4, Priorities.Scheme("Urgent")));

        Assert.Equal("dashboard goes quiet", Text(cells));
        Assert.All(cells, cell => Assert.Equal(row, cell.Attribute));
    }

    private static List<Cell> Row(string text, Attribute attribute) =>
        [.. text.Select(character => new Cell { Grapheme = character.ToString(), Attribute = attribute })];

    private static string Text(IEnumerable<Cell> cells) => string.Concat(cells.Select(cell => cell.Grapheme));
}
