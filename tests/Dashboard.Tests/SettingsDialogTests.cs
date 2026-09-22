using System.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public class SettingsDialogTests : IDisposable
{
    private readonly string _configRoot = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_configRoot))
            Directory.Delete(_configRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Ctrl_Enter_keeps_the_theme_picked_in_the_dialog()
    {
        using var dialog = Open(out _);

        Assert.True(dialog.NewKeyDownEvent(Key.Enter.WithCtrl));
        Assert.True(dialog.Confirmed);
    }

    [Fact]
    public void Esc_closes_the_dialog_without_keeping_the_theme()
    {
        using var dialog = Open(out _);

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void Enter_is_left_free_and_does_not_close_the_dialog()
    {
        using var dialog = Open(out _);
        var asked = false;
        dialog.Accepting += (_, _) => asked = true;

        Themes(dialog).NewKeyDownEvent(Key.Enter);

        Assert.False(asked);
        Assert.Null(dialog.Result);
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void The_hint_row_reads_what_the_dialog_can_do_with_the_keys_that_do_it()
    {
        using var dialog = Open(out _);
        OpenPage(dialog, "Keyboard Shortcuts");

        Assert.Equal("Enter rebind · Ctrl+Enter keep · Esc cancel", HintRow(dialog));
    }

    [Fact]
    public void Clicking_keep_confirms_the_dialog_and_clicking_cancel_does_not()
    {
        using var keep = Open(out _);
        Hint(keep, "Ctrl+Enter keep").InvokeCommand(Command.Accept);
        Assert.True(keep.Confirmed);

        using var cancel = Open(out _);
        Hint(cancel, "Esc cancel").InvokeCommand(Command.Accept);
        Assert.False(cancel.Confirmed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_tool_calls_box_opens_showing_what_is_stored(bool expand)
    {
        using var dialog = Open(out _, expand);

        Assert.Equal(expand ? CheckState.Checked : CheckState.UnChecked, ToolCalls(dialog).Value);
        Assert.Equal(expand, dialog.ExpandToolCalls);
    }

    [Fact]
    public void Confirming_stores_the_tool_calls_box_as_it_was_left()
    {
        var settings = new DashboardSettings(_configRoot);
        using var dialog = Open(out var theme);
        ToolCalls(dialog).InvokeCommand(Command.Activate);
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        dialog.Store(theme, settings);

        Assert.True(settings.ReadExpandToolCalls());
    }

    [Fact]
    public void Cancelling_leaves_the_stored_tool_calls_setting_alone()
    {
        var settings = new DashboardSettings(_configRoot);
        settings.WriteExpandToolCalls(true);
        using var dialog = Open(out var theme, expand: true);
        ToolCalls(dialog).InvokeCommand(Command.Activate);
        dialog.NewKeyDownEvent(Key.Esc);

        dialog.Store(theme, settings);

        Assert.True(settings.ReadExpandToolCalls());
    }

    [Fact]
    public void Confirming_stores_the_tool_calls_setting_without_losing_the_theme()
    {
        var settings = new DashboardSettings(_configRoot);
        using var dialog = Open(out var theme, keep: settings.WriteTheme);
        theme.Preview(BundledThemes.Daylight);
        ToolCalls(dialog).InvokeCommand(Command.Activate);
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        dialog.Store(theme, settings);

        Assert.Equal(BundledThemes.Daylight, settings.ReadTheme());
        Assert.True(settings.ReadExpandToolCalls());
    }

    [Fact]
    public void Space_on_the_tool_calls_box_turns_it_on_without_closing_the_dialog()
    {
        using var dialog = Open(out _);
        OpenPage(dialog, "Dashboard");
        var box = ToolCalls(dialog);
        box.SetFocus();

        box.NewKeyDownEvent(Key.Space);

        Assert.True(dialog.ExpandToolCalls);
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void Enter_on_the_tool_calls_box_does_not_close_the_dialog_either()
    {
        using var dialog = Open(out _);
        OpenPage(dialog, "Dashboard");
        var box = ToolCalls(dialog);
        box.SetFocus();

        box.NewKeyDownEvent(Key.Enter);

        Assert.Null(dialog.Result);
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void Focus_starts_on_the_page_list()
    {
        using var dialog = Open(out _);

        Assert.True(dialog.Pages.HasFocus);
    }

    [Fact]
    public void The_page_list_names_a_page_per_group_of_settings()
    {
        using var dialog = Open(out _);

        Assert.Equal(["Theme", "Keyboard Shortcuts", "Dashboard"], PageNames(dialog));
    }

    [Fact]
    public void Only_the_page_the_list_is_on_is_showing()
    {
        using var dialog = Open(out _);

        Assert.True(Themes(dialog).Visible);
        Assert.False(dialog.Keys.Visible);
        Assert.False(ToolCalls(dialog).Visible);

        OpenPage(dialog, "Keyboard Shortcuts");

        Assert.False(Themes(dialog).Visible);
        Assert.True(dialog.Keys.Visible);
        Assert.False(ToolCalls(dialog).Visible);

        OpenPage(dialog, "Dashboard");

        Assert.False(Themes(dialog).Visible);
        Assert.False(dialog.Keys.Visible);
        Assert.True(ToolCalls(dialog).Visible);
    }

    [Fact]
    public void Tab_moves_from_the_page_list_into_the_page_showing()
    {
        using var dialog = Open(out _);
        OpenPage(dialog, "Dashboard");

        dialog.AdvanceFocus(NavigationDirection.Forward, TabBehavior.TabStop);

        Assert.True(ToolCalls(dialog).HasFocus);
    }

    [Fact]
    public void The_hint_row_names_rebinding_only_on_the_keys_page()
    {
        using var dialog = Open(out _);

        Assert.Equal("Ctrl+Enter keep · Esc cancel", HintRow(dialog));

        OpenPage(dialog, "Keyboard Shortcuts");

        Assert.Equal("Enter rebind · Ctrl+Enter keep · Esc cancel", HintRow(dialog));

        OpenPage(dialog, "Dashboard");

        Assert.Equal("Ctrl+Enter keep · Esc cancel", HintRow(dialog));
    }

    [Fact]
    public void The_keys_list_has_a_row_per_command_naming_the_key_it_runs_on()
    {
        using var dialog = Open(out _);

        Assert.Equal(["Commands  Ctrl+E", "Settings  s", "Quit      "], dialog.Rows);
    }

    [Fact]
    public void The_keys_list_names_the_key_an_override_bound_rather_than_the_one_in_the_source()
    {
        var commands = Registry();
        commands.Apply([("commands", Key.F4)]);

        using var dialog = Open(out _, commands: commands);

        Assert.Contains("Commands  F4", dialog.Rows);
    }

    [Fact]
    public void Enter_on_a_row_asks_for_the_key_to_run_it()
    {
        using var dialog = Open(out _);
        OpenPage(dialog, "Keyboard Shortcuts");
        dialog.Keys.SetFocus();

        Assert.True(dialog.NewKeyDownEvent(Key.Enter));

        Assert.Equal("Commands  Press a key…", Showing(dialog).First());
        Assert.False(dialog.Confirmed);
        Assert.Null(dialog.Result);
    }

    [Fact]
    public void The_key_pressed_next_is_the_one_that_row_is_bound_to()
    {
        var settings = new DashboardSettings(_configRoot);
        var commands = Registry();
        using var dialog = Open(out var theme, commands: commands);
        Rebind(dialog, 0, Key.F4);
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        dialog.Store(theme, settings);

        Assert.Equal([("commands", Key.F4)], settings.ReadKeys());
        Assert.Equal(Key.F4, commands.Registered.Single(command => command.Id == "commands").Key);
    }

    [Fact]
    public void Esc_while_capturing_leaves_the_row_as_it_was_and_the_dialog_open()
    {
        var settings = new DashboardSettings(_configRoot);
        var commands = Registry();
        using var dialog = Open(out var theme, commands: commands);
        Rebind(dialog, 0, Key.Esc);

        Assert.Equal("Commands  Ctrl+E", Showing(dialog).First());
        Assert.False(dialog.Confirmed);
        Assert.Null(dialog.Result);

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);
        dialog.Store(theme, settings);

        Assert.Empty(settings.ReadKeys());
        Assert.Equal(Key.E.WithCtrl, commands.Registered.Single(command => command.Id == "commands").Key);
    }

    [Fact]
    public void Esc_on_the_dialog_leaves_every_key_where_it_was()
    {
        var settings = new DashboardSettings(_configRoot);
        var commands = Registry();
        using var dialog = Open(out var theme, commands: commands);
        Rebind(dialog, 0, Key.F4);
        dialog.NewKeyDownEvent(Key.Esc);

        dialog.Store(theme, settings);

        Assert.Empty(settings.ReadKeys());
        Assert.Equal(Key.E.WithCtrl, commands.Registered.Single(command => command.Id == "commands").Key);
    }

    [Fact]
    public void A_key_another_command_holds_is_refused_by_name_and_leaves_the_row_alone()
    {
        using var dialog = Open(out _);

        Rebind(dialog, 0, new Key('s'));

        Assert.Equal("s already runs Settings.", dialog.Message.Text);
        Assert.Equal("Commands  Ctrl+E", Showing(dialog).First());
        Assert.Empty(dialog.Changed);
        Assert.True(dialog.Keys.HasFocus);
    }

    [Fact]
    public void Saving_a_key_leaves_the_theme_and_the_tool_calls_setting_as_they_were()
    {
        var settings = new DashboardSettings(_configRoot);
        using var dialog = Open(out var theme, expand: true, keep: settings.WriteTheme);
        theme.Preview(BundledThemes.Daylight);
        Rebind(dialog, 0, Key.F4);
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        dialog.Store(theme, settings);

        Assert.Equal([("commands", Key.F4)], settings.ReadKeys());
        Assert.Equal(BundledThemes.Daylight, settings.ReadTheme());
        Assert.True(settings.ReadExpandToolCalls());
    }

    [Fact]
    public void The_dialog_stays_inside_a_small_terminal_and_scrolls_its_keys_instead()
    {
        using var host = new View { Width = 30, Height = 12 };
        using var dialog = Open(out _);
        OpenPage(dialog, "Keyboard Shortcuts");
        host.Add(dialog);

        host.Layout(new Size(30, 12));

        Assert.True(host.Viewport.Contains(dialog.Frame), $"{dialog.Frame} overhangs {host.Viewport}");
        Assert.True(dialog.Keys.Frame.Height >= 1);
    }

    private static void Rebind(SettingsDialog dialog, int row, Key key)
    {
        OpenPage(dialog, "Keyboard Shortcuts");
        dialog.Keys.SetFocus();
        dialog.Keys.Value = row;
        dialog.NewKeyDownEvent(Key.Enter);
        dialog.NewKeyDownEvent(key);
    }

    private static IReadOnlyList<string> PageNames(SettingsDialog dialog) =>
        [.. Enumerable.Range(0, dialog.Pages.Source?.Count ?? 0)
            .Select(i => dialog.Pages.Source!.ToList()[i]?.ToString() ?? "")];

    private static IReadOnlyList<string> Showing(SettingsDialog dialog) =>
        [.. Enumerable.Range(0, dialog.Keys.Source?.Count ?? 0).Select(i => dialog.Keys.Source!.ToList()[i]?.ToString() ?? "")];

    private static OptionSelector Themes(SettingsDialog dialog) => dialog.SubViews.OfType<OptionSelector>().Single();

    private static CheckBox ToolCalls(SettingsDialog dialog) =>
        dialog.SubViews.OfType<CheckBox>().Single(box => box.Text == "Show tool calls in full");

    private static void OpenPage(SettingsDialog dialog, string name)
    {
        dialog.Pages.Value = Enumerable.Range(0, dialog.Pages.Source!.Count)
            .First(row => dialog.Pages.Source!.ToList()[row]?.ToString() == name);
    }

    private static Button Hint(SettingsDialog dialog, string text) =>
        dialog.SubViews.OfType<Button>().Single(hint => hint.Text == text);

    private static string HintRow(SettingsDialog dialog) => string.Concat(dialog.SubViews
        .Where(view => view is Button { NoDecorations: true } || view.Text == " · ")
        .Select(view => view.Text));

    private static CommandRegistry Registry() => new CommandRegistry()
        .Register("commands", "Commands", () => { }, Key.E.WithCtrl)
        .Register("settings", "Settings", () => { }, new Key('s'))
        .Register("quit", "Quit", () => { });

    private static SettingsDialog Open(
        out ThemeSetting theme,
        bool expand = false,
        Action<string>? keep = null,
        CommandRegistry? commands = null)
    {
        theme = new ThemeSetting(BundledThemes.Midnight, _ => { }, keep ?? (_ => { }));
        var dialog = new SettingsDialog(theme, expand, commands ?? Registry(), () => { });
        dialog.SetFocus();
        return dialog;
    }
}
