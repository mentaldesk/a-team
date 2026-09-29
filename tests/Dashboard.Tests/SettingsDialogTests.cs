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
        using var dialog = Open(out _, out _);

        Assert.True(dialog.NewKeyDownEvent(Key.Enter.WithCtrl));
        Assert.True(dialog.Confirmed);
    }

    [Fact]
    public void Esc_closes_the_dialog_without_keeping_the_theme()
    {
        using var dialog = Open(out _, out _);

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void Enter_is_left_free_and_does_not_close_the_dialog()
    {
        using var dialog = Open(out _, out _);
        var asked = false;
        dialog.Accepting += (_, _) => asked = true;

        Themes(dialog).NewKeyDownEvent(Key.Enter);

        Assert.False(asked);
        Assert.Null(dialog.Result);
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void The_hint_rows_read_what_the_page_and_the_dialog_can_do_with_the_keys_that_do_it()
    {
        using var dialog = Laid(Registry(), 80, 24);

        Assert.Equal(["Enter rebind · Delete remove · Type to filter", "Ctrl+Enter keep · Esc cancel"], HintRows(dialog));
    }

    [Fact]
    public void The_pages_hints_line_up_with_the_page_and_the_dialogs_with_its_left_edge()
    {
        using var dialog = Laid(Registry(), 80, 24);

        Assert.Equal(dialog.Keys.Frame.X, Hint(dialog, "Enter rebind").Frame.X);
        Assert.Equal(dialog.Pages.Frame.X, Hint(dialog, "Ctrl+Enter keep").Frame.X);
        Assert.True(Hint(dialog, "Enter rebind").Frame.Y < Hint(dialog, "Ctrl+Enter keep").Frame.Y);
    }

    [Fact]
    public void Clicking_keep_confirms_the_dialog_and_clicking_cancel_does_not()
    {
        using var keep = Open(out _, out _);
        Hint(keep, "Ctrl+Enter keep").InvokeCommand(Command.Accept);
        Assert.True(keep.Confirmed);

        using var cancel = Open(out _, out _);
        Hint(cancel, "Esc cancel").InvokeCommand(Command.Accept);
        Assert.False(cancel.Confirmed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_tool_calls_box_opens_showing_what_is_stored(bool expand)
    {
        using var dialog = Open(out _, out _, expand);

        Assert.Equal(expand ? CheckState.Checked : CheckState.UnChecked, ToolCalls(dialog).Value);
        Assert.Equal(expand, dialog.ExpandToolCalls);
    }

    [Theory]
    [InlineData(IconStyle.Auto)]
    [InlineData(IconStyle.NerdFont)]
    [InlineData(IconStyle.Unicode)]
    public void The_icon_rows_open_on_the_style_that_is_stored(IconStyle style)
    {
        using var dialog = Open(out _, out _, iconStyle: style);

        Assert.Equal(
            [
                "Automatic  this terminal: Unicode",
                $"Nerd Font  {Icons.Sample(IconStyle.NerdFont)}",
                $"Unicode    {Icons.Sample(IconStyle.Unicode)}",
            ],
            IconStyles(dialog).Labels);
        Assert.Equal((int)style, IconStyles(dialog).Value);
    }

    [Theory]
    [InlineData(IconStyle.NerdFont, "Automatic  this terminal: Nerd Font")]
    [InlineData(IconStyle.Unicode, "Automatic  this terminal: Unicode")]
    public void The_Automatic_row_names_what_it_decided_for_the_terminal_you_are_in(IconStyle auto, string row)
    {
        using var dialog = Open(out _, out _, auto: auto);

        Assert.Equal(row, IconStyles(dialog).Labels![0]);
    }

    [Fact]
    public void A_legend_underneath_names_what_the_icon_columns_mean()
    {
        using var host = new View { Width = 80, Height = 24 };
        using var dialog = Open(out _, out _);
        OpenPage(dialog, "Dashboard");
        host.Add(dialog);

        host.Layout(new Size(80, 24));

        Assert.True(Legend(dialog).Visible);
        Assert.True(Legend(dialog).Frame.Y >= IconStyles(dialog).Frame.Bottom);
        Assert.Equal(IconStyles(dialog).Frame.X, Legend(dialog).Frame.X);
    }

    [Fact]
    public void Picking_an_icon_row_previews_it_behind_the_dialog()
    {
        var previewed = new List<IconStyle>();
        using var dialog = Open(out _, out var icons, apply: previewed.Add);

        IconStyles(dialog).Value = (int)IconStyle.NerdFont;

        Assert.Equal([IconStyle.NerdFont], previewed);
        Assert.Equal(IconStyle.NerdFont, icons.Current);
    }

    [Fact]
    public void Confirming_keeps_the_icon_row_that_was_left_picked()
    {
        var settings = new DashboardSettings(_configRoot);
        using var dialog = Open(out var theme, out var icons);
        IconStyles(dialog).Value = (int)IconStyle.NerdFont;
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        dialog.Store(theme, icons, settings);

        Assert.Equal(IconStyle.NerdFont, settings.ReadIcons());
    }

    [Fact]
    public void Esc_puts_back_the_icon_style_that_was_in_effect_and_stores_nothing()
    {
        var settings = new DashboardSettings(_configRoot);
        var previewed = new List<IconStyle>();
        using var dialog = Open(out var theme, out var icons, apply: previewed.Add);
        IconStyles(dialog).Value = (int)IconStyle.NerdFont;
        dialog.NewKeyDownEvent(Key.Esc);

        dialog.Store(theme, icons, settings);

        Assert.Equal([IconStyle.NerdFont, IconStyle.Auto], previewed);
        Assert.Equal(IconStyle.Auto, settings.ReadIcons());
    }

    [Fact]
    public void Confirming_stores_the_tool_calls_box_as_it_was_left()
    {
        var settings = new DashboardSettings(_configRoot);
        using var dialog = Open(out var theme, out var icons);
        ToolCalls(dialog).InvokeCommand(Command.Activate);
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        dialog.Store(theme, icons, settings);

        Assert.True(settings.ReadExpandToolCalls());
    }

    [Fact]
    public void Cancelling_leaves_the_stored_tool_calls_setting_alone()
    {
        var settings = new DashboardSettings(_configRoot);
        settings.WriteExpandToolCalls(true);
        using var dialog = Open(out var theme, out var icons, expand: true);
        ToolCalls(dialog).InvokeCommand(Command.Activate);
        dialog.NewKeyDownEvent(Key.Esc);

        dialog.Store(theme, icons, settings);

        Assert.True(settings.ReadExpandToolCalls());
    }

    [Fact]
    public void Confirming_stores_the_tool_calls_setting_without_losing_the_theme()
    {
        var settings = new DashboardSettings(_configRoot);
        using var dialog = Open(out var theme, out var icons, keep: settings.WriteTheme);
        theme.Preview(BundledThemes.Daylight);
        ToolCalls(dialog).InvokeCommand(Command.Activate);
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        dialog.Store(theme, icons, settings);

        Assert.Equal(BundledThemes.Daylight, settings.ReadTheme());
        Assert.True(settings.ReadExpandToolCalls());
    }

    [Fact]
    public void Space_on_the_tool_calls_box_turns_it_on_without_closing_the_dialog()
    {
        using var dialog = Open(out _, out _);
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
        using var dialog = Open(out _, out _);
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
        using var dialog = Open(out _, out _);

        Assert.True(dialog.Pages.HasFocus);
    }

    [Fact]
    public void The_page_list_names_a_page_per_group_of_settings()
    {
        using var dialog = Open(out _, out _);

        Assert.Equal(["Theme", "Keyboard Shortcuts", "Dashboard"], PageNames(dialog));
    }

    [Fact]
    public void Only_the_page_the_list_is_on_is_showing()
    {
        using var dialog = Open(out _, out _);

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
        using var dialog = Open(out _, out _);
        OpenPage(dialog, "Dashboard");

        dialog.AdvanceFocus(NavigationDirection.Forward, TabBehavior.TabStop);

        Assert.True(ToolCalls(dialog).HasFocus);
    }

    [Fact]
    public void Only_the_keys_page_has_hints_of_its_own()
    {
        using var dialog = Laid(Registry(), 80, 24);

        OpenPage(dialog, "Theme");
        Assert.Equal(["Ctrl+Enter keep · Esc cancel"], HintRows(dialog));

        OpenPage(dialog, "Keyboard Shortcuts");
        Assert.Equal(2, HintRows(dialog).Count);

        OpenPage(dialog, "Dashboard");
        Assert.Equal(["Ctrl+Enter keep · Esc cancel"], HintRows(dialog));
    }

    [Fact]
    public void The_keys_list_has_a_row_per_command_naming_the_key_it_runs_on()
    {
        using var dialog = Open(out _, out _);

        Assert.Equal(["Commands  Ctrl+E", "Settings  s", "Quit      "], dialog.Rows);
    }

    [Fact]
    public void The_keys_list_names_the_key_an_override_bound_rather_than_the_one_in_the_source()
    {
        var commands = Registry();
        commands.Apply([("commands", Key.F4)]);

        using var dialog = Open(out _, out _, commands: commands);

        Assert.Contains("Commands  F4", dialog.Rows);
    }

    [Fact]
    public void Enter_on_a_row_asks_for_the_key_to_run_it()
    {
        using var dialog = Open(out _, out _);
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
        using var dialog = Open(out var theme, out var icons, commands: commands);
        Rebind(dialog, 0, Key.F4);
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        dialog.Store(theme, icons, settings);

        Assert.Equal([("commands", Key.F4)], settings.ReadKeys());
        Assert.Equal(Key.F4, commands.Registered.Single(command => command.Id == "commands").Key);
    }

    [Fact]
    public void Esc_while_capturing_leaves_the_row_as_it_was_and_the_dialog_open()
    {
        var settings = new DashboardSettings(_configRoot);
        var commands = Registry();
        using var dialog = Open(out var theme, out var icons, commands: commands);
        Rebind(dialog, 0, Key.Esc);

        Assert.Equal("Commands  Ctrl+E", Showing(dialog).First());
        Assert.False(dialog.Confirmed);
        Assert.Null(dialog.Result);

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);
        dialog.Store(theme, icons, settings);

        Assert.Empty(settings.ReadKeys());
        Assert.Equal(Key.E.WithCtrl, commands.Registered.Single(command => command.Id == "commands").Key);
    }

    [Fact]
    public void Esc_on_the_dialog_leaves_every_key_where_it_was()
    {
        var settings = new DashboardSettings(_configRoot);
        var commands = Registry();
        using var dialog = Open(out var theme, out var icons, commands: commands);
        Rebind(dialog, 0, Key.F4);
        dialog.NewKeyDownEvent(Key.Esc);

        dialog.Store(theme, icons, settings);

        Assert.Empty(settings.ReadKeys());
        Assert.Equal(Key.E.WithCtrl, commands.Registered.Single(command => command.Id == "commands").Key);
    }

    [Fact]
    public void A_key_another_command_holds_is_refused_by_name_and_leaves_the_row_alone()
    {
        using var dialog = Open(out _, out _);

        Rebind(dialog, 0, new Key('s'));

        Assert.Equal("s already runs Settings.", dialog.Message.Says);
        Assert.Equal("Commands  Ctrl+E", Showing(dialog).First());
        Assert.Empty(dialog.Changed);
        Assert.True(dialog.Keys.HasFocus);
    }

    [Fact]
    public void Delete_on_a_row_leaves_its_command_with_no_key_once_kept()
    {
        var settings = new DashboardSettings(_configRoot);
        var commands = Registry();
        using var dialog = Open(out var theme, out var icons, commands: commands);
        OpenPage(dialog, "Keyboard Shortcuts");
        dialog.Keys.SetFocus();

        dialog.NewKeyDownEvent(Key.Delete);

        Assert.Equal("Commands  ", Showing(dialog).First());
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);
        dialog.Store(theme, icons, settings);
        Assert.Equal([("commands", Key.Empty)], settings.ReadKeys());
        Assert.Equal(Key.Empty, commands.KeyFor("commands"));
    }

    [Fact]
    public void Typing_on_the_keys_list_filters_it_and_backspace_takes_the_filter_back()
    {
        using var dialog = Open(out _, out _);
        OpenPage(dialog, "Keyboard Shortcuts");
        dialog.Keys.SetFocus();

        dialog.NewKeyDownEvent(new Key('q'));

        Assert.Equal("q", dialog.Filter.Text);
        Assert.Equal(["Quit      "], dialog.Rows);
        Assert.True(dialog.Keys.HasFocus);

        dialog.NewKeyDownEvent(Key.Backspace);

        Assert.Equal(3, dialog.Rows.Count);
    }

    [Fact]
    public void The_filter_matches_a_key_as_well_as_a_command()
    {
        using var dialog = Open(out _, out _);

        dialog.Filter.Text = "ctrl+e";

        Assert.Equal(["Commands  Ctrl+E"], dialog.Rows);
    }

    [Fact]
    public void Rebinding_a_filtered_row_rebinds_the_command_it_shows()
    {
        using var dialog = Open(out _, out _);
        dialog.Filter.Text = "quit";

        Rebind(dialog, 0, Key.F4);

        Assert.Equal([("quit", Key.F4)], dialog.Changed);
        Assert.Equal(["Quit      F4"], dialog.Rows);
    }

    [Fact]
    public void Down_from_the_filter_moves_into_the_keys_list()
    {
        using var dialog = Open(out _, out _);
        OpenPage(dialog, "Keyboard Shortcuts");
        dialog.Filter.SetFocus();

        dialog.NewKeyDownEvent(Key.CursorDown);

        Assert.True(dialog.Keys.HasFocus);
    }

    [Fact]
    public void Every_other_row_but_the_selected_one_is_striped()
    {
        var normal = new Terminal.Gui.Drawing.Attribute(Terminal.Gui.Drawing.Color.White, Terminal.Gui.Drawing.Color.Black);

        Assert.Null(SettingsDialog.Stripe(0, null, normal));
        Assert.Null(SettingsDialog.Stripe(1, 1, normal));
        Assert.Null(SettingsDialog.Stripe(2, null, normal));
        var striped = SettingsDialog.Stripe(1, 0, normal);
        Assert.NotNull(striped);
        Assert.NotEqual(normal.Background, striped.Value.Background);
        Assert.Equal(normal.Foreground, striped.Value.Foreground);
    }

    [Fact]
    public void Saving_a_key_leaves_the_theme_and_the_tool_calls_setting_as_they_were()
    {
        var settings = new DashboardSettings(_configRoot);
        using var dialog = Open(out var theme, out var icons, expand: true, keep: settings.WriteTheme);
        theme.Preview(BundledThemes.Daylight);
        Rebind(dialog, 0, Key.F4);
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        dialog.Store(theme, icons, settings);

        Assert.Equal([("commands", Key.F4)], settings.ReadKeys());
        Assert.Equal(BundledThemes.Daylight, settings.ReadTheme());
        Assert.True(settings.ReadExpandToolCalls());
    }

    [Theory]
    [InlineData(80, 24, "Theme")]
    [InlineData(80, 24, "Keyboard Shortcuts")]
    [InlineData(80, 24, "Dashboard")]
    [InlineData(200, 60, "Theme")]
    [InlineData(200, 60, "Keyboard Shortcuts")]
    [InlineData(200, 60, "Dashboard")]
    public void The_dialog_fills_the_window_but_the_status_bar_on_every_page(int width, int height, string page)
    {
        using var host = new View { Width = width, Height = height };
        using var dialog = Open(out _, out _);
        OpenPage(dialog, page);
        host.Add(dialog);

        host.Layout(new Size(width, height));

        Assert.Equal(new Rectangle(0, 0, width, height - 1), dialog.Frame);
    }

    [Fact]
    public void Resizing_the_window_resizes_the_dialog_with_it()
    {
        using var host = new View { Width = 80, Height = 24 };
        using var dialog = Open(out _, out _);
        host.Add(dialog);
        host.Layout(new Size(80, 24));

        host.Width = 120;
        host.Height = 40;
        host.Layout(new Size(120, 40));

        Assert.Equal(new Rectangle(0, 0, 120, 39), dialog.Frame);
    }

    [Fact]
    public void The_keys_list_shows_a_scroll_bar_only_when_its_rows_do_not_fit()
    {
        using var tall = Laid(Registry(), 80, 24);
        Assert.False(tall.Keys.VerticalScrollBar.Visible);

        using var @short = Laid(Many(40), 80, 24);
        Assert.True(@short.Keys.VerticalScrollBar.Visible);
    }

    [Fact]
    public void Moving_down_past_the_last_row_showing_scrolls_the_keys_list()
    {
        using var dialog = Laid(Many(40), 80, 24);
        dialog.Keys.SetFocus();
        var showing = dialog.Keys.Viewport.Height;

        for (var row = 0; row < showing + 5; row++)
            dialog.Keys.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal(showing + 5, dialog.Keys.Value);
        Assert.InRange(dialog.Keys.Value!.Value, dialog.Keys.Viewport.Y, dialog.Keys.Viewport.Bottom - 1);
    }

    [Fact]
    public void Rebinding_a_row_near_the_bottom_keeps_it_in_view()
    {
        using var dialog = Laid(Many(40), 80, 24);
        dialog.Keys.SetFocus();
        dialog.Keys.Value = 38;
        dialog.Keys.EnsureSelectedItemVisible();

        dialog.NewKeyDownEvent(Key.Enter);
        dialog.Layout();

        Assert.EndsWith("Press a key…", Showing(dialog)[38]);
        Assert.InRange(38, dialog.Keys.Viewport.Y, dialog.Keys.Viewport.Bottom - 1);
    }

    [Fact]
    public void A_refused_key_shows_its_message_on_a_short_terminal_with_the_list_and_hints_still_in_view()
    {
        using var dialog = Laid(Many(40), 80, 12);
        dialog.Keys.SetFocus();
        dialog.Keys.Value = 30;
        dialog.NewKeyDownEvent(Key.Enter);
        dialog.NewKeyDownEvent(Key.F2);
        dialog.Layout();

        Assert.Equal("F2 already runs Command 1.", dialog.Message.Says);
        var hint = Hint(dialog, "Esc cancel");
        Assert.True(dialog.Keys.Frame.Height >= 1);
        Assert.True(dialog.Keys.Frame.Bottom <= hint.Frame.Y, $"{dialog.Keys.Frame} runs into {hint.Frame}");
        Assert.True(hint.Frame.Y < dialog.Message.Frame.Y, $"{hint.Frame} runs into {dialog.Message.Frame}");
        Assert.True(dialog.Viewport.Contains(dialog.Message.Frame), $"{dialog.Message.Frame} overhangs {dialog.Viewport}");
        Assert.InRange(30, dialog.Keys.Viewport.Y, dialog.Keys.Viewport.Bottom - 1);
    }

    [Fact]
    public void The_Dashboard_pages_rows_all_fit_an_eighty_column_terminal()
    {
        using var host = new View { Width = 80, Height = 24 };
        using var dialog = Open(out _, out _);
        OpenPage(dialog, "Dashboard");
        host.Add(dialog);

        host.Layout(new Size(80, 24));

        Assert.All(
            new View[] { ToolCalls(dialog), IconStyles(dialog), Legend(dialog) },
            view => Assert.True(
                dialog.Viewport.Contains(view.Frame), $"{view.GetType().Name} {view.Frame} overhangs {dialog.Viewport}"));
    }

    private SettingsDialog Laid(CommandRegistry commands, int width, int height)
    {
        var host = new View { Width = width, Height = height, CanFocus = true };
        var dialog = Open(out _, out _, commands: commands);
        OpenPage(dialog, "Keyboard Shortcuts");
        host.Add(dialog);
        host.Layout(new Size(width, height));
        return dialog;
    }

    private static CommandRegistry Many(int count)
    {
        var commands = new CommandRegistry();
        for (var index = 0; index < count; index++)
            commands.Register($"command{index}", $"Command {index}", () => { }, index == 1 ? Key.F2 : null);
        return commands;
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

    private static OptionSelector Themes(SettingsDialog dialog) =>
        dialog.SubViews.OfType<OptionSelector>().First(selector => selector.Labels!.Contains(BundledThemes.Midnight));

    private static Label Legend(SettingsDialog dialog) =>
        dialog.SubViews.OfType<Label>().Single(label => label.Text == "run  idle  paused  ok  error  here  tool");

    private static OptionSelector IconStyles(SettingsDialog dialog) =>
        dialog.SubViews.OfType<OptionSelector>()
            .First(selector => selector.Labels!.Any(label => label.StartsWith("Automatic", StringComparison.Ordinal)));

    private static CheckBox ToolCalls(SettingsDialog dialog) =>
        dialog.SubViews.OfType<CheckBox>().Single(box => box.Text == "Show tool calls in full");

    private static void OpenPage(SettingsDialog dialog, string name)
    {
        dialog.Pages.Value = Enumerable.Range(0, dialog.Pages.Source!.Count)
            .First(row => dialog.Pages.Source!.ToList()[row]?.ToString() == name);
    }

    private static Button Hint(SettingsDialog dialog, string text) =>
        dialog.SubViews.OfType<Button>().Single(hint => hint.Text == text);

    private static IReadOnlyList<string> HintRows(SettingsDialog dialog)
    {
        dialog.Layout();
        return [.. dialog.SubViews
            .Where(view => view is Button { NoDecorations: true } || view.Text == " · ")
            .GroupBy(view => view.Frame.Y)
            .OrderBy(row => row.Key)
            .Select(row => string.Concat(row.OrderBy(view => view.Frame.X).Select(view => view.Text)))];
    }

    private static CommandRegistry Registry() => new CommandRegistry()
        .Register("commands", "Commands", () => { }, Key.E.WithCtrl)
        .Register("settings", "Settings", () => { }, new Key('s'))
        .Register("quit", "Quit", () => { });

    private SettingsDialog Open(
        out ThemeSetting theme,
        out IconSetting icons,
        bool expand = false,
        IconStyle iconStyle = IconStyle.Auto,
        Action<string>? keep = null,
        Action<IconStyle>? apply = null,
        CommandRegistry? commands = null,
        IconStyle auto = IconStyle.Unicode)
    {
        theme = new ThemeSetting(BundledThemes.Midnight, _ => { }, keep ?? (_ => { }));
        icons = new IconSetting(iconStyle, apply ?? (_ => { }), new DashboardSettings(_configRoot).WriteIcons);
        var dialog = new SettingsDialog(theme, icons, expand, commands ?? Registry(), () => { }, auto);
        dialog.SetFocus();
        return dialog;
    }
}
