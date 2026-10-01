using System.Drawing;
using System.Text;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

[Collection("StaticConfiguration")]
public class DashboardWindowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");
    private readonly PlatformKeyBinding _quit = Application.DefaultKeyBindings![Command.Quit];

    public void Dispose()
    {
        Application.SetDefaultKeyBinding(Command.Quit, _quit);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void The_apps_own_quit_binding_moves_to_the_quit_key_so_Esc_only_goes_back()
    {
        using var window = Open();

        Assert.Equal(new Key('q'), Application.GetDefaultKey(Command.Quit));
    }

    [Fact]
    public void Rebinding_quit_takes_the_apps_quit_binding_with_it()
    {
        using var window = Open(keys: "{ \"quit\": \"x\" }");

        Assert.Equal(new Key('x'), Application.GetDefaultKey(Command.Quit));
    }

    [Fact]
    public void Pressing_t_expands_the_selected_pane_and_nothing_else()
    {
        using var window = Open();
        window.NewKeyDownEvent(Key.Tab);

        Assert.True(window.NewKeyDownEvent(new Key('t')));

        Assert.Equal([true, false], window.Panes.Select(pane => pane.Expanded));
    }

    [Fact]
    public void Pressing_t_again_collapses_that_pane()
    {
        using var window = Open();
        window.NewKeyDownEvent(Key.Tab);

        window.NewKeyDownEvent(new Key('t'));
        window.NewKeyDownEvent(new Key('t'));

        Assert.All(window.Panes, pane => Assert.False(pane.Expanded));
    }

    [Fact]
    public void Tab_moves_the_selection_on_and_t_follows_it()
    {
        using var window = Open();
        window.NewKeyDownEvent(Key.Tab);
        window.NewKeyDownEvent(Key.Tab);

        window.NewKeyDownEvent(new Key('t'));

        Assert.Equal([false, true], window.Panes.Select(pane => pane.Expanded));
    }

    [Fact]
    public void The_keys_t_does_not_shadow_still_do_what_they_did()
    {
        using var window = Open();

        Assert.True(window.NewKeyDownEvent(Key.Tab));
        Assert.True(window.NewKeyDownEvent(Key.PageUp));
        Assert.True(window.NewKeyDownEvent(Key.PageDown));
        Assert.True(window.NewKeyDownEvent(Key.Home));
        Assert.True(window.NewKeyDownEvent(Key.End));
        Assert.True(window.NewKeyDownEvent(Key.CursorRight));
        Assert.True(window.NewKeyDownEvent(Key.Tab.WithShift));
        Assert.All(window.Panes, pane => Assert.False(pane.Expanded));
    }

    [Fact]
    public void Panes_start_expanded_when_that_is_what_the_settings_file_says()
    {
        using var window = Open(expandToolCalls: true);

        Assert.All(window.Panes, pane => Assert.True(pane.Expanded));
    }

    [Fact]
    public void A_pane_that_started_expanded_still_folds_up_on_t()
    {
        using var window = Open(expandToolCalls: true);
        window.NewKeyDownEvent(Key.Tab);

        window.NewKeyDownEvent(new Key('t'));

        Assert.Equal([false, true], window.Panes.Select(pane => pane.Expanded));
    }

    [Theory]
    [InlineData(2, 1, 2)]
    [InlineData(4, 2, 2)]
    [InlineData(6, 3, 2)]
    public void Agents_are_a_row_per_team_and_a_column_per_role(int count, int rows, int columns)
    {
        using var window = Open(agents: Agents(count));

        var cells = LayOut(window, 120, 30);

        Assert.Equal(rows, cells.Select(cell => cell.Y).Distinct().Count());
        Assert.Equal(columns, cells.Select(cell => cell.X).Distinct().Count());
    }

    [Theory]
    [InlineData(2, 120, 30)]
    [InlineData(4, 120, 30)]
    [InlineData(6, 120, 30)]
    [InlineData(4, 101, 41)]
    [InlineData(6, 101, 41)]
    [InlineData(3, 120, 30)]
    [InlineData(5, 101, 41)]
    public void The_cells_divide_the_agent_area_with_nothing_left_over(int count, int width, int height)
    {
        using var window = Open(agents: Agents(count));

        var cells = LayOut(window, width, height);

        AssertTiles(AgentArea(window), cells);
    }

    [Fact]
    public void Resizing_lays_the_grid_out_again_over_the_new_area()
    {
        using var window = Open(agents: Agents(4));
        LayOut(window, 120, 30);

        var cells = LayOut(window, 101, 25);

        AssertTiles(AgentArea(window), cells);
    }

    [Fact]
    public void Forgetting_a_removed_team_takes_its_agents_off_the_grid_and_its_lane_out_of_the_Work_area()
    {
        using var window = Open(agents: Agents(6));
        LayOut(window, 120, 30);

        window.Forget(["team0"]);
        var cells = LayOut(window, 120, 30);

        Assert.Equal(["team1", "team1", "team2", "team2"], window.Panes.Select(pane => pane.Team));
        Assert.Equal(["team1", "team2"], window.Work.Lanes.Select(lane => lane.Team));
        AssertTiles(AgentArea(window), cells);
    }

    [Fact]
    public void Two_teams_at_120_by_30_give_each_agent_a_readable_width()
    {
        using var window = Open(agents: Agents(4));

        var cells = LayOut(window, 120, 30);

        Assert.All(cells, cell => Assert.Equal(60, cell.Width));
    }

    [Fact]
    public void The_dispatcher_strip_stays_at_the_bottom_full_width()
    {
        using var window = Open(agents: Agents(4));
        LayOut(window, 120, 30);

        var strip = window.Dispatcher.Frame;

        Assert.Equal(new Rectangle(0, window.Viewport.Height - 7, window.Viewport.Width, 6), strip);
    }

    [Fact]
    public void Tab_steps_through_every_agent_in_reading_order_and_wraps()
    {
        using var window = Open(agents: Agents(4));

        var visited = Enumerable.Range(0, 5).Select(_ =>
        {
            window.NewKeyDownEvent(Key.Tab);
            return Selected(window);
        });

        Assert.Equal([0, 1, 2, 3, 0], visited);
    }

    [Fact]
    public void Shift_Tab_steps_back_through_reading_order_and_wraps()
    {
        using var window = Open(agents: Agents(4));

        var visited = Enumerable.Range(0, 5).Select(_ =>
        {
            window.NewKeyDownEvent(Key.Tab.WithShift);
            return Selected(window);
        });

        Assert.Equal([3, 2, 1, 0, 3], visited);
    }

    [Fact]
    public void The_arrows_move_by_row_and_by_column()
    {
        using var window = Open(agents: Agents(4));
        window.NewKeyDownEvent(Key.Tab);

        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(2, Selected(window));
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(3, Selected(window));
        window.NewKeyDownEvent(Key.CursorUp);
        Assert.Equal(1, Selected(window));
        window.NewKeyDownEvent(Key.CursorLeft);
        Assert.Equal(0, Selected(window));
    }

    [Fact]
    public void The_arrows_do_not_fall_off_the_edges()
    {
        using var window = Open(agents: Agents(4));
        window.NewKeyDownEvent(Key.Tab);

        Assert.True(window.NewKeyDownEvent(Key.CursorUp));
        Assert.True(window.NewKeyDownEvent(Key.CursorLeft));
        Assert.Equal(0, Selected(window));

        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.True(window.NewKeyDownEvent(Key.CursorDown));
        Assert.True(window.NewKeyDownEvent(Key.CursorRight));
        Assert.Equal(3, Selected(window));
    }

    [Fact]
    public void An_odd_agent_out_stays_selectable_from_the_row_above()
    {
        using var window = Open(agents: Agents(3));
        window.NewKeyDownEvent(Key.Tab);
        window.NewKeyDownEvent(Key.CursorRight);

        window.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal(2, Selected(window));
    }

    [Fact]
    public void Enter_expands_the_selected_agent_over_the_whole_agent_area()
    {
        using var window = Open(agents: Agents(4));
        window.NewKeyDownEvent(Key.Tab);
        window.NewKeyDownEvent(Key.Tab);

        Assert.True(window.NewKeyDownEvent(Key.Enter));
        var cells = LayOut(window, 120, 30);

        Assert.Equal(1, window.ExpandedAgent);
        Assert.Equal(AgentArea(window), cells[1]);
    }

    [Fact]
    public void Esc_puts_the_grid_back_the_way_it_was()
    {
        using var window = Open(agents: Agents(4));
        window.NewKeyDownEvent(Key.Tab);
        var grid = LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        LayOut(window, 120, 30);
        Assert.True(window.NewKeyDownEvent(Key.Esc));
        var back = LayOut(window, 120, 30);

        Assert.Null(window.ExpandedAgent);
        Assert.Equal(grid, back);
    }

    [Fact]
    public void Esc_with_nothing_expanded_does_nothing_and_q_is_the_way_out()
    {
        using var window = Open(agents: Agents(4));
        window.NewKeyDownEvent(Key.Tab);

        Assert.False(window.NewKeyDownEvent(Key.Esc));
        Assert.True(window.NewKeyDownEvent(new Key('q')));
    }

    [Fact]
    public void Tab_and_Shift_Tab_read_the_next_agent_without_leaving_the_expanded_view()
    {
        using var window = Open(agents: Agents(4));
        window.NewKeyDownEvent(Key.Tab);
        window.NewKeyDownEvent(Key.Enter);

        window.NewKeyDownEvent(Key.Tab);
        Assert.Equal(1, window.ExpandedAgent);
        Assert.Equal(1, Selected(window));

        window.NewKeyDownEvent(Key.Tab.WithShift);
        Assert.Equal(0, window.ExpandedAgent);
        Assert.Equal(0, Selected(window));
    }

    [Fact]
    public void The_arrows_do_nothing_while_expanded()
    {
        using var window = Open(agents: Agents(4));
        window.NewKeyDownEvent(Key.Tab);
        window.NewKeyDownEvent(Key.Enter);

        foreach (var arrow in new[] { Key.CursorDown, Key.CursorRight, Key.CursorUp, Key.CursorLeft })
            Assert.True(window.NewKeyDownEvent(arrow));

        Assert.Equal(0, window.ExpandedAgent);
        Assert.Equal(0, Selected(window));
    }

    [Fact]
    public void Scrolling_and_t_still_reach_the_expanded_agent()
    {
        using var window = Open(agents: Agents(4));
        window.NewKeyDownEvent(Key.Tab);
        window.NewKeyDownEvent(Key.Enter);

        Assert.True(window.NewKeyDownEvent(Key.PageUp));
        Assert.True(window.NewKeyDownEvent(Key.PageDown));
        Assert.True(window.NewKeyDownEvent(Key.Home));
        Assert.True(window.NewKeyDownEvent(Key.End));
        Assert.True(window.NewKeyDownEvent(new Key('t')));

        Assert.Equal([true, false, false, false], window.Panes.Select(pane => pane.Expanded));
    }

    [Fact]
    public void The_dispatcher_strip_keeps_its_place_while_an_agent_is_expanded()
    {
        using var window = Open(agents: Agents(4));
        LayOut(window, 120, 30);
        var grid = window.Dispatcher.Frame;

        window.NewKeyDownEvent(Key.Tab);
        window.NewKeyDownEvent(Key.Enter);
        LayOut(window, 120, 30);

        Assert.Equal(grid, window.Dispatcher.Frame);
    }

    [Fact]
    public void The_status_bar_offers_Enter_to_expand_and_Esc_to_go_back_from_there()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal(
            "a-team 1.2.3 · Enter: expand",
            DashboardWindow.Hints("1.2.3", Mode.Grid, window.Commands));
        Assert.Equal(
            "a-team 1.2.3 · PgUp/PgDn: scroll · Esc: back",
            DashboardWindow.Hints("1.2.3", Mode.Expanded, window.Commands));
        Assert.Equal(
            "a-team 1.2.3 · Enter: read · p: set priority · m: only mine · F5: refresh",
            DashboardWindow.Hints("1.2.3", Mode.Work, window.Commands));
    }

    /// <summary>Work's bar is the one #130 asks for, which its mockup already draws cut off at 80 columns.</summary>
    [Fact]
    public void Every_bar_but_the_Work_area_s_fits_the_80_columns_the_narrowest_window_has()
    {
        using var window = Open(agents: Agents(4));

        Assert.All(
            new[] { Mode.Grid, Mode.Expanded },
            mode => Assert.InRange(DashboardWindow.Hints("1.2.3", mode, window.Commands).Length, 1, 77));
    }

    [Fact]
    public void Every_action_the_dashboard_has_is_a_command_you_can_run_by_name()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal(
            [
                "Select the next agent", "Select the previous agent", "Select the agent to the right",
                "Select the agent to the left", "Select the agent below", "Select the agent above",
                "Expand the selected agent", "Scroll the log up", "Scroll the log down",
                "Jump to the top of the log", "Jump to the bottom of the log", "Show tool calls in full",
                "Select the column to the right", "Select the column to the left", "Select the card below",
                "Select the card above", "Open", "Set priority", "Try PR", "Open on GitHub",
                "Approve the pitch you're reading", "Show only what's your move",
                "Read what's waiting again", "Dashboard", "Work",
                "Pause selected agent's role", "Interrupt selected agent", "Commands", "Settings", "Teams", "Keys", "About", "Back to the agent grid", "Quit",
            ],
            window.Commands.Registered.Select(command => command.Label));
    }

    [Fact]
    public void Help_opens_with_F1()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal(Key.F1, window.Commands.Registered.Single(c => c.Id == "help").Key);
    }

    [Fact]
    public void Settings_opens_with_s_and_Ctrl_comma_is_gone()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal(new Key('s'), window.Commands.Registered.Single(c => c.Id == "settings").Key);
        Assert.DoesNotContain(window.Commands.Registered, c => c.Key == new Key(',').WithCtrl);
        Assert.False(window.NewKeyDownEvent(new Key(',').WithCtrl));
        Assert.All(
            new[] { Mode.Grid, Mode.Expanded, Mode.Work },
            mode => Assert.DoesNotContain("Ctrl+,", DashboardWindow.Hints("1.2.3", mode, window.Commands)));
    }

    [Fact]
    public void A_key_the_file_names_runs_that_command_and_the_one_in_the_source_no_longer_does()
    {
        using var window = Open(keys: "{ \"log.toolCalls\": \"x\" }");
        window.NewKeyDownEvent(Key.Tab);

        Assert.True(window.NewKeyDownEvent(new Key('x')));
        Assert.False(window.NewKeyDownEvent(new Key('t')));

        Assert.Equal([true, false], window.Panes.Select(pane => pane.Expanded));
    }

    [Fact]
    public void A_command_the_file_says_nothing_about_keeps_the_key_it_had()
    {
        using var window = Open(keys: "{ \"log.toolCalls\": \"x\" }");

        Assert.Equal(Key.F1, window.Commands.Registered.Single(command => command.Id == "help").Key);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"nobody.registered.this\": \"F4\" }")]
    [InlineData("{ \"settings\": \"nonsense\" }")]
    [InlineData("{ \"settings\": \"t\" }")]
    public void An_override_we_cannot_use_is_ignored_on_its_own_and_leaves_every_default_alone(string keys)
    {
        using var window = Open(keys: keys);

        Assert.Equal(new Key('s'), window.Commands.Registered.Single(command => command.Id == "settings").Key);
        Assert.Equal(new Key('t'), window.Commands.Registered.Single(command => command.Id == "log.toolCalls").Key);
    }

    [Theory]
    [InlineData("{ \"nobody.registered.this\": \"F4\", \"help\": \"F2\" }")]
    [InlineData("{ \"settings\": \"nonsense\", \"help\": \"F2\" }")]
    [InlineData("{ \"settings\": \"t\", \"help\": \"F2\" }")]
    public void The_rest_of_the_file_still_applies_around_an_override_we_cannot_use(string keys)
    {
        using var window = Open(keys: keys);

        Assert.Equal(Key.F2, window.Commands.Registered.Single(command => command.Id == "help").Key);
    }

    [Fact]
    public void The_status_bar_names_the_key_the_file_bound_and_not_the_one_in_the_source()
    {
        using var window = Open(agents: Agents(4), keys: "{ \"agent.expand\": \"x\" }");

        Assert.Contains("x: expand", DashboardWindow.Hints("1.2.3", Mode.Grid, window.Commands));
        Assert.DoesNotContain("Enter", DashboardWindow.Hints("1.2.3", Mode.Grid, window.Commands));
    }

    [Fact]
    public void The_commands_list_names_the_key_the_file_bound()
    {
        using var window = Open(agents: Agents(4), keys: "{ \"commands\": \"Ctrl+K\" }");

        using var dialog = new CommandsDialog(window.Commands.Registered);

        Assert.Contains(dialog.Matches.Select(command => command.Key), key => key == Key.K.WithCtrl);
    }

    [Fact]
    public void The_status_bar_follows_the_view_it_is_showing()
    {
        using var window = Open(agents: Agents(4));
        window.NewKeyDownEvent(Key.Tab);

        window.NewKeyDownEvent(Key.Enter);
        Assert.Equal(window.HintLine, window.Status.Says);
        Assert.Contains("Esc: back", window.Status.Says);

        window.NewKeyDownEvent(Key.Esc);
        Assert.Equal(window.HintLine, window.Status.Says);
        Assert.DoesNotContain("Esc: back", window.Status.Says);
        Assert.Contains("Enter: expand", window.Status.Says);
    }

    [Fact]
    public void The_hints_are_the_last_row_of_the_window_in_a_band_of_their_own()
    {
        using var window = Open(agents: Agents(4));

        LayOut(window, 120, 30);

        Assert.Equal(new Rectangle(0, window.Viewport.Height - 1, window.Viewport.Width, 1), window.Status.Frame);
        Assert.Equal(StatusBar.Scheme, window.Status.SchemeName);
        Assert.Equal(window.HintLine, window.Status.Says);
        Assert.Equal(0, window.Message.Lines);
    }

    [Fact]
    public void A_message_takes_the_row_below_the_hints_and_never_their_own()
    {
        using var window = Open(agents: Agents(4), run: _ => new TaskCompletionSource<string?>().Task);
        LayOut(window, 120, 30);
        var hints = window.Status.Frame;
        SelectAgent(window, 0);

        window.Commands.Execute("agent.hold");
        LayOut(window, 120, 30);

        Assert.Equal("Pausing this role…", window.Message.Says);
        Assert.Equal(new Rectangle(0, window.Viewport.Height - 1, window.Viewport.Width, 1), window.Message.Frame);
        Assert.Equal(hints with { Y = window.Viewport.Height - 2 }, window.Status.Frame);
        Assert.Equal(window.HintLine, window.Status.Says);
    }

    [Fact]
    public void Clicking_a_hint_runs_the_command_it_names()
    {
        using var window = Open(agents: Agents(4));
        window.NewKeyDownEvent(Key.Tab);

        Hint(window, "Enter: expand").InvokeCommand(Command.Accept);

        Assert.Equal(0, window.ExpandedAgent);
    }

    [Fact]
    public void A_hint_is_a_clickable_one_of_its_own_that_claims_no_key()
    {
        using var window = Open(agents: Agents(4));

        Assert.All(window.Status.Hints, hint =>
        {
            Assert.True(hint.NoDecorations);
            Assert.True(hint.NoPadding);
            Assert.Equal(ShadowStyles.None, hint.ShadowStyle);
            Assert.False(hint.CanFocus);
            Assert.Equal((Rune)0xffff, hint.HotKeySpecifier);
        });
    }

    [Fact]
    public void The_window_has_no_border_and_no_title_so_the_menu_is_row_zero()
    {
        using var window = Open(agents: Agents(4));

        LayOut(window, 120, 30);

        Assert.Equal("", window.Title);
        Assert.Equal(LineStyle.None, window.BorderStyle);
        Assert.Equal(new Size(120, 30), window.Viewport.Size);
        Assert.Equal(new Rectangle(0, 0, 120, 1), window.Menu.Frame);
    }

    [Fact]
    public void A_cell_never_shrinks_below_five_rows()
    {
        using var window = Open(agents: Agents(6));

        var cells = LayOut(window, 120, 20);

        Assert.All(cells, cell => Assert.Equal(5, cell.Height));
        Assert.True(window.Agents.GetContentSize().Height > window.Agents.Viewport.Height);
    }

    [Theory]
    [InlineData(8, 120, 30, false)]
    [InlineData(10, 120, 30, true)]
    [InlineData(4, 120, 20, false)]
    [InlineData(6, 120, 20, true)]
    public void The_grid_scrolls_from_a_fifth_team_at_120_by_30_and_a_third_on_a_20_row_window(
        int count, int width, int height, bool scrolls)
    {
        using var window = Open(agents: Agents(count));

        LayOut(window, width, height);

        Assert.Equal(scrolls, window.Agents.VerticalScrollBar.Visible);
        Assert.Equal(scrolls, window.Agents.GetContentSize().Height > window.Agents.Viewport.Height);
    }

    [Fact]
    public void A_scrolling_grid_still_tiles_its_content_with_nothing_left_over()
    {
        using var window = Open(agents: Agents(10));

        var cells = LayOut(window, 120, 30);

        AssertTiles(new Rectangle(Point.Empty, window.Agents.GetContentSize()), cells);
    }

    [Fact]
    public void Selecting_an_agent_below_the_fold_scrolls_it_into_view_and_back_again()
    {
        using var window = Open(agents: Agents(6));
        LayOut(window, 120, 20);

        SelectAgent(window, 4);
        Assert.True(window.Agents.Viewport.Y > 0);
        Assert.True(InView(window, 4));

        window.NewKeyDownEvent(Key.CursorUp);
        window.NewKeyDownEvent(Key.CursorUp);
        Assert.Equal(0, Selected(window));
        Assert.Equal(0, window.Agents.Viewport.Y);
    }

    [Fact]
    public void Scrolling_keys_stay_with_the_selected_panes_log_and_leave_the_grid_where_it_is()
    {
        using var window = Open(agents: Agents(6));
        LayOut(window, 120, 20);
        SelectAgent(window, 4);
        var top = window.Agents.Viewport.Y;

        Assert.True(window.NewKeyDownEvent(Key.PageUp));
        Assert.True(window.NewKeyDownEvent(Key.PageDown));
        Assert.True(window.NewKeyDownEvent(Key.Home));
        Assert.True(window.NewKeyDownEvent(Key.End));

        Assert.Equal(top, window.Agents.Viewport.Y);
    }

    [Fact]
    public void Expanding_from_a_scrolled_grid_fills_the_area_and_Esc_comes_back_to_that_agent()
    {
        using var window = Open(agents: Agents(6));
        LayOut(window, 120, 20);
        SelectAgent(window, 4);

        window.NewKeyDownEvent(Key.Enter);
        var expanded = LayOut(window, 120, 20);
        Assert.Equal(0, window.Agents.Viewport.Y);
        Assert.False(window.Agents.VerticalScrollBar.Visible);
        Assert.Equal(new Rectangle(Point.Empty, window.Agents.Viewport.Size), expanded[4]);

        window.NewKeyDownEvent(Key.Esc);
        LayOut(window, 120, 20);
        Assert.Equal(4, Selected(window));
        Assert.True(InView(window, 4));
    }

    [Fact]
    public void Resizing_down_to_where_the_floor_bites_and_back_keeps_the_selection_in_view()
    {
        using var window = Open(agents: Agents(6));
        LayOut(window, 120, 30);
        SelectAgent(window, 4);
        Assert.Equal(0, window.Agents.Viewport.Y);

        LayOut(window, 120, 20);
        Assert.True(InView(window, 4));

        var back = LayOut(window, 120, 30);
        Assert.Equal(0, window.Agents.Viewport.Y);
        AssertTiles(AgentArea(window), back);
    }

    [Fact]
    public void Every_dispatcher_line_gets_one_row_of_its_own_coloured_by_what_it_says()
    {
        WriteDispatchLog(
            "2026-09-20T16:53:09Z a-team lead: would start: 3 Ideas to shape",
            "2026-09-20T17:01:17Z tuicode dev: triggers failed: gh: Not Found (HTTP 404)",
            "2026-09-20T17:04:02Z tuicode dev: started 41234: 1 task Ready");
        using var window = Open(agents: Agents(4));
        LayOut(window, 120, 30);

        window.Refresh();

        Assert.Equal(
            ["16:53 a-team lead: would start: 3 Ideas to shape",
             "17:01 tuicode dev: triggers failed: gh: Not Found (HTTP 404)",
             "17:04 tuicode dev: started 41234: 1 task Ready"],
            window.DispatchLog.Lines.Select(line => line.Text));
        Assert.Equal(
            [LogLineKind.DispatchSkipped, LogLineKind.DispatchFailed, LogLineKind.Prose],
            window.DispatchLog.Lines.Select(line => line.Kind));
    }

    [Fact]
    public void The_last_four_lines_fill_four_rows_of_the_panes_width_at_any_size()
    {
        WriteDispatchLog([.. Enumerable.Range(1, 6).Select(n =>
            $"2026-09-20T17:0{n}:17Z tuicode dev: triggers failed: " + new string('x', 400))]);
        using var window = Open(agents: Agents(4));
        LayOut(window, 120, 30);

        window.Refresh();

        Assert.Equal(4, window.DispatchLog.Lines.Count);
        Assert.Equal(118, window.DispatchLog.Viewport.Width);
        AssertFourRowsOfTheWidth(window);

        LayOut(window, 80, 30);
        Assert.Equal(78, window.DispatchLog.Viewport.Width);
        AssertFourRowsOfTheWidth(window);
    }

    private static void AssertFourRowsOfTheWidth(DashboardWindow window)
    {
        var rows = window.DispatchLog.Rows(window.DispatchLog.Viewport.Width);
        Assert.Equal(4, rows.Count);
        Assert.All(rows, row => Assert.Equal(window.DispatchLog.Viewport.Width, row.Text.Length));
    }

    [Fact]
    public void A_dispatcher_that_has_never_run_says_so_below_a_grid_the_message_bar_still_sits_under()
    {
        using var window = Open(agents: Agents(4));
        LayOut(window, 120, 30);

        window.Refresh();

        var line = Assert.Single(window.DispatchLog.Lines);
        Assert.Equal("(the dispatcher hasn't run yet)", line.Text);
        Assert.Equal(LogLineKind.Prose, line.Kind);
        Assert.Equal(0, window.Message.Lines);
        Assert.Equal(window.Viewport.Height - 7, window.Dispatcher.Frame.Y);
    }

    [Fact]
    public void The_dispatcher_strip_does_not_scroll_with_the_grid()
    {
        using var window = Open(agents: Agents(6));
        LayOut(window, 120, 20);
        var strip = window.Dispatcher.Frame;

        SelectAgent(window, 4);
        LayOut(window, 120, 20);

        Assert.True(window.Agents.Viewport.Y > 0);
        Assert.Equal(strip, window.Dispatcher.Frame);
    }

    [Fact]
    public void The_message_block_takes_no_rows_until_there_is_something_to_say()
    {
        using var window = Open(agents: Agents(4));

        LayOut(window, 120, 30);

        Assert.Equal(0, window.Message.Lines);
        Assert.Equal(0, window.Message.Frame.Height);
        Assert.Equal(window.Viewport.Height - 7, window.Dispatcher.Frame.Y);
    }

    [Fact]
    public void A_message_shrinks_the_grid_above_it_instead_of_covering_anything()
    {
        using var window = Open(agents: Agents(4), run: _ => new TaskCompletionSource<string?>().Task);
        var before = LayOut(window, 120, 30);
        SelectAgent(window, 0);

        window.Commands.Execute("agent.hold");
        var after = LayOut(window, 120, 30);

        Assert.Equal("Pausing this role…", window.Message.Says);
        Assert.Equal(new Rectangle(0, window.Viewport.Height - 1, window.Viewport.Width, 1), window.Message.Frame);
        Assert.Equal(window.Viewport.Height - 8, window.Dispatcher.Frame.Y);
        Assert.Equal(before.Sum(cell => cell.Height) - 2, after.Sum(cell => cell.Height));
        AssertTiles(AgentArea(window), after);
    }

    [Fact]
    public void A_second_go_is_refused_until_the_first_one_resolves()
    {
        var calls = 0;
        var finish = new TaskCompletionSource<string?>();
        using var window = Open(agents: Agents(4), run: _ =>
        {
            calls++;
            return finish.Task;
        });
        SelectAgent(window, 0);

        window.Commands.Execute("agent.hold");
        window.Commands.Execute("agent.hold");
        Assert.Equal(1, calls);

        finish.SetResult(null);
        window.Refresh();
        window.Commands.Execute("agent.hold");
        Assert.Equal(2, calls);
    }

    [Fact]
    public void A_command_that_worked_leaves_the_message_block_empty_again()
    {
        using var window = Open(agents: Agents(4));
        SelectAgent(window, 0);

        window.Commands.Execute("agent.hold");
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(0, window.Message.Lines);
        Assert.Equal(window.Viewport.Height - 7, window.Dispatcher.Frame.Y);
    }

    [Fact]
    public void A_command_that_failed_shows_one_line_and_leaves_the_dashboard_usable()
    {
        using var window = Open(
            agents: Agents(4),
            run: _ => Task.FromResult<string?>("a-team pause: can't write /nope/team0.json\nstack\ntrace"));
        SelectAgent(window, 0);

        window.Commands.Execute("agent.hold");
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("a-team pause: can't write /nope/team0.json", window.Message.Says);
        Assert.Equal(1, window.Message.Lines);
        Assert.True(window.NewKeyDownEvent(Key.Tab));
        Assert.Equal(1, Selected(window));
    }

    [Fact]
    public void Teams_opens_Settings_on_its_Teams_page_and_nothing_pauses_a_team_from_outside_it()
    {
        using var window = Open(agents: Agents(4));

        var ids = window.Commands.Registered.Select(command => command.Id).ToList();
        Assert.DoesNotContain("team.pause", ids);
        Assert.Contains("teams", ids);
        Assert.DoesNotContain(window.MenuItems, item => item.Id == "team.pause");
    }

    [Fact]
    public void Pause_this_role_holds_the_selected_role_without_stopping_its_run()
    {
        var calls = new List<string[]>();
        using var window = Open(agents: Agents(4), run: arguments =>
        {
            calls.Add(arguments);
            return new TaskCompletionSource<string?>().Task;
        });
        WriteRunning("team1", "dev");
        window.Refresh();
        SelectAgent(window, 3);

        Assert.True(window.NewKeyDownEvent(new Key('h')));

        Assert.Equal([["pause", "team1", "dev"]], calls);
        Assert.Equal("Pausing this role…", window.Message.Says);
    }

    [Fact]
    public void Pause_this_role_reads_Let_this_role_start_again_once_held_which_resumes_it()
    {
        var calls = new List<string[]>();
        using var window = Open(agents: Agents(2), run: arguments =>
        {
            calls.Add(arguments);
            if (arguments[0] == "pause")
                WriteHold(arguments[1], arguments[2]);
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        SelectAgent(window, 1);
        Assert.True(window.Commands.IsEnabled("agent.hold"));
        Assert.Equal("Pause selected agent's role", Label(window, "agent.hold"));

        window.Commands.Execute("agent.hold");
        window.Refresh();

        Assert.True(window.Panes[1].Held);
        Assert.Equal("Let selected agent's role start again", Label(window, "agent.hold"));
        Assert.Equal("Let t_his role start again", window.MenuItems.Single(item => item.Id == "agent.hold").Item.Title);

        window.Commands.Execute("agent.hold");

        Assert.Equal([["pause", "team0", "dev"], ["resume", "team0", "dev"]], calls);
    }

    [Fact]
    public void i_interrupts_the_selected_agents_run_and_says_so_while_it_runs()
    {
        var calls = new List<string[]>();
        using var window = Open(agents: Agents(4), run: arguments =>
        {
            calls.Add(arguments);
            return new TaskCompletionSource<string?>().Task;
        });
        WriteRunning("team1", "dev");
        window.Refresh();
        SelectAgent(window, 3);

        Assert.True(window.NewKeyDownEvent(new Key('i')));

        Assert.Equal([["stop", "team1", "dev"]], calls);
        Assert.Equal("Interrupting…", window.Message.Says);
    }

    [Fact]
    public void Interrupting_a_run_reaches_its_pane_on_the_next_refresh()
    {
        using var window = Open(agents: Agents(2), run: arguments =>
        {
            WriteHold(arguments[1], arguments[2]);
            return Task.FromResult<string?>(null);
        });
        WriteRunning("team0", "dev");
        window.Refresh();
        SelectAgent(window, 1);

        window.Commands.Execute("agent.interrupt");
        window.Refresh();

        Assert.True(window.Panes[1].Held);
        Assert.Equal("Let selected agent start again", Label(window, "agent.interrupt"));
    }

    [Fact]
    public void A_held_role_is_offered_Let_it_start_again_which_resumes_it()
    {
        var calls = new List<string[]>();
        WriteHold("team0", "dev");
        using var window = Open(agents: Agents(2), run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        SelectAgent(window, 1);

        Assert.Equal("Let selected agent start again", Label(window, "agent.interrupt"));
        Assert.True(window.NewKeyDownEvent(new Key('i')));

        Assert.Equal([["resume", "team0", "dev"]], calls);
    }

    [Fact]
    public void Interrupt_is_enabled_only_for_a_selected_role_that_is_running_or_held()
    {
        WriteHold("team0", "dev");
        WriteRunning("team1", "lead");
        using var window = Open(agents: Agents(4));
        window.Refresh();
        Assert.False(window.Commands.IsEnabled("agent.interrupt"));

        var enabled = Enumerable.Range(0, 4).Select(_ =>
        {
            window.NewKeyDownEvent(Key.Tab);
            return window.Commands.IsEnabled("agent.interrupt");
        }).ToList();

        Assert.Equal([false, true, true, false], enabled);
    }

    [Fact]
    public void Interrupt_stays_on_the_selected_agent_while_the_menu_has_focus()
    {
        var calls = new List<string[]>();
        using var window = Open(agents: Agents(2), run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        WriteRunning("team0", "dev");
        window.Refresh();
        SelectAgent(window, 1);

        window.HasFocus = false;
        window.Refresh();

        Assert.DoesNotContain(window.Panes, pane => pane.HasFocus);
        Assert.True(window.Commands.IsEnabled("agent.interrupt"));
        window.Commands.Execute("agent.interrupt");
        Assert.Equal([["stop", "team0", "dev"]], calls);
    }

    [Fact]
    public void i_does_nothing_on_an_idle_role()
    {
        var calls = 0;
        using var window = Open(agents: Agents(2), run: _ =>
        {
            calls++;
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        SelectAgent(window, 1);

        Assert.False(window.NewKeyDownEvent(new Key('i')));
        window.Commands.Execute("agent.interrupt");

        Assert.Equal(0, calls);
    }

    [Fact]
    public void An_interrupt_that_failed_shows_the_file_in_red()
    {
        using var window = Open(
            agents: Agents(2),
            run: _ => Task.FromResult<string?>("a-team stop: can't write /nope/team0.json"));
        WriteRunning("team0", "dev");
        window.Refresh();
        SelectAgent(window, 1);

        window.Commands.Execute("agent.interrupt");
        window.Refresh();

        Assert.Equal("a-team stop: can't write /nope/team0.json", window.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), window.Message.SchemeName);
        Assert.False(window.Panes[1].Held);
        Assert.Equal("Interrupt selected agent", Label(window, "agent.interrupt"));
    }

    [Fact]
    public void Interrupt_adds_nothing_to_either_hint_bar()
    {
        using var window = Open(agents: Agents(4));

        Assert.All(
            [Mode.Grid, Mode.Expanded, Mode.Work],
            mode => Assert.DoesNotContain("k:", DashboardWindow.Hints("1.2.3", mode, window.Commands)));
    }

    [Fact]
    public void i_on_a_run_with_a_session_hands_it_to_attach_instead_of_stopping_it()
    {
        var calls = new List<string[]>();
        var handed = new List<Handover>();
        using var window = Open(agents: Agents(4), handOver: handed.Add, run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        WriteRunning("team1", "dev");
        WriteSession("team1", "dev");
        window.Refresh();
        SelectAgent(window, 3);

        Assert.True(window.NewKeyDownEvent(new Key('i')));

        var handover = Assert.IsType<AttachHandover>(Assert.Single(handed));
        Assert.Equal(["attach", "team1", "dev"], handover.Arguments);
        Assert.Equal(Area.Dashboard, handover.Area);
        Assert.Empty(calls);
    }

    [Fact]
    public void i_on_a_held_role_with_a_session_lets_it_start_again_rather_than_attaching()
    {
        var calls = new List<string[]>();
        var handed = new List<Handover>();
        WriteHold("team0", "dev");
        WriteSession("team0", "dev");
        using var window = Open(agents: Agents(2), handOver: handed.Add, run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        SelectAgent(window, 1);

        Assert.True(window.NewKeyDownEvent(new Key('i')));

        Assert.Equal([["resume", "team0", "dev"]], calls);
        Assert.Empty(handed);
    }

    [Fact]
    public void i_does_not_attach_to_a_finished_run()
    {
        var handed = new List<Handover>();
        WriteSession("team0", "dev");
        using var window = Open(agents: Agents(2), handOver: handed.Add);
        window.Refresh();
        SelectAgent(window, 1);

        Assert.False(window.NewKeyDownEvent(new Key('i')));
        Assert.Empty(handed);
    }

    [Fact]
    public void Back_from_an_interrupt_the_grid_has_the_same_agent_selected()
    {
        using var window = Open(agents: Agents(4), resume: new AttachHandover("team1", "dev"));
        window.Refresh();
        LayOut(window, 120, 40);
        window.FocusResumed();

        Assert.Equal(3, Selected(window));
        Assert.True(window.Agents.Visible);
        Assert.Equal("", window.Message.Says);
    }

    [Fact]
    public void A_session_that_would_not_resume_says_so_in_red_once_you_re_back()
    {
        using var window = Open(
            agents: Agents(4),
            resume: new AttachHandover("team1", "dev") { Failure = "attach team1 dev exited 1" });
        window.Refresh();
        LayOut(window, 120, 40);
        window.FocusResumed();

        Assert.Equal(3, Selected(window));
        Assert.Equal("attach team1 dev exited 1", window.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), window.Message.SchemeName);
    }

    private static string Label(DashboardWindow window, string id) =>
        window.Commands.Registered.Single(command => command.Id == id).Label;

    private static void SelectAgent(DashboardWindow window, int index)
    {
        for (var i = 0; i <= index; i++)
            window.NewKeyDownEvent(Key.Tab);
        Assert.Equal(index, Selected(window));
    }

    private static bool InView(DashboardWindow window, int index)
    {
        var cell = window.Panes[index].Frame;
        var view = window.Agents.Viewport;
        return cell.Top >= view.Y && cell.Bottom <= view.Y + view.Height;
    }

    private static (string, string)[] Agents(int count) =>
        [.. Enumerable.Range(0, count).Select(i => ($"team{i / 2}", i % 2 == 0 ? "lead" : "dev"))];

    private static Rectangle[] LayOut(DashboardWindow window, int width, int height)
    {
        window.Frame = new Rectangle(0, 0, width, height);
        window.Layout(new Size(width, height));
        return [.. window.Panes.Select(pane => pane.Frame)];
    }

    private static Rectangle AgentArea(DashboardWindow window) =>
        new(0, 0, window.Viewport.Width, window.Dispatcher.Frame.Y - window.Agents.Frame.Y);

    private static Button Hint(DashboardWindow window, string text) =>
        window.Status.Hints.Single(hint => hint.Text == text);

    private static int Selected(DashboardWindow window) =>
        window.Panes.ToList().FindIndex(pane => pane.HasFocus);

    private static void AssertTiles(Rectangle area, IReadOnlyList<Rectangle> cells)
    {
        var covered = new HashSet<(int X, int Y)>();
        foreach (var cell in cells)
        {
            Assert.True(area.Contains(cell), $"{cell} overhangs {area}");
            for (var x = cell.Left; x < cell.Right; x++)
                for (var y = cell.Top; y < cell.Bottom; y++)
                    Assert.True(covered.Add((x, y)), $"{cell} overlaps another at {x},{y}");
        }
        Assert.Equal(area.Width * area.Height, covered.Count);
    }

    private DashboardWindow Open(
        bool expandToolCalls = false,
        IReadOnlyList<(string, string)>? agents = null,
        Func<string[], Task<string?>>? run = null,
        string? keys = null,
        Func<string, Task<Reading>>? readWaiting = null,
        Action<string>? openUrl = null,
        Area area = Area.Dashboard,
        IconStyle auto = IconStyle.Unicode,
        Action<Handover>? handOver = null,
        Handover? resume = null)
    {
        Directory.CreateDirectory(_root);
        if (keys is not null)
        {
            Directory.CreateDirectory(Config);
            File.WriteAllText(Path.Combine(Config, "dashboard.json"), $"{{ \"keys\": {keys} }}");
        }
        var settings = new DashboardSettings(Config);
        if (expandToolCalls)
            settings.WriteExpandToolCalls(true);
        return new DashboardWindow(
            agents ?? [("a-team", "lead"), ("a-team", "dev")],
            _root,
            settings,
            new TeamConfigs(Config),
            run ?? (_ => Task.FromResult<string?>(null)),
            readWaiting ?? (_ => Task.FromResult(new Reading("[]", null))),
            _ => Task.FromResult(new Reading("{}", null)),
            openUrl ?? (_ => { }),
            (_, _) => null,
            (_, _, _, _) => { },
            area,
            auto,
            handOver,
            resume);
    }

    private string Config => Path.Combine(_root, "config");

    private void WriteDispatchLog(params string[] lines)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllLines(Path.Combine(_root, "dispatch.log"), lines);
    }

    private void WriteRunning(string team, string role)
    {
        var dir = Path.Combine(_root, team, role);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "pid"), Environment.ProcessId.ToString());
    }

    private void WriteSession(string team, string role)
    {
        var dir = Path.Combine(_root, team, role);
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "latest.jsonl"),
            "{\"type\":\"system\",\"subtype\":\"init\",\"session_id\":\"sess-" + team + role + "\"}\n");
    }

    private void WriteHold(string team, string role)
    {
        var teams = Path.Combine(Config, "teams");
        Directory.CreateDirectory(teams);
        File.WriteAllText(
            Path.Combine(teams, team + ".json"),
            "{\"dispatch\": {\"enabled\": true, \"hold\": [\"" + role + "\"]}}");
    }
}
