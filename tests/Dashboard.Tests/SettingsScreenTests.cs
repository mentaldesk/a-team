using MentalDesk.Tui.Keys;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public class SettingsScreenTests : IDisposable
{
    private readonly string _configRoot = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_configRoot))
            Directory.Delete(_configRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    private DashboardSettings Settings => new(_configRoot);

    [Fact]
    public void Settings_has_the_apps_own_pages_and_no_Teams()
    {
        using var screen = Open(terminalKeys: new TerminalPageTests.FakeKeys("kitty"));

        Assert.Equal(["Theme", "Keyboard Shortcuts", "Dashboard", "Terminal", "Overseer"], PageNames(screen));
    }

    [Fact]
    public void The_foot_has_Keep_and_Cancel_buttons()
    {
        using var screen = Open();

        Assert.Equal("Ctrl+Enter Keep", screen.Dialog.KeepButton.Text.Trim());
        Assert.Equal("Esc Cancel", screen.Dialog.CancelButton.Text.Trim());
    }

    [Fact]
    public void A_changed_page_shows_a_dot_until_the_setting_is_put_back()
    {
        using var screen = Open();

        screen.Theme.Themes.Value = BundledThemes.Names.ToList().IndexOf(BundledThemes.Daylight);
        Assert.Equal("Theme •", PageNames(screen)[0]);

        screen.Theme.Themes.Value = BundledThemes.Names.ToList().IndexOf(BundledThemes.Midnight);
        Assert.Equal("Theme", PageNames(screen)[0]);
    }

    [Fact]
    public void Each_kind_of_row_says_its_default_only_while_it_is_not_at_it()
    {
        var commands = Registry();
        using var screen = Open(commands: commands);
        Assert.Equal("", screen.Theme.Default);
        Assert.Equal("", screen.Dashboard.ToolCallsDefault);
        Assert.Equal("", screen.Dashboard.IconsDefault);
        Assert.All(screen.Overseer.Defaults, note => Assert.Equal("", note));
        Assert.DoesNotContain(screen.KeysPage.Rows, row => row.Contains("(default:", StringComparison.Ordinal));

        screen.Theme.Themes.Value = BundledThemes.Names.ToList().IndexOf(BundledThemes.Daylight);
        screen.Dashboard.ToolCalls.Value = CheckState.Checked;
        screen.Dashboard.IconStyles.Value = (int)IconStyle.NerdFont;
        screen.Overseer.Fields[0].Field.Text = "2d";
        Record(screen, "Commands", Key.F4, Key.Enter);

        Assert.Equal("(default: Midnight)", screen.Theme.Default);
        Assert.Equal("(default: off)", screen.Dashboard.ToolCallsDefault);
        Assert.Equal("(default: Automatic)", screen.Dashboard.IconsDefault);
        Assert.Equal("(default: none)", screen.Overseer.Defaults[0]);
        Assert.All(screen.Overseer.Defaults.Skip(1), note => Assert.Equal("", note));
        Assert.Contains(screen.KeysPage.Rows, row => row.Contains("(default: Ctrl+E)", StringComparison.Ordinal));
    }

    [Fact]
    public void Ctrl_PgDn_and_Ctrl_PgUp_change_page_and_wrap_round()
    {
        using var screen = Open();

        Press(screen, Key.PageDown.WithCtrl);
        Assert.Equal(screen.KeysPage, screen.Dialog.Page);
        Press(screen, Key.PageUp.WithCtrl);
        Press(screen, Key.PageUp.WithCtrl);
        Assert.Equal(screen.Overseer, screen.Dialog.Page);
        Press(screen, Key.PageDown.WithCtrl);
        Assert.Equal(screen.Theme, screen.Dialog.Page);
    }

    [Fact]
    public void Picking_a_page_in_the_list_opens_it()
    {
        using var screen = Open();

        screen.Dialog.PageList.SelectedItem = 2;

        Assert.Equal(screen.Dashboard, screen.Dialog.Page);
        Assert.True(screen.Dashboard.Visible);
        Assert.False(screen.Theme.Visible);
    }

    [Fact]
    public void Esc_puts_back_every_page_and_stores_nothing()
    {
        var previewed = new List<string>();
        var commands = Registry();
        using var screen = Open(out var icons, commands: commands, keys: Settings.KeyOverrides(commands), apply: previewed.Add);
        ChangeEverything(screen);

        Press(screen, Key.Esc);

        Assert.False(screen.Confirmed);
        Assert.Equal([BundledThemes.Daylight, BundledThemes.Midnight], previewed);
        Assert.Equal(CheckState.UnChecked, screen.Dashboard.ToolCalls.Value);
        Assert.Equal(IconStyle.Auto, icons.Current);
        Assert.All(screen.Overseer.Fields, limit => Assert.Equal("", limit.Field.Text));
        Assert.False(screen.KeysPage.IsChanged);
        Assert.Equal("Ctrl+E", commands.KeysFor("commands"));
        Assert.False(File.Exists(Path.Combine(_configRoot, "dashboard.json")));
        Assert.All(PageNames(screen), name => Assert.DoesNotContain("•", name, StringComparison.Ordinal));
    }

    [Fact]
    public void Ctrl_Enter_keeps_every_page_for_next_time()
    {
        var commands = Registry();
        using var screen = Open(commands: commands, keys: Settings.KeyOverrides(commands));
        ChangeEverything(screen);

        Press(screen, Key.Enter.WithCtrl);

        Assert.True(screen.Confirmed);
        Assert.Equal(BundledThemes.Daylight, Settings.ReadTheme());
        Assert.True(Settings.ReadExpandToolCalls());
        Assert.Equal(IconStyle.NerdFont, Settings.ReadIcons());
        Assert.Equal(new Dictionary<string, string> { [OverseerBoard.Columns[0]] = "2d" }, Settings.ReadLimits());
        var next = Registry();
        Settings.KeyOverrides(next).Load();
        Assert.Equal("Ctrl+E, F4", next.KeysFor("commands"));
    }

    [Fact]
    public void Clicking_Keep_keeps_and_clicking_Cancel_does_not()
    {
        using var keep = Open();
        keep.Dialog.KeepButton.InvokeCommand(Command.Accept);
        Assert.True(keep.Confirmed);

        using var cancel = Open();
        cancel.Dialog.CancelButton.InvokeCommand(Command.Accept);
        Assert.False(cancel.Confirmed);
    }

    [Fact]
    public void The_Terminal_box_installs_nothing_until_Keep_and_Esc_leaves_the_terminal_as_it_was()
    {
        var cancelled = new TerminalPageTests.FakeKeys("kitty");
        using (var screen = Open(terminalKeys: cancelled))
        {
            screen.Terminal!.TurnedOn.Value = CheckState.Checked;
            Assert.Equal("Terminal •", PageNames(screen)[3]);
            Press(screen, Key.Esc);
        }
        Assert.Equal(MacKeysState.NotInstalled, cancelled.State);

        var kept = new TerminalPageTests.FakeKeys("kitty");
        using (var screen = Open(terminalKeys: kept))
        {
            screen.Terminal!.TurnedOn.Value = CheckState.Checked;
            Assert.Equal(MacKeysState.NotInstalled, kept.State);
            Press(screen, Key.Enter.WithCtrl);
        }
        Assert.Equal(MacKeysState.Installed, kept.State);
    }

    [Fact]
    public void A_limit_Overseer_cannot_read_is_refused_on_its_page()
    {
        using var screen = Open();
        screen.Theme.Themes.Value = BundledThemes.Names.ToList().IndexOf(BundledThemes.Daylight);
        var building = screen.Overseer.Fields.Single(limit => limit.Column == "Building").Field;
        building.Text = "two weeks";

        Press(screen, Key.Enter.WithCtrl);

        Assert.False(screen.Confirmed);
        Assert.Equal(screen.Overseer, screen.Dialog.Page);
        Assert.True(building.HasFocus);
        Assert.Contains("can't read the limit for Building", screen.Dialog.Alert.Message, StringComparison.Ordinal);
        Assert.Equal(BundledThemes.Default, Settings.ReadTheme());
    }

    [Fact]
    public void Enter_on_a_page_does_not_close_Settings()
    {
        using var screen = Open();
        screen.Dashboard.ToolCalls.SetFocus();

        screen.Dashboard.ToolCalls.NewKeyDownEvent(Key.Enter);

        Assert.False(screen.Confirmed);
        Assert.Null(screen.Dialog.Result);
    }

    [Fact]
    public void The_icon_rows_name_what_Automatic_decided_for_this_terminal()
    {
        using var screen = Open(auto: IconStyle.NerdFont);

        Assert.Equal(
            [
                "Automatic  this terminal: Nerd Font",
                $"Nerd Font  {Icons.Sample(IconStyle.NerdFont)}",
                $"Unicode    {Icons.Sample(IconStyle.Unicode)}",
            ],
            screen.Dashboard.IconStyles.Labels);
        Assert.Equal(DashboardPage.LegendText, screen.Dashboard.Legend.Text);
    }

    [Fact]
    public void Enter_on_a_keys_row_records_a_chord_and_Esc_while_recording_leaves_Settings_open()
    {
        using var screen = Open();

        Record(screen, "Commands", Key.G.WithCtrl);
        Assert.True(screen.KeysPage.IsRecording);
        Press(screen, Key.Esc);

        Assert.False(screen.KeysPage.IsRecording);
        Assert.False(screen.KeysPage.IsChanged);
        Assert.False(screen.Confirmed);
        Assert.Null(screen.Dialog.Result);
    }

    [Fact]
    public void Ctrl_G_opens_the_guide()
    {
        var opened = new List<string>();
        using var screen = Open(showGuide: opened.Add);

        Assert.True(screen.Route(Key.G.WithCtrl));

        Assert.Equal([GuideDialog.Teams], opened);
        Assert.False(screen.Confirmed);
    }

    [Fact]
    public void F1_lists_the_pages_keys_then_Settings_own()
    {
        using var screen = Open();
        var shown = new List<KeySheet>();
        screen.ShowHelp = shown.Add;
        (string, string)[] own =
        [
            ("Ctrl+Enter", "Keep"), ("Ctrl+PgDn/Ctrl+PgUp", "Next / previous page"), ("Ctrl+G", "Guide"),
            ("Tab/Shift+Tab", "Next / previous"), ("Esc", "Cancel"),
        ];

        Assert.True(screen.Route(Key.F1));
        Press(screen, Key.PageDown.WithCtrl);
        Assert.True(screen.Route(Key.F1));

        Assert.Equal(["Theme", "Keyboard Shortcuts"], shown.Select(sheet => sheet.Here!.Heading));
        Assert.Equal(own, DialogTests.Rows(shown[0]));
        Assert.Equal([("Enter", "Add a key"), ("Del", "Remove the key"), ("Ctrl+R", "Reset to defaults…"), ("Type", "Filter"), .. own],
            DialogTests.Rows(shown[1]));
    }

    private void ChangeEverything(SettingsScreen screen)
    {
        screen.Theme.Themes.Value = BundledThemes.Names.ToList().IndexOf(BundledThemes.Daylight);
        screen.Dashboard.ToolCalls.Value = CheckState.Checked;
        screen.Dashboard.IconStyles.Value = (int)IconStyle.NerdFont;
        screen.Overseer.Fields[0].Field.Text = "2d";
        Record(screen, "Commands", Key.F4, Key.Enter);
        Assert.All(PageNames(screen).Where(name => name != "Terminal"), name => Assert.EndsWith(" •", name, StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> PageNames(SettingsScreen screen) =>
        [.. Enumerable.Range(0, screen.Dialog.PageList.Source?.Count ?? 0)
            .Select(i => screen.Dialog.PageList.Source!.ToList()[i]?.ToString() ?? "")];

    /// <summary>The app hands each key to the screen's route before any view, as the dashboard does.</summary>
    private static void Press(SettingsScreen screen, Key key)
    {
        if (!screen.Route(key))
            screen.Dialog.NewKeyDownEvent(key);
    }

    private static void Record(SettingsScreen screen, string command, params Key[] keys)
    {
        screen.Dialog.Open(screen.Dialog.Pages.ToList().IndexOf(screen.KeysPage));
        screen.KeysPage.Filter.SetFocus();
        screen.KeysPage.Filter.Text = command;
        Press(screen, Key.Enter);
        foreach (var key in keys)
            Press(screen, key);
    }

    private static CommandRegistry Registry() => new CommandRegistry()
        .Register("commands", "Commands", () => { }, Key.E.WithCtrl)
        .Register("view.work", "Go to Work", () => { }, chord: "Ctrl+G W")
        .Register("quit", "Quit", () => { }, Key.Q.WithCtrl);

    private SettingsScreen Open(
        CommandRegistry? commands = null,
        IconStyle auto = IconStyle.Unicode,
        Action<string>? showGuide = null,
        ITerminalKeys? terminalKeys = null,
        KeyOverrides? keys = null) =>
        Open(out _, commands, auto, showGuide, terminalKeys, keys);

    private SettingsScreen Open(
        out IconSetting icons,
        CommandRegistry? commands = null,
        IconStyle auto = IconStyle.Unicode,
        Action<string>? showGuide = null,
        ITerminalKeys? terminalKeys = null,
        KeyOverrides? keys = null,
        Action<string>? apply = null)
    {
        var settings = Settings;
        var theme = new ThemeSetting(BundledThemes.Midnight, apply ?? (_ => { }), settings.WriteTheme);
        icons = new IconSetting(IconStyle.Auto, _ => { }, settings.WriteIcons);
        var screen = new SettingsScreen(theme, icons, settings, commands ?? Registry(), () => { }, auto, showGuide, terminalKeys, keys);
        screen.Dialog.BeginInit();
        screen.Dialog.EndInit();
        return screen;
    }
}
