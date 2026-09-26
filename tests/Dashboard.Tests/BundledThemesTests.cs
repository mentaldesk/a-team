using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public class BundledThemesTests : StaticConfigurationTest
{
    [Fact]
    public void Every_theme_we_offer_is_registered_at_start_up()
    {
        BundledThemes.Load();

        Assert.All(BundledThemes.Names, name => Assert.True(ThemeManager.Themes?.ContainsKey(name), name));
    }

    [Fact]
    public void Every_theme_defines_the_schemes_the_dashboard_draws_with()
    {
        BundledThemes.Load();

        foreach (var theme in BundledThemes.Names)
        {
            BundledThemes.Apply(theme);
            foreach (var scheme in Enum.GetValues<Schemes>())
            {
                var name = SchemeManager.SchemesToSchemeName(scheme);
                Assert.True(name is not null && SchemeManager.TryGetScheme(name, out _), $"{theme} has no {scheme} scheme");
            }
        }
    }

    [Fact]
    public void Every_theme_draws_the_menu_in_a_background_of_its_own()
    {
        BundledThemes.Load();

        foreach (var theme in BundledThemes.Names)
        {
            BundledThemes.Apply(theme);
            var menu = SchemeManager.GetScheme(Schemes.Menu);
            var window = SchemeManager.GetScheme(Schemes.Base);
            Assert.NotEqual(window.Normal.Background, menu.Normal.Background);
        }
    }

    [Fact]
    public void Every_theme_draws_an_open_menu_as_a_bordered_panel()
    {
        BundledThemes.Load();

        foreach (var theme in BundledThemes.Names)
        {
            BundledThemes.Apply(theme);
            Assert.NotEqual(LineStyle.None, Menu.DefaultBorderStyle);
        }
    }

    [Fact]
    public void Every_theme_underlines_the_hot_letter_the_way_the_framework_would()
    {
        BundledThemes.Load();

        foreach (var theme in BundledThemes.Names)
        {
            BundledThemes.Apply(theme);
            foreach (var name in new[] { "Base", "Accent", "Dialog", "Menu", "Error" })
            {
                var scheme = SchemeManager.GetScheme(name);
                foreach (var (role, attribute) in new[]
                         {
                             ("HotNormal", scheme.HotNormal),
                             ("HotFocus", scheme.HotFocus),
                             ("HotActive", scheme.HotActive),
                         })
                    Assert.True(attribute.Style.HasFlag(TextStyle.Underline), $"{theme} {name} {role}");
            }
        }
    }

    [Fact]
    public void A_theme_we_do_not_ship_falls_back_to_the_default()
    {
        BundledThemes.Load();

        BundledThemes.Apply("Solarized");

        Assert.Equal(BundledThemes.Default, BundledThemes.Current);
    }

    [Fact]
    public void A_fresh_run_starts_on_midnight()
    {
        BundledThemes.Load();

        Assert.Equal(BundledThemes.Midnight, BundledThemes.Current);
    }

    [Fact]
    public void A_run_starts_in_the_theme_it_was_loaded_with()
    {
        BundledThemes.Load(BundledThemes.Daylight);

        Assert.Equal(BundledThemes.Daylight, BundledThemes.Current);
    }

    [Fact]
    public void The_success_scheme_is_re_derived_from_the_theme_it_switched_to()
    {
        BundledThemes.Load();
        var before = SchemeManager.GetScheme(Schemes.Base).Normal.Background;

        BundledThemes.Apply(BundledThemes.Daylight);

        var baseScheme = SchemeManager.GetScheme(Schemes.Base);
        var success = SchemeManager.GetScheme(LogSchemes.Success);
        Assert.NotEqual(before, baseScheme.Normal.Background);
        Assert.Equal(baseScheme.Normal.Background, success.Normal.Background);
        Assert.NotEqual(baseScheme.Normal.Foreground, success.Normal.Foreground);
    }

    [Fact]
    public void The_menu_scheme_is_re_resolved_from_the_theme_it_switched_to()
    {
        BundledThemes.Load();
        var before = SchemeManager.GetScheme(Schemes.Menu).Normal.Background;

        BundledThemes.Apply(BundledThemes.Daylight);

        Assert.NotEqual(before, SchemeManager.GetScheme(Schemes.Menu).Normal.Background);
    }
}
