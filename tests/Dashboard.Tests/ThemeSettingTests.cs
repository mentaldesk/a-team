namespace ATeam.Dashboard.Tests;

public class ThemeSettingTests
{
    [Fact]
    public void Previewing_applies_the_theme_it_was_given()
    {
        var applied = new List<string>();
        var setting = new ThemeSetting(BundledThemes.Midnight, applied.Add);

        setting.Preview(BundledThemes.Daylight);

        Assert.Equal([BundledThemes.Daylight], applied);
        Assert.Equal(BundledThemes.Daylight, setting.Current);
    }

    [Fact]
    public void Cancelling_puts_back_the_theme_that_was_in_effect_when_it_opened()
    {
        var applied = new List<string>();
        var setting = new ThemeSetting(BundledThemes.TurboPascal, applied.Add);
        setting.Preview(BundledThemes.Daylight);
        setting.Preview(BundledThemes.ModernBorland);

        setting.Cancel();

        Assert.Equal(BundledThemes.TurboPascal, setting.Current);
        Assert.Equal(BundledThemes.TurboPascal, applied[^1]);
    }

    [Fact]
    public void Re_selecting_the_theme_already_showing_costs_nothing()
    {
        var applied = new List<string>();
        var setting = new ThemeSetting(BundledThemes.Midnight, applied.Add);

        setting.Preview(BundledThemes.Midnight);
        setting.Cancel();

        Assert.Empty(applied);
    }
}
