using System.Text.Json;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

public class DashboardSettingsTests : IDisposable
{
    private readonly string _configRoot = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_configRoot))
            Directory.Delete(_configRoot, recursive: true);
    }

    [Fact]
    public void The_theme_written_is_the_theme_the_next_run_reads()
    {
        new DashboardSettings(_configRoot).WriteTheme(BundledThemes.Daylight);

        Assert.Equal(BundledThemes.Daylight, new DashboardSettings(_configRoot).ReadTheme());
    }

    [Fact]
    public void The_file_lands_beside_the_team_configs_with_the_theme_in_it()
    {
        new DashboardSettings(_configRoot).WriteTheme(BundledThemes.Daylight);

        var written = File.ReadAllText(Path.Combine(_configRoot, "dashboard.json"));
        Assert.Contains("\"theme\"", written);
        Assert.Contains($"\"{BundledThemes.Daylight}\"", written);
    }

    [Fact]
    public void The_area_written_is_the_area_the_next_run_opens_in()
    {
        new DashboardSettings(_configRoot).WriteArea(Area.Overseer);

        Assert.Equal(Area.Overseer, new DashboardSettings(_configRoot).ReadArea());
        Assert.Contains("\"area\"", File.ReadAllText(Path.Combine(_configRoot, "dashboard.json")));
    }

    [Theory]
    [InlineData("{\"area\": \"Dashboard\"}")]
    [InlineData("{\"area\": \"dashboard\"}")]
    public void A_saved_Dashboard_opens_on_Overseer(string contents)
    {
        Write(contents);

        Assert.Equal(Area.Overseer, new DashboardSettings(_configRoot).ReadArea());
    }

    [Theory]
    [InlineData("dashboard", Area.Overseer)]
    [InlineData("Overseer", Area.Overseer)]
    [InlineData("work", Area.Work)]
    [InlineData("nowhere", null)]
    public void The_command_line_s_area_named_Dashboard_is_Overseer(string name, Area? area) =>
        Assert.Equal(area, DashboardSettings.ParseArea(name));

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("{\"area\": \"nowhere\"}")]
    [InlineData("{\"area\": 3}")]
    [InlineData("{\"area\": \"Work\"}")]
    public void A_first_run_and_anything_we_cannot_read_as_an_area_open_on_Work(string? contents)
    {
        if (contents is not null)
            Write(contents);

        Assert.Equal(Area.Work, new DashboardSettings(_configRoot).ReadArea());
    }

    [Fact]
    public void Writing_creates_the_config_directory_if_it_is_not_there_yet()
    {
        var nested = Path.Combine(_configRoot, "nested");

        new DashboardSettings(nested).WriteTheme(BundledThemes.TurboPascal);

        Assert.Equal(BundledThemes.TurboPascal, new DashboardSettings(nested).ReadTheme());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("\"Daylight\"")]
    [InlineData("{}")]
    [InlineData("{\"theme\": 3}")]
    [InlineData("{\"theme\": null}")]
    [InlineData("{\"theme\": \"Solarized\"}")]
    public void Anything_we_cannot_read_as_a_theme_we_ship_starts_in_midnight(string? contents)
    {
        if (contents is not null)
            Write(contents);

        Assert.Equal(BundledThemes.Midnight, new DashboardSettings(_configRoot).ReadTheme());
    }

    [Fact]
    public void A_hand_edited_theme_name_is_what_the_dashboard_comes_up_in()
    {
        Write($"{{ \"theme\": \"{BundledThemes.ModernBorland}\" }}");

        Assert.Equal(BundledThemes.ModernBorland, new DashboardSettings(_configRoot).ReadTheme());
    }

    [Fact]
    public void Reading_a_file_we_could_not_understand_leaves_it_alone()
    {
        var path = Write("{ oops");

        new DashboardSettings(_configRoot).ReadTheme();

        Assert.Equal("{ oops", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(IconStyle.Auto)]
    [InlineData(IconStyle.NerdFont)]
    [InlineData(IconStyle.Unicode)]
    public void The_icon_style_written_is_the_one_the_next_run_draws_with(IconStyle style)
    {
        new DashboardSettings(_configRoot).WriteIcons(style);

        Assert.Equal(style, new DashboardSettings(_configRoot).ReadIcons());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("{\"icons\": \"boxes\"}")]
    [InlineData("{\"icons\": true}")]
    [InlineData("{\"icons\": 1}")]
    [InlineData("{\"nerdFont\": true}")]
    public void A_file_that_does_not_name_a_style_we_know_leaves_the_dashboard_on_Auto(string? contents)
    {
        var path = contents is null ? null : Write(contents);

        Assert.Equal(IconStyle.Auto, new DashboardSettings(_configRoot).ReadIcons());
        if (path is not null)
            Assert.Equal(contents, File.ReadAllText(path));
    }

    [Fact]
    public void The_style_a_file_names_is_read_whatever_case_it_is_written_in()
    {
        Write("{\"icons\": \"nerdfont\"}");

        Assert.Equal(IconStyle.NerdFont, new DashboardSettings(_configRoot).ReadIcons());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Showing_tool_calls_in_full_is_what_the_next_run_starts_panes_as(bool expand)
    {
        new DashboardSettings(_configRoot).WriteExpandToolCalls(expand);

        Assert.Equal(expand, new DashboardSettings(_configRoot).ReadExpandToolCalls());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"expandToolCalls\": \"yes\"}")]
    [InlineData("{\"expandToolCalls\": 1}")]
    [InlineData("{\"expandToolCalls\": null}")]
    public void Anything_we_cannot_read_as_a_yes_or_no_leaves_tool_calls_folded_up(string? contents)
    {
        if (contents is not null)
            Write(contents);

        Assert.False(new DashboardSettings(_configRoot).ReadExpandToolCalls());
    }

    [Fact]
    public void Writing_the_tool_calls_setting_keeps_the_theme_that_was_already_there()
    {
        var settings = new DashboardSettings(_configRoot);
        settings.WriteTheme(BundledThemes.Daylight);

        settings.WriteExpandToolCalls(true);

        Assert.Equal(BundledThemes.Daylight, settings.ReadTheme());
        Assert.True(settings.ReadExpandToolCalls());
    }

    [Fact]
    public void Writing_the_theme_keeps_the_tool_calls_setting_that_was_already_there()
    {
        var settings = new DashboardSettings(_configRoot);
        settings.WriteExpandToolCalls(true);

        settings.WriteTheme(BundledThemes.TurboPascal);

        Assert.True(settings.ReadExpandToolCalls());
        Assert.Equal(BundledThemes.TurboPascal, settings.ReadTheme());
    }

    [Fact]
    public void A_file_from_before_this_setting_existed_reads_and_keeps_its_theme()
    {
        Write($"{{ \"theme\": \"{BundledThemes.ModernBorland}\" }}");
        var settings = new DashboardSettings(_configRoot);
        Assert.False(settings.ReadExpandToolCalls());

        settings.WriteExpandToolCalls(true);

        Assert.Equal(BundledThemes.ModernBorland, settings.ReadTheme());
        Assert.True(settings.ReadExpandToolCalls());
    }

    [Fact]
    public void A_setting_written_twice_is_not_written_twice_over()
    {
        var settings = new DashboardSettings(_configRoot);
        settings.WriteExpandToolCalls(true);

        settings.WriteExpandToolCalls(false);

        Assert.False(settings.ReadExpandToolCalls());
        Assert.Single(JsonDocument.Parse(File.ReadAllText(Path.Combine(_configRoot, "dashboard.json")))
            .RootElement.EnumerateObject());
    }

    [Fact]
    public void The_keys_object_says_which_key_runs_which_command_in_the_order_it_gives_them()
    {
        Write("{ \"keys\": { \"settings\": \"Ctrl+,\", \"log.toolCalls\": \"d\" } }");

        Assert.Equal(
            [("settings", new Key(',').WithCtrl), ("log.toolCalls", new Key('d'))],
            new DashboardSettings(_configRoot).ReadKeys());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{\"keys\": {}}")]
    [InlineData("{\"keys\": null}")]
    [InlineData("{\"keys\": \"Ctrl+E\"}")]
    [InlineData("{\"keys\": []}")]
    public void Anything_we_cannot_read_as_a_keys_object_rebinds_nothing(string? contents)
    {
        if (contents is not null)
            Write(contents);

        Assert.Empty(new DashboardSettings(_configRoot).ReadKeys());
    }

    [Fact]
    public void An_empty_name_is_a_command_left_with_no_key()
    {
        Write("{ \"keys\": { \"settings\": \"\" } }");

        Assert.Equal([("settings", Key.Empty)], new DashboardSettings(_configRoot).ReadKeys());
    }

    [Theory]
    [InlineData("\"PgUp\"")]
    [InlineData("\"nonsense\"")]
    [InlineData("3")]
    [InlineData("null")]
    public void A_name_that_is_not_a_key_is_left_out_while_the_rest_of_the_file_still_applies(string value)
    {
        Write($"{{ \"keys\": {{ \"settings\": {value}, \"help\": \"F2\" }} }}");

        Assert.Equal([("help", Key.F2)], new DashboardSettings(_configRoot).ReadKeys());
    }

    [Theory]
    [InlineData("Ctrl+,")]
    [InlineData("PageUp")]
    [InlineData("t")]
    public void A_key_name_reads_back_as_the_name_it_was_written_under(string name)
    {
        Assert.True(Key.TryParse(name, out var key));

        Assert.Equal(name, key.ToString());
    }

    [Fact]
    public void Reading_the_keys_leaves_the_file_exactly_as_it_was()
    {
        var contents = "{ \"keys\": { \"settings\": \"PgUp\" } }";
        var path = Write(contents);

        new DashboardSettings(_configRoot).ReadKeys();

        Assert.Equal(contents, File.ReadAllText(path));
    }

    [Fact]
    public void Kept_keys_are_the_keys_the_next_run_reads_and_the_other_settings_stay()
    {
        var settings = new DashboardSettings(_configRoot);
        settings.WriteTheme(BundledThemes.Daylight);
        var commands = Commands();
        var keys = settings.KeyOverrides(commands);

        keys.Keep([.. keys.Current.Where(binding => binding.CommandId != "settings"),
            new MentalDesk.Tui.Keys.KeyBinding(MentalDesk.Tui.Keys.KeyChord.Parse("Ctrl+G S"), "settings", MentalDesk.Tui.Commands.CommandScope.Global)]);

        var next = Commands();
        new DashboardSettings(_configRoot).KeyOverrides(next).Load();
        Assert.Equal("Ctrl+G S", next.KeysFor("settings"));
        Assert.Equal(BundledThemes.Daylight, settings.ReadTheme());
    }

    [Fact]
    public void Keys_saved_before_the_keymap_carry_over_until_the_keymap_has_its_own()
    {
        Write("{ \"keys\": { \"settings\": \"s\" } }");
        var commands = Commands();
        var keys = new DashboardSettings(_configRoot).KeyOverrides(commands);

        keys.Load();
        Assert.Equal("S", commands.KeysFor("settings"));

        keys.Keep([.. keys.Defaults]);
        var next = Commands();
        new DashboardSettings(_configRoot).KeyOverrides(next).Load();
        Assert.Equal("Ctrl+,", next.KeysFor("settings"));
    }

    private static CommandRegistry Commands() => new CommandRegistry()
        .Register("settings", "Settings", () => { }, chord: "Ctrl+,")
        .Register("help", "Show keys", () => { }, Key.F1);

    private string Write(string contents)
    {
        Directory.CreateDirectory(_configRoot);
        var path = Path.Combine(_configRoot, "dashboard.json");
        File.WriteAllText(path, contents);
        return path;
    }
}

