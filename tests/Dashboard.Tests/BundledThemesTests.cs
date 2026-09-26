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
    public void Every_theme_draws_the_status_bar_in_a_band_of_its_own()
    {
        BundledThemes.Load();

        foreach (var theme in BundledThemes.Names)
        {
            BundledThemes.Apply(theme);
            Assert.True(SchemeManager.TryGetScheme(StatusBar.Scheme, out var status), $"{theme} has no StatusBar scheme");
            Assert.NotEqual(SchemeManager.GetScheme(Schemes.Base).Normal.Background, status!.Normal.Background);
        }
    }

    [Fact]
    public void Every_theme_draws_a_dialog_s_form_band_in_a_background_of_its_own()
    {
        BundledThemes.Load();

        foreach (var theme in BundledThemes.Names)
        {
            BundledThemes.Apply(theme);
            var dialog = SchemeManager.GetScheme(Schemes.Dialog);
            var form = SchemeManager.GetScheme(LogSchemes.Form);
            Assert.NotEqual(dialog.Normal.Background, form.Normal.Background);
            Assert.Equal(dialog.Normal.Foreground, form.Normal.Foreground);
            Assert.Equal(form.Normal.Background, form.HotNormal.Background);
            Assert.Equal(dialog.Focus, form.Focus);
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

        BundledThemes.Apply(BundledThemes.TurboPascal);

        Assert.NotEqual(before, SchemeManager.GetScheme(Schemes.Menu).Normal.Background);
    }

    [Fact]
    public void Midnight_and_daylight_share_one_menu_band()
    {
        BundledThemes.Load();
        var midnight = SchemeManager.GetScheme(Schemes.Menu).Normal;

        BundledThemes.Apply(BundledThemes.Daylight);

        Assert.Equal(midnight, SchemeManager.GetScheme(Schemes.Menu).Normal);
    }

    [Theory]
    [InlineData(BundledThemes.Midnight)]
    [InlineData(BundledThemes.Daylight)]
    public void The_menu_reads_against_its_own_band(string theme)
    {
        BundledThemes.Load();
        BundledThemes.Apply(theme);
        var menu = SchemeManager.GetScheme(Schemes.Menu);

        foreach (var (role, attribute) in new[]
                 {
                     ("Normal", menu.Normal),
                     ("HotNormal", menu.HotNormal),
                     ("Focus", menu.Focus),
                     ("HotFocus", menu.HotFocus),
                     ("Active", menu.Active),
                     ("HotActive", menu.HotActive),
                 })
            Assert.True(Contrast(attribute.Foreground, attribute.Background) >= 4.5,
                $"{theme} {role} is {Contrast(attribute.Foreground, attribute.Background):0.00}:1");
    }

    static double Contrast(Color foreground, Color background)
    {
        var (a, b) = (Luminance(foreground), Luminance(background));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    static double Luminance(Color colour)
    {
        static double Channel(byte value)
        {
            var v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(colour.R) + 0.7152 * Channel(colour.G) + 0.0722 * Channel(colour.B);
    }
}
