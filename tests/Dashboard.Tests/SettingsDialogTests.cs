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

        Assert.Equal(["Theme", "Keyboard Shortcuts", "Dashboard", "Teams"], PageNames(dialog));
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

    [Fact]
    public void The_Teams_page_lists_each_team_with_its_repo_and_whether_it_is_working()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        WriteTeam("beta", """{"repo": "mentaldesk/beta-long", "dispatch": {"enabled": false}}""");
        WriteTeam("gamma", "{\n  \"repo\": \"mentaldesk/gamma\",\n}\n");
        using var dialog = Open(out _, out _, page: "Teams");

        Assert.Equal(
            [
                "alpha  mentaldesk/alpha      working",
                "beta   mentaldesk/beta-long  paused",
                "gamma                        can't read this file",
            ],
            TeamRows(dialog));
        Assert.Equal(0, dialog.Teams.Value);
        Assert.True(dialog.Teams.HasFocus);
    }

    [Fact]
    public void Selecting_a_team_whose_file_is_broken_says_which_line_and_why()
    {
        WriteTeam("alpha", """{"dispatch": {"enabled": true}}""");
        WriteTeam("gamma", "{\n  \"repo\": \"mentaldesk/gamma\",\n}\n");
        using var dialog = Open(out _, out _, page: "Teams");
        Assert.Equal("", dialog.Message.Says);

        dialog.Teams.Value = 1;

        Assert.StartsWith("gamma.json, line 3: ", dialog.Message.Says);
        dialog.Teams.Value = 0;
        Assert.Equal("", dialog.Message.Says);
    }

    [Fact]
    public void P_pauses_a_working_team_and_starts_a_paused_one_and_the_row_and_hint_follow()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        using var dialog = Open(out _, out _, page: "Teams");
        Assert.Equal(["p pause · Enter edit · x remove · n new", "Ctrl+Enter keep · Esc cancel"], HintRows(dialog));

        Assert.True(dialog.Teams.NewKeyDownEvent(new Key('p')));

        Assert.Equal(["alpha  mentaldesk/alpha  paused"], TeamRows(dialog));
        Assert.True(new TeamConfigs(_configRoot).IsPaused("alpha"));
        Assert.Equal(["p resume · Enter edit · x remove · n new", "Ctrl+Enter keep · Esc cancel"], HintRows(dialog));

        Hint(dialog, "p resume").InvokeCommand(Command.Accept);

        Assert.Equal(["alpha  mentaldesk/alpha  working"], TeamRows(dialog));
        Assert.False(new TeamConfigs(_configRoot).IsPaused("alpha"));
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void P_on_a_team_whose_file_is_broken_leaves_it_alone_and_says_why()
    {
        const string broken = "{\"dispatch\": {\"enabled\": true,}}";
        WriteTeam("gamma", broken);
        using var dialog = Open(out _, out _, page: "Teams");

        dialog.Teams.NewKeyDownEvent(new Key('p'));

        Assert.Equal(broken, File.ReadAllText(Path.Combine(_configRoot, "teams", "gamma.json")));
        Assert.Equal("Fix gamma.json before starting or pausing gamma: a-team can't read it.", dialog.Message.Says);
    }

    [Fact]
    public void Enter_on_the_Teams_list_does_not_close_the_dialog()
    {
        WriteTeam("alpha", """{"dispatch": {"enabled": true}}""");
        using var dialog = Open(out _, out _, page: "Teams");

        dialog.Teams.NewKeyDownEvent(Key.Enter);

        Assert.Null(dialog.Result);
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void Enter_on_a_team_opens_its_form_and_saving_it_updates_the_file_and_the_row()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "app": {"id": 1}, "dispatch": {"enabled": true, "hold": ["dev"]}}""");
        using var dialog = Open(out _, out _, page: "Teams");
        string? opened = null;
        dialog.EditTeam = (team, settings, save) =>
        {
            opened = team;
            var after = settings with { Repo = "mentaldesk/beta", Working = false };
            return save(after) is null ? after : null;
        };

        dialog.Teams.NewKeyDownEvent(Key.Enter);

        Assert.Equal("alpha", opened);
        Assert.Equal(["alpha  mentaldesk/beta  paused"], TeamRows(dialog));
        Assert.Equal(
            """{"repo": "mentaldesk/beta", "app": {"id": 1}, "dispatch": {"enabled": false, "hold": ["dev"]}}""",
            File.ReadAllText(Path.Combine(_configRoot, "teams", "alpha.json")));
        Assert.Null(dialog.Result);
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void A_save_that_leaves_the_workdir_missing_warns_on_the_Teams_page()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "workdir": "/nowhere/alpha"}""");
        using var dialog = Open(out _, out _, page: "Teams");
        dialog.EditTeam = (_, settings, _) => settings;

        dialog.Teams.NewKeyDownEvent(Key.Enter);

        Assert.Equal("/nowhere/alpha isn't there, so the agents would have nothing to work in.", dialog.Message.Says);
    }

    [Fact]
    public void Enter_on_a_team_whose_file_is_broken_opens_nothing_and_says_why()
    {
        WriteTeam("gamma", "{\n  \"repo\": \"o/r\",\n}");
        using var dialog = Open(out _, out _, page: "Teams");
        var opened = false;
        dialog.EditTeam = (_, _, _) =>
        {
            opened = true;
            return null;
        };

        dialog.Teams.NewKeyDownEvent(Key.Enter);

        Assert.False(opened);
        Assert.StartsWith("gamma.json, line 3: ", dialog.Message.Says);
    }

    [Fact]
    public void A_save_to_a_read_only_file_leaves_the_form_open_and_filled_in()
    {
        if (OperatingSystem.IsWindows())
            return;
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "project": {"owner": "mentaldesk", "number": 1}, "vision": "v", "workdir": "w"}""");
        var path = Path.Combine(_configRoot, "teams", "alpha.json");
        File.SetUnixFileMode(path, UnixFileMode.UserRead);
        using var dialog = Open(out _, out _, page: "Teams");
        TeamForm? shown = null;
        dialog.EditTeam = (team, settings, save) =>
        {
            shown = new TeamForm(team, settings, save);
            shown.Repo.Text = "mentaldesk/beta";
            shown.Save();
            return shown.Saved;
        };

        dialog.Teams.NewKeyDownEvent(Key.Enter);

        using var form = shown!;
        Assert.Null(form.Saved);
        Assert.StartsWith("Couldn't save alpha: ", form.Message.Says);
        Assert.Equal("mentaldesk/beta", form.Repo.Text);
        Assert.Equal(["alpha  mentaldesk/alpha  paused"], TeamRows(dialog));
    }

    [Fact]
    public void The_Teams_page_hints_offer_Enter_edit()
    {
        WriteTeam("alpha", """{"dispatch": {"enabled": true}}""");
        using var dialog = Open(out _, out _, page: "Teams");
        var opened = false;
        dialog.EditTeam = (_, _, _) =>
        {
            opened = true;
            return null;
        };

        Hint(dialog, "Enter edit").InvokeCommand(Command.Accept);

        Assert.True(opened);
    }

    [Fact]
    public void N_on_the_Teams_page_creates_a_team_selects_it_and_says_where_it_stands()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        using var dialog = Open(out _, out _, page: "Teams");
        dialog.CreateTeam = (_, create) =>
        {
            WriteTeam("fretty", """{"repo": "mentaldesk/fretty"}""");
            return "fretty";
        };
        string? followed = null;
        dialog.FollowTeam = team =>
        {
            followed = team;
            return ($"{team} is paused. Press p when you want it to start.", Terminal.Gui.Drawing.Schemes.Base);
        };

        Assert.True(dialog.Teams.NewKeyDownEvent(new Key('n')));

        Assert.Equal("fretty", followed);
        Assert.Equal(["alpha   mentaldesk/alpha   working", "fretty  mentaldesk/fretty  paused"], TeamRows(dialog));
        Assert.Equal(1, dialog.Teams.Value);
        Assert.Equal("fretty is paused. Press p when you want it to start.", dialog.Message.Says);
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void X_asks_first_and_Esc_leaves_the_team_as_it_was()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        using var dialog = Open(out _, out _, page: "Teams");
        (string, string, string)? asked = null;
        dialog.ConfirmRemove = (team, repo, kept) =>
        {
            asked = (team, repo, kept);
            return false;
        };

        Assert.True(dialog.Teams.NewKeyDownEvent(new Key('x')));

        Assert.Equal(("alpha", "mentaldesk/alpha", "alpha.json.removed"), asked);
        Assert.True(File.Exists(Path.Combine(_configRoot, "teams", "alpha.json")));
        Assert.Equal(["alpha  mentaldesk/alpha  working"], TeamRows(dialog));
        Assert.Empty(dialog.RemovedTeams);
    }

    [Fact]
    public void Removing_a_team_takes_it_off_the_page_and_keeps_its_file_as_removed()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha"}""");
        WriteTeam("beta", """{"repo": "mentaldesk/beta"}""");
        using var dialog = Open(out _, out _, page: "Teams");
        dialog.ConfirmRemove = (_, _, _) => true;
        dialog.Teams.Value = 1;

        Hint(dialog, "x remove").InvokeCommand(Command.Accept);

        Assert.Equal(["alpha  mentaldesk/alpha  paused"], TeamRows(dialog));
        Assert.Equal(0, dialog.Teams.Value);
        Assert.Equal(["beta"], dialog.RemovedTeams);
        Assert.True(File.Exists(Path.Combine(_configRoot, "teams", "beta.json.removed")));
        Assert.Equal("Removed beta. Rename beta.json.removed back to beta.json to bring it back.", dialog.Message.Says);
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void A_new_team_form_that_was_cancelled_changes_nothing()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha"}""");
        using var dialog = Open(out _, out _, page: "Teams");
        dialog.CreateTeam = (_, _) => null;
        var followed = false;
        dialog.FollowTeam = _ =>
        {
            followed = true;
            return null;
        };

        Hint(dialog, "n new").InvokeCommand(Command.Accept);

        Assert.False(followed);
        Assert.Equal(["alpha  mentaldesk/alpha  paused"], TeamRows(dialog));
    }

    [Fact]
    public void Back_past_the_first_step_reopens_the_form_on_the_team_and_follows_it_again()
    {
        using var dialog = Open(out _, out _, page: "Teams");
        List<string?> opened = [];
        dialog.CreateTeam = (again, create) =>
        {
            opened.Add(again);
            if (again is null)
                WriteTeam("fretty", """{"repo": "mentaldesk/fretty"}""");
            return "fretty";
        };
        var follows = 0;
        dialog.FollowTeam = team => ++follows == 1 ? null : ($"{team} is working.", Terminal.Gui.Drawing.Schemes.Base);

        dialog.NewTeam();

        Assert.Equal([null, "fretty"], opened);
        Assert.Equal("fretty is working.", dialog.Message.Says);
    }

    [Fact]
    public void Cancelling_the_form_Back_reopened_cancels_the_new_team()
    {
        using var dialog = Open(out _, out _, page: "Teams");
        dialog.CreateTeam = (again, create) =>
        {
            if (again is not null)
                return null;
            WriteTeam("fretty", """{"repo": "mentaldesk/fretty"}""");
            return "fretty";
        };
        dialog.FollowTeam = _ => null;

        dialog.NewTeam();

        Assert.Empty(TeamRows(dialog));
        Assert.Equal("fretty is cancelled. Its clone, App and project are still there.", dialog.Message.Says);
    }

    [Fact]
    public void Removing_the_last_team_leaves_an_empty_list_with_no_team_hints()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        using var dialog = Open(out _, out _, page: "Teams");
        dialog.ConfirmRemove = (_, _, _) => true;
        Assert.Equal(["p pause · Enter edit · x remove · n new", "Ctrl+Enter keep · Esc cancel"], HintRows(dialog));

        dialog.Teams.NewKeyDownEvent(new Key('x'));

        Assert.Empty(TeamRows(dialog));
        Assert.Null(dialog.Teams.Value);
        Assert.Equal(["n new", "Ctrl+Enter keep · Esc cancel"], HintRows(dialog));
    }

    private void WriteTeam(string team, string config)
    {
        var teams = Path.Combine(_configRoot, "teams");
        Directory.CreateDirectory(teams);
        File.WriteAllText(Path.Combine(teams, $"{team}.json"), config);
    }

    private static IReadOnlyList<string> TeamRows(SettingsDialog dialog) =>
        [.. Enumerable.Range(0, dialog.Teams.Source?.Count ?? 0).Select(i => dialog.Teams.Source!.ToList()[i]?.ToString() ?? "")];

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
        IconStyle auto = IconStyle.Unicode,
        string? page = null)
    {
        theme = new ThemeSetting(BundledThemes.Midnight, _ => { }, keep ?? (_ => { }));
        icons = new IconSetting(iconStyle, apply ?? (_ => { }), new DashboardSettings(_configRoot).WriteIcons);
        var dialog = new SettingsDialog(
            theme, icons, expand, commands ?? Registry(), new TeamConfigs(_configRoot), () => { }, auto, page);
        dialog.SetFocus();
        return dialog;
    }
}