[CollectionDefinition("Environment", DisableParallelization = true)]
public sealed class EnvironmentCollection;

[Collection("Environment")]
public class ConfigRootTests : IDisposable
{
    private readonly string? _config = Environment.GetEnvironmentVariable("A_TEAM_CONFIG");
    private readonly string? _xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("A_TEAM_CONFIG", _config);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _xdg);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_TEAM_CONFIG_is_the_config_directory_as_it_stands()
    {
        Set("A_TEAM_CONFIG", "/tmp/cfg");
        Set("XDG_CONFIG_HOME", null);

        Assert.Equal("/tmp/cfg", DashboardSettings.ConfigRoot());
    }

    [Fact]
    public void XDG_CONFIG_HOME_gets_an_a_team_directory_of_its_own()
    {
        Set("A_TEAM_CONFIG", null);
        Set("XDG_CONFIG_HOME", "/tmp/xdg");

        Assert.Equal(Path.Combine("/tmp/xdg", "a-team"), DashboardSettings.ConfigRoot());
    }

    [Fact]
    public void A_TEAM_CONFIG_wins_over_XDG_CONFIG_HOME()
    {
        Set("A_TEAM_CONFIG", "/tmp/cfg");
        Set("XDG_CONFIG_HOME", "/tmp/xdg");

        Assert.Equal("/tmp/cfg", DashboardSettings.ConfigRoot());
    }

    [Fact]
    public void With_neither_set_it_is_under_the_home_directory()
    {
        Set("A_TEAM_CONFIG", null);
        Set("XDG_CONFIG_HOME", null);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, ".config", "a-team"), DashboardSettings.ConfigRoot());
    }

    private static void Set(string name, string? value) => Environment.SetEnvironmentVariable(name, value);
}
