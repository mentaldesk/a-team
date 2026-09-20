namespace ATeam.Dashboard.Tests;

public class ThemeSettingTests : IDisposable
{
    private readonly string _configRoot = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_configRoot))
            Directory.Delete(_configRoot, recursive: true);
    }

    [Fact]
    public void Previewing_applies_the_theme_it_was_given()
    {
        var applied = new List<string>();
        var setting = new ThemeSetting(BundledThemes.Midnight, applied.Add, _ => { });

        setting.Preview(BundledThemes.Daylight);

        Assert.Equal([BundledThemes.Daylight], applied);
        Assert.Equal(BundledThemes.Daylight, setting.Current);
    }

    [Fact]
    public void Cancelling_puts_back_the_theme_that_was_in_effect_when_it_opened()
    {
        var applied = new List<string>();
        var setting = new ThemeSetting(BundledThemes.TurboPascal, applied.Add, _ => { });
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
        var setting = new ThemeSetting(BundledThemes.Midnight, applied.Add, _ => { });

        setting.Preview(BundledThemes.Midnight);
        setting.Cancel();

        Assert.Empty(applied);
    }

    [Fact]
    public void Keeping_the_previewed_theme_writes_it_to_the_settings_file()
    {
        var settings = new DashboardSettings(_configRoot);
        var setting = new ThemeSetting(BundledThemes.Midnight, _ => { }, settings.WriteTheme);

        setting.Preview(BundledThemes.Daylight);
        setting.Keep();

        Assert.Equal(BundledThemes.Daylight, settings.ReadTheme());
    }

    [Fact]
    public void Cancelling_leaves_the_file_on_disk_byte_identical()
    {
        var settings = new DashboardSettings(_configRoot);
        settings.WriteTheme(BundledThemes.TurboPascal);
        var path = Path.Combine(_configRoot, "dashboard.json");
        var before = File.ReadAllBytes(path);
        var setting = new ThemeSetting(BundledThemes.TurboPascal, _ => { }, settings.WriteTheme);

        setting.Preview(BundledThemes.Daylight);
        setting.Cancel();

        Assert.Equal(before, File.ReadAllBytes(path));
    }
}
