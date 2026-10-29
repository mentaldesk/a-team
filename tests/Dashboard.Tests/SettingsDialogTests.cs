using MentalDesk.Tui.Keys;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
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
        OpenPage(dialog, SettingsDialog.TeamsPage);

        Assert.Equal(["n new", "Ctrl+Enter keep · Esc cancel · Ctrl+G guide"], HintRows(dialog));
    }

    [Fact]
    public void The_pages_hints_line_up_with_the_page_and_the_dialogs_with_its_left_edge()
    {
        using var dialog = Laid(Registry(), 80, 24);
        OpenPage(dialog, SettingsDialog.TeamsPage);
        dialog.Layout();

        Assert.Equal(dialog.KeysPage.Frame.X, Hint(dialog, "n new").Frame.X);
        Assert.Equal(dialog.Pages.Frame.X, Hint(dialog, "Ctrl+Enter keep").Frame.X);
        Assert.True(Hint(dialog, "n new").Frame.Y < Hint(dialog, "Ctrl+Enter keep").Frame.Y);
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

        Assert.Equal(["Theme", "Keyboard Shortcuts", "Dashboard", "Teams", "Overseer"], PageNames(dialog));
    }

    [Fact]
    public void The_Terminal_page_comes_after_Dashboard_and_shows_only_when_picked()
    {
        using var dialog = Open(out _, out _, terminalKeys: new TerminalPageTests.FakeKeys("kitty"));

        Assert.Equal(["Theme", "Keyboard Shortcuts", "Dashboard", "Terminal", "Teams", "Overseer"], PageNames(dialog));
        Assert.False(dialog.Terminal!.Visible);
        OpenPage(dialog, "Terminal");
        Assert.True(dialog.Terminal.Visible);
        Assert.False(Themes(dialog).Visible);
    }

    [Fact]
    public void What_the_Terminal_page_does_is_said_at_the_foot_of_the_dialog()
    {
        using var dialog = Open(out _, out _, terminalKeys: new TerminalPageTests.FakeKeys("kitty"));
        OpenPage(dialog, "Terminal");

        dialog.Terminal!.InstallButton.InvokeCommand(Command.Accept);

        Assert.Equal("Installed the Mac keys for kitty.", dialog.Message.Says);
    }

    [Fact]
    public void Only_the_page_the_list_is_on_is_showing()
    {
        using var dialog = Open(out _, out _);

        Assert.True(Themes(dialog).Visible);
        Assert.False(dialog.KeysPage.Visible);
        Assert.False(ToolCalls(dialog).Visible);

        OpenPage(dialog, "Keyboard Shortcuts");

        Assert.False(Themes(dialog).Visible);
        Assert.True(dialog.KeysPage.Visible);
        Assert.False(ToolCalls(dialog).Visible);

        OpenPage(dialog, "Dashboard");

        Assert.False(Themes(dialog).Visible);
        Assert.False(dialog.KeysPage.Visible);
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
    public void The_keys_page_leaves_its_hints_to_itself_and_the_dialog_has_only_its_own()
    {
        using var dialog = Laid(Registry(), 80, 24);

        Assert.Equal(["Ctrl+Enter keep · Esc cancel · Ctrl+G guide"], HintRows(dialog));
        Assert.Equal(KeysPage.BrowsingHint, dialog.KeysPage.Hint);
    }

    [Fact]
    public void The_keys_page_has_a_row_per_command_naming_its_keys_and_where_they_work()
    {
        using var dialog = Open(out _, out _);

        Assert.Contains(dialog.KeysPage.Rows, row => Has(row, "Commands", "Ctrl+E", KeysPage.Everywhere));
        Assert.Contains(dialog.KeysPage.Rows, row => Has(row, "Go to Work", "Ctrl+G W", KeysPage.Everywhere));
        Assert.Contains(dialog.KeysPage.Rows, row => Has(row, "Try", "T", "Work"));
    }

    [Fact]
    public void Enter_on_a_row_records_a_chord_a_key_at_a_time_and_Enter_keeps_it_as_another_key()
    {
        var commands = Registry();
        using var dialog = Open(out _, out _, commands: commands);

        Record(dialog, "Commands", Key.G.WithCtrl);

        Assert.True(dialog.KeysPage.IsRecording);
        Assert.Contains(dialog.KeysPage.Rows, row => row.Contains($"{KeysPage.Recording} Ctrl+G", StringComparison.Ordinal));

        Press(dialog, new Key('c'));
        Press(dialog, Key.Enter);

        Assert.False(dialog.KeysPage.IsRecording);
        Assert.Contains(dialog.KeysPage.Staged, binding => binding.CommandId == "commands" && binding.Display == "Ctrl+G C");
        Assert.Contains(dialog.KeysPage.Staged, binding => binding.CommandId == "commands" && binding.Display == "Ctrl+E");
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void A_kept_chord_runs_its_command_and_is_saved_for_next_time()
    {
        var settings = new DashboardSettings(_configRoot);
        var ran = 0;
        var commands = Registry(() => ran++);
        using var dialog = Open(out var theme, out var icons, commands: commands, keys: settings.KeyOverrides(commands));
        Record(dialog, "Commands", Key.G.WithCtrl, new Key('c'), Key.Enter);
        Press(dialog, Key.Enter.WithCtrl);

        dialog.Store(theme, icons, settings);
        commands.Press(Key.G.WithCtrl);
        commands.Press(new Key('c'));

        Assert.Equal(1, ran);
        var next = Registry();
        settings.KeyOverrides(next).Load();
        Assert.Equal("Ctrl+E, Ctrl+G C", next.KeysFor("commands"));
    }

    [Fact]
    public void Esc_while_recording_leaves_the_row_as_it_was_and_the_dialog_open()
    {
        using var dialog = Open(out _, out _);

        Record(dialog, "Commands", Key.G.WithCtrl, Key.Esc);

        Assert.False(dialog.KeysPage.IsRecording);
        Assert.False(dialog.KeysPage.IsChanged);
        Assert.Null(dialog.Result);
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void Esc_on_the_dialog_leaves_every_key_where_it_was()
    {
        var settings = new DashboardSettings(_configRoot);
        var commands = Registry();
        using var dialog = Open(out var theme, out var icons, commands: commands, keys: settings.KeyOverrides(commands));
        Record(dialog, "Commands", Key.F4, Key.Enter);
        Press(dialog, Key.Esc);

        dialog.Store(theme, icons, settings);

        Assert.Equal("Ctrl+E", commands.KeysFor("commands"));
        Assert.False(File.Exists(Path.Combine(_configRoot, "dashboard.json")) && File.ReadAllText(Path.Combine(_configRoot, "dashboard.json")).Contains("keymap"));
    }

    [Fact]
    public void Del_removes_the_selected_key()
    {
        using var dialog = Open(out _, out _);
        OpenPage(dialog, "Keyboard Shortcuts");
        dialog.KeysPage.Filter.SetFocus();
        dialog.KeysPage.Filter.Text = "Commands";
        dialog.KeysPage.Filter.MoveEnd();

        Press(dialog, Key.Delete);

        Assert.DoesNotContain(dialog.KeysPage.Staged, binding => binding.CommandId == "commands");
    }

    [Fact]
    public void A_chord_another_command_has_in_the_same_place_asks_which_keeps_it()
    {
        var asked = new List<string>();
        using var dialog = Open(out _, out _);
        dialog.Confirm = confirm => asked.Add(confirm.Title);

        Record(dialog, "Commands", Key.Q.WithCtrl, Key.Enter);

        Assert.Equal(["Replace key?"], asked);
    }

    [Fact]
    public void A_chord_that_starts_another_is_refused_on_the_row_naming_the_other_command()
    {
        using var dialog = Open(out _, out _);

        Record(dialog, "Commands", Key.G.WithCtrl, Key.Enter);

        Assert.True(dialog.KeysPage.IsRecording);
        Assert.Contains(dialog.KeysPage.Rows, row => row.Contains("Ctrl+G starts Ctrl+G W, which runs Go to Work", StringComparison.Ordinal));
        Assert.DoesNotContain(dialog.KeysPage.Staged, binding => binding.CommandId == "commands" && binding.Display == "Ctrl+G");
    }

    [Fact]
    public void A_chord_that_goes_on_from_another_is_refused_on_the_row_naming_the_other_command()
    {
        using var dialog = Open(out _, out _);

        Record(dialog, "Commands", Key.Q.WithCtrl, new Key('x'), Key.Enter);

        Assert.Contains(dialog.KeysPage.Rows, row => row.Contains("Ctrl+Q X goes on from Ctrl+Q, which runs Quit", StringComparison.Ordinal));
    }

    [Fact]
    public void Ctrl_R_on_the_keys_page_asks_before_putting_every_key_back()
    {
        var asked = new List<string>();
        using var dialog = Open(out _, out _);
        dialog.Confirm = confirm => asked.Add(confirm.Title);
        OpenPage(dialog, "Keyboard Shortcuts");

        Press(dialog, Key.R.WithCtrl);

        Assert.Equal(["Reset to defaults"], asked);
    }

    [Fact]
    public void Saving_a_key_leaves_the_theme_and_the_tool_calls_setting_as_they_were()
    {
        var settings = new DashboardSettings(_configRoot);
        var commands = Registry();
        using var dialog = Open(out var theme, out var icons, expand: true, keep: settings.WriteTheme, commands: commands, keys: settings.KeyOverrides(commands));
        theme.Preview(BundledThemes.Daylight);
        Record(dialog, "Commands", Key.F4, Key.Enter);
        Press(dialog, Key.Enter.WithCtrl);

        dialog.Store(theme, icons, settings);

        Assert.Equal("Ctrl+E, F4", commands.KeysFor("commands"));
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
    public void The_Teams_page_heads_each_column_over_the_list()
    {
        WriteTeam("ai", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        using var dialog = Open(out _, out _, page: "Teams", check: _ => Task.FromResult(new TeamHealth([])));

        Assert.Equal("Team  Repo              Status   Health", dialog.TeamHeader);
        Assert.Equal(["ai    mentaldesk/alpha  working  ok"], TeamRows(dialog));
    }

    [Fact]
    public void Today_shows_what_each_team_has_spent_and_a_team_at_its_budget_reads_budget_reached()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        WriteTeam("beta", """{"repo": "mentaldesk/beta", "dispatch": {"enabled": true, "budget": 40}}""");
        WriteTeam("gamma", """{"repo": "mentaldesk/gamma", "dispatch": {"enabled": false, "budget": 1}}""");
        var spent = new Dictionary<string, string>
        {
            ["alpha"] = """{"cost":18.4,"budget":0,"reached":false}""",
            ["beta"] = """{"cost":41.37,"budget":40,"reached":true}""",
            ["gamma"] = """{"cost":0,"budget":1,"reached":false}""",
        };
        using var dialog = Open(out _, out _, page: "Teams", today: team => Task.FromResult(new Reading(spent[team], null)));

        Assert.Equal("Team   Repo              Status           Today", dialog.TeamHeader);
        Assert.Equal(
            [
                "alpha  mentaldesk/alpha  working         $18.40",
                "beta   mentaldesk/beta   budget reached  $41.37",
                "gamma  mentaldesk/gamma  paused           $0.00",
            ],
            TeamRows(dialog));
    }

    [Fact]
    public void Today_stays_blank_for_a_team_whose_cost_cannot_be_read()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        using var dialog = Open(out _, out _, page: "Teams", today: _ => Task.FromResult(new Reading("", "board.sh: no config")));

        Assert.Equal(["alpha  mentaldesk/alpha  working"], TeamRows(dialog));
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
        Assert.Equal(["p pause · Enter edit · x remove · n new", "Ctrl+Enter keep · Esc cancel · Ctrl+G guide"], HintRows(dialog));

        Assert.True(dialog.Teams.NewKeyDownEvent(new Key('p')));

        Assert.Equal(["alpha  mentaldesk/alpha  paused"], TeamRows(dialog));
        Assert.True(new TeamConfigs(_configRoot).IsPaused("alpha"));
        Assert.Equal(["p resume · Enter edit · x remove · n new", "Ctrl+Enter keep · Esc cancel · Ctrl+G guide"], HintRows(dialog));

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
    public void The_Teams_page_says_checking_until_each_team_s_check_answers()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        WriteTeam("goose", """{"repo": "aaif-goose/goose"}""");
        var goose = new TaskCompletionSource<TeamHealth>();
        using var dialog = Open(out _, out _, page: "Teams", check: team => team == "goose"
            ? goose.Task
            : Task.FromResult(new TeamHealth([])));

        Assert.Equal(["alpha  mentaldesk/alpha  working  ok", "goose  aaif-goose/goose  paused   checking…"], TeamRows(dialog));

        goose.SetResult(new TeamHealth([new TeamProblem("project", "no field"), new TeamProblem("labels", "1 of 6 missing")]));
        Assert.Equal(["alpha  mentaldesk/alpha  working  ok", "goose  aaif-goose/goose  paused   2 problems"], TeamRows(dialog));
    }

    [Fact]
    public void Teams_are_checked_only_once_the_Teams_page_is_open()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha"}""");
        List<string> checkedTeams = [];
        using var dialog = Open(out _, out _, check: team =>
        {
            checkedTeams.Add(team);
            return Task.FromResult(new TeamHealth([]));
        });
        Assert.Empty(checkedTeams);

        OpenPage(dialog, "Teams");
        OpenPage(dialog, "Theme");
        OpenPage(dialog, "Teams");

        Assert.Equal(["alpha"], checkedTeams);
    }

    [Fact]
    public async Task The_Teams_page_shows_the_check_the_panes_already_ran_and_a_repair_reaches_them()
    {
        WriteTeam("goose", """{"repo": "aaif-goose/goose"}""");
        var ran = 0;
        var shared = new TeamChecks(
            _ => Task.FromResult(++ran == 1 ? new TeamHealth([new TeamProblem("status", "missing")]) : new TeamHealth([])),
            new TeamConfigs(_configRoot).Stamp);
        shared.Follow(["goose"]);
        using var dialog = Open(out _, out _, page: "Teams", checks: shared);

        Assert.Equal(["goose  aaif-goose/goose  paused  1 problem"], TeamRows(dialog));
        Assert.Equal(1, ran);

        dialog.RepairTeam = _ => true;
        await dialog.Repaired("goose")!;

        Assert.Null(shared.Fatal("goose"));
    }

    [Fact]
    public void A_file_that_can_t_be_read_is_one_problem_without_running_a_check()
    {
        WriteTeam("gamma", "{\"repo\": ");
        var ran = false;
        using var dialog = Open(out _, out _, page: "Teams", check: _ =>
        {
            ran = true;
            return Task.FromResult(new TeamHealth([]));
        });

        Assert.Equal(["gamma        can't read this file  1 problem"], TeamRows(dialog));
        Assert.False(ran);
    }

    [Fact]
    public async Task After_a_repair_the_team_is_checked_again_and_its_row_follows()
    {
        WriteTeam("goose", """{"repo": "aaif-goose/goose"}""");
        var checks = 0;
        using var dialog = Open(out _, out _, page: "Teams", check: _ => Task.FromResult(++checks == 1
            ? new TeamHealth([new TeamProblem("labels", "no 'blocked' label, so the Dev can't mark a task that waits on your answer")])
            : new TeamHealth([])));
        Assert.Equal(["goose  aaif-goose/goose  paused  1 problem"], TeamRows(dialog));
        dialog.RepairTeam = _ => true;

        var after = dialog.Repaired("goose");

        Assert.Empty((await after!).Problems);
        Assert.Equal(["goose  aaif-goose/goose  paused  ok"], TeamRows(dialog));
    }

    [Fact]
    public void A_repair_left_undone_checks_nothing_again()
    {
        WriteTeam("goose", """{"repo": "aaif-goose/goose"}""");
        var checks = 0;
        using var dialog = Open(out _, out _, page: "Teams", check: _ =>
        {
            checks++;
            return Task.FromResult(new TeamHealth([new TeamProblem("labels", "no 'blocked' label, so the Dev can't mark a task that waits on your answer")]));
        });
        dialog.RepairTeam = _ => false;

        Assert.Null(dialog.Repaired("goose"));
        Assert.Equal(1, checks);
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
    public void A_save_with_a_docs_page_not_written_yet_says_where_the_Customer_lead_will_propose_them()
    {
        var workdir = Path.Combine(_configRoot, "work");
        Directory.CreateDirectory(Path.Combine(workdir, "main", "docs"));
        File.WriteAllText(Path.Combine(workdir, "main", "docs", "vision.md"), "");
        WriteTeam("alpha", $$"""{"repo": "mentaldesk/alpha", "workdir": "{{workdir}}", "vision": "docs/vision.md"}""");
        using var dialog = Open(out _, out _, page: "Teams", today: _ => Task.FromResult(new Reading("", null)));
        dialog.EditTeam = (_, settings, save) =>
        {
            var after = settings with { Customer = true, Docs = "docs/guide/index.md" };
            return save(after) is null ? after : null;
        };

        dialog.Teams.NewKeyDownEvent(Key.Enter);

        Assert.Equal("The Customer lead will propose the docs at docs/guide/index.md.", dialog.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Base), dialog.Message.SchemeName);
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
        Assert.Equal(["p pause · Enter edit · x remove · n new", "Ctrl+Enter keep · Esc cancel · Ctrl+G guide"], HintRows(dialog));

        dialog.Teams.NewKeyDownEvent(new Key('x'));

        Assert.Empty(TeamRows(dialog));
        Assert.Null(dialog.Teams.Value);
        Assert.Equal(["n new", "Ctrl+Enter keep · Esc cancel · Ctrl+G guide"], HintRows(dialog));
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

    private static bool Has(string row, params string[] parts) => parts.All(part => row.Contains(part, StringComparison.Ordinal));

    /// <summary>The app hands each key to the dialog's route before any view, as the dashboard does.</summary>
    private static void Press(SettingsDialog dialog, Key key)
    {
        if (!dialog.Route(key))
            dialog.NewKeyDownEvent(key);
    }

    private static void Record(SettingsDialog dialog, string command, params Key[] keys)
    {
        OpenPage(dialog, "Keyboard Shortcuts");
        dialog.KeysPage.Filter.SetFocus();
        dialog.KeysPage.Filter.Text = command;
        Press(dialog, Key.Enter);
        foreach (var key in keys)
            Press(dialog, key);
    }

    private static IReadOnlyList<string> PageNames(SettingsDialog dialog) =>
        [.. Enumerable.Range(0, dialog.Pages.Source?.Count ?? 0)
            .Select(i => dialog.Pages.Source!.ToList()[i]?.ToString() ?? "")];

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

    [Fact]
    public void Ctrl_G_opens_the_guide_on_the_Teams_page_from_any_page_and_F1_no_longer_does()
    {
        var opened = new List<string>();
        using var dialog = Open(out _, out _, showGuide: opened.Add);
        dialog.ShowHelp = _ => { };

        Assert.True(dialog.NewKeyDownEvent(Key.G.WithCtrl));
        Hint(dialog, "Ctrl+G guide").InvokeCommand(Command.Accept);
        Assert.True(dialog.NewKeyDownEvent(Key.F1));

        Assert.Equal([GuideDialog.Teams, GuideDialog.Teams], opened);
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void F1_on_every_page_shows_Help_headed_with_the_page_and_listing_its_keys_and_the_dialogs()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        using var dialog = Open(out _, out _);
        var shown = new List<KeySheet>();
        dialog.ShowHelp = shown.Add;
        (string, string)[] dialogKeys = [("Ctrl+Enter", "Keep"), ("Ctrl+G", "Guide"), ("Tab/Shift+Tab", "Next / previous"), ("Esc", "Cancel")];

        var pages = PageNames(dialog).ToList();
        foreach (var page in pages)
        {
            OpenPage(dialog, page);
            Assert.True(dialog.NewKeyDownEvent(Key.F1));
        }

        Assert.Equal(pages, shown.Select(sheet => sheet.Here!.Heading));
        Assert.All(shown, sheet => Assert.Null(sheet.Everywhere));
        Assert.Equal(dialogKeys, DialogTests.Rows(shown[pages.IndexOf("Theme")]));
        Assert.Equal([("Enter", "Add a key"), ("Del", "Remove the key"), ("Ctrl+R", "Reset to defaults…"), ("Type", "Filter"), .. dialogKeys],
            DialogTests.Rows(shown[pages.IndexOf("Keyboard Shortcuts")]));
        Assert.Equal([("p", "Pause"), ("Enter", "Edit"), ("x", "Remove"), ("n", "New team"), .. dialogKeys],
            DialogTests.Rows(shown[pages.IndexOf(SettingsDialog.TeamsPage)]));
    }

    [Fact]
    public void Closing_Help_puts_focus_back_on_the_field_with_what_was_typed()
    {
        using var dialog = Open(out _, out _, page: "Keyboard Shortcuts");
        dialog.KeysPage.Filter.SetFocus();
        dialog.KeysPage.Filter.Text = "comm";
        dialog.ShowHelp = _ => dialog.Pages.SetFocus();

        Assert.True(dialog.NewKeyDownEvent(Key.F1));

        Assert.True(dialog.KeysPage.Filter.HasFocus);
        Assert.Equal("comm", dialog.KeysPage.Filter.Text);
    }

    private static CommandRegistry Registry(Action? commands = null) => new CommandRegistry()
        .Register("commands", "Commands", commands ?? (() => { }), Key.E.WithCtrl)
        .Register("view.work", "Go to Work", () => { }, chord: "Ctrl+G W")
        .Register("work.try", "Try", () => { }, new Key('t'), scopes: [Regions.Work])
        .Register("quit", "Quit", () => { }, Key.Q.WithCtrl);

    private SettingsDialog Open(
        out ThemeSetting theme,
        out IconSetting icons,
        bool expand = false,
        IconStyle iconStyle = IconStyle.Auto,
        Action<string>? keep = null,
        Action<IconStyle>? apply = null,
        CommandRegistry? commands = null,
        IconStyle auto = IconStyle.Unicode,
        string? page = null,
        Func<string, Task<TeamHealth>>? check = null,
        Action<string>? showGuide = null,
        TeamChecks? checks = null,
        Func<string, Task<Reading>>? today = null,
        ITerminalKeys? terminalKeys = null,
        KeyOverrides? keys = null)
    {
        theme = new ThemeSetting(BundledThemes.Midnight, _ => { }, keep ?? (_ => { }));
        icons = new IconSetting(iconStyle, apply ?? (_ => { }), new DashboardSettings(_configRoot).WriteIcons);
        var dialog = new SettingsDialog(
            theme, icons, expand, commands ?? Registry(), new TeamConfigs(_configRoot), () => { }, auto, page, check: check, showGuide: showGuide, checks: checks, today: today,
            terminalKeys: terminalKeys, keys: keys);
        dialog.SetFocus();
        return dialog;
    }
}
