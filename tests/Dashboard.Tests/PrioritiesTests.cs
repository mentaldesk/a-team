using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard.Tests;

public class PrioritiesTests : StaticConfigurationTest
{
    private static WaitingItem At(string priority) =>
        new(107, "When the dashboard goes quiet", "Pitched", "https://github.com/x/1", "a-team",
            "you", "awaiting your approval since 08:14", Priority: priority);

    [Theory]
    [InlineData("Urgent", "#c1408d")]
    [InlineData("High", "#d4323c")]
    [InlineData("Medium", "#9a6700")]
    [InlineData("Low", "#21833d")]
    public void Each_Priority_is_drawn_in_the_colour_GitHub_gives_it(string priority, string colour)
    {
        BundledThemes.Load();

        var scheme = SchemeManager.GetScheme(Priorities.Scheme(priority));

        Assert.Equal(new Color(colour), scheme.Normal.Foreground);
    }

    [Fact]
    public void Those_colours_survive_a_change_of_theme()
    {
        BundledThemes.Load();

        BundledThemes.Apply(BundledThemes.Daylight);

        Assert.Equal(new Color("#c1408d"), SchemeManager.GetScheme(Priorities.Scheme("Urgent")).Normal.Foreground);
    }

    [Fact]
    public void The_same_colour_on_a_form_band_keeps_the_band_s_background_and_its_underline()
    {
        BundledThemes.Load();

        var form = SchemeManager.GetScheme(Priorities.FormScheme("Urgent"));

        Assert.Equal(new Color("#c1408d"), form.Normal.Foreground);
        Assert.Equal(new Color("#c1408d"), form.HotNormal.Foreground);
        Assert.Equal(SchemeManager.GetScheme(LogSchemes.Form).Normal.Background, form.Normal.Background);
        Assert.True(form.HotNormal.Style.HasFlag(TextStyle.Underline));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Whatever")]
    public void A_priority_we_do_not_colour_has_no_scheme_on_a_form_band_either(string priority) =>
        Assert.Equal("", Priorities.FormScheme(priority));

    [Fact]
    public void A_cards_number_is_what_wears_the_colour_and_the_rest_of_it_is_left_alone()
    {
        var card = new Card(At("High"), false).Text(0);

        Assert.Equal(new PriorityMark(4, Priorities.Scheme("High")), Priorities.Mark(At("High"), card));
        Assert.StartsWith("#107", card, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Whatever")]
    public void An_item_with_no_Priority_we_colour_leaves_its_number_as_it_is(string priority)
    {
        Assert.Equal(default, Priorities.Mark(At(priority), new Card(At(priority), false).Text(0)));
    }

    [Fact]
    public void A_column_too_narrow_for_the_whole_number_colours_only_what_it_drew()
    {
        Assert.Equal(3, Priorities.Mark(At("Low"), new Card(At("Low"), false).Text(4)).Width);
    }
}
