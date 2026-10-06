using System.Drawing;
using System.Text;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
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
    public void A_team_whose_check_fails_says_so_on_its_panes_and_once_in_the_message_bar()
    {
        var answer = new TaskCompletionSource<TeamHealth>();
        var ran = 0;
        var checks = new TeamChecks(_ => { ran++; return answer.Task; }, _ => "{}");
        using var window = Open(checks: checks);
        window.Refresh();
        Assert.All(window.Panes, pane => Assert.DoesNotContain("⚠", pane.Title));

        answer.SetResult(new TeamHealth([new TeamProblem("checkout", "~/code/a-team/main isn't there"), new TeamProblem("status", "missing")]));
        window.Refresh();

        Assert.All(window.Panes, pane => Assert.StartsWith(Icons.Field(Icon.Misconfigured, IconStyle.Unicode), pane.Title));
        Assert.Equal("a-team: 2 checks failed — checkout, status", window.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), window.Message.SchemeName);
        window.Refresh();
        Assert.Equal(1, ran);
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
    public void Tab_steps_through_every_agent_in_reading_order_then_the_dispatcher_and_wraps()
    {
        using var window = Open(agents: Agents(4));

        var visited = Enumerable.Range(0, 6).Select(_ =>
        {
            window.NewKeyDownEvent(Key.Tab);
            return Stop(window);
        });

        Assert.Equal([0, 1, 2, 3, Dispatcher, 0], visited);
    }

    [Fact]
    public void Shift_Tab_steps_back_through_reading_order_and_wraps()
    {
        using var window = Open(agents: Agents(4));

        var visited = Enumerable.Range(0, 6).Select(_ =>
        {
            window.NewKeyDownEvent(Key.Tab.WithShift);
            return Stop(window);
        });

        Assert.Equal([Dispatcher, 3, 2, 1, 0, Dispatcher], visited);
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
        Assert.True(window.NewKeyDownEvent(Key.CursorRight));
        Assert.Equal(3, Selected(window));
    }

    [Fact]
    public void Down_from_the_bottom_row_selects_the_dispatcher_and_Up_goes_back()
    {
        using var window = Open(agents: Agents(4));
        window.NewKeyDownEvent(Key.Tab);
        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(Key.CursorRight);

        Assert.True(window.NewKeyDownEvent(Key.CursorDown));
        Assert.Equal(Dispatcher, Stop(window));
        Assert.StartsWith(Icons.Field(Icon.Selected, IconStyle.Unicode), window.Dispatcher.Title);
        Assert.True(window.NewKeyDownEvent(Key.CursorDown));
        Assert.Equal(Dispatcher, Stop(window));

        window.NewKeyDownEvent(Key.CursorUp);
        Assert.Equal(3, Stop(window));
        Assert.DoesNotContain(Icons.Field(Icon.Selected, IconStyle.Unicode), window.Dispatcher.Title);
    }

    [Fact]
    public void Enter_on_the_dispatcher_expands_it_over_the_agents_and_Esc_collapses_it()
    {
        using var window = Open(agents: Agents(4));
        window.NewKeyDownEvent(Key.Tab.WithShift);
        LayOut(window, 120, 30);
        var collapsed = window.Dispatcher.Frame;

        Assert.True(window.NewKeyDownEvent(Key.Enter));
        LayOut(window, 120, 30);

        Assert.True(window.DispatcherExpanded);
        Assert.False(window.Agents.Visible);
        Assert.Equal(new Rectangle(0, 1, window.Viewport.Width, window.Viewport.Height - 2), window.Dispatcher.Frame);
        Assert.True(window.WholeDispatchLog.Visible);

        Assert.True(window.NewKeyDownEvent(Key.Esc));
        LayOut(window, 120, 30);

        Assert.False(window.DispatcherExpanded);
        Assert.True(window.Agents.Visible);
        Assert.Equal(collapsed, window.Dispatcher.Frame);
        Assert.Equal(Dispatcher, Stop(window));
    }

    [Fact]
    public void The_dispatcher_opens_collapsed_and_unselected()
    {
        using var window = Open(agents: Agents(4));

        Assert.False(window.DispatcherExpanded);
        Assert.False(window.DispatcherSelected);
        Assert.False(window.Commands.IsEnabled("agent.collapse"));
    }

    [Fact]
    public void Expanded_the_dispatcher_shows_the_whole_log_and_scrolls_it()
    {
        WriteDispatchLog([.. Enumerable.Range(1, 100).Select(n => $"2026-10-04T10:00:00Z a-team dev: line {n}")]);
        using var window = Open(agents: Agents(4));
        window.NewKeyDownEvent(Key.Tab.WithShift);
        window.NewKeyDownEvent(Key.Enter);
        LayOut(window, 120, 30);
        window.Refresh();

        Assert.Equal(100, window.WholeDispatchLog.Lines.Count);
        Assert.Equal(ScrollBarVisibilityMode.Auto, window.WholeDispatchLog.VerticalScrollBar.VisibilityMode);
        Assert.True(window.WholeDispatchLog.Following);

        Assert.True(window.NewKeyDownEvent(Key.Home));
        Assert.False(window.WholeDispatchLog.Following);
        Assert.True(window.NewKeyDownEvent(Key.PageDown));
        Assert.True(window.NewKeyDownEvent(Key.End));
        Assert.True(window.WholeDispatchLog.Following);
    }

    [Fact]
    public void Tab_from_an_expanded_agent_reads_the_dispatcher_expanded_and_on_round_to_the_first_agent()
    {
        using var window = Open(agents: Agents(2));
        window.NewKeyDownEvent(Key.Tab);
        window.NewKeyDownEvent(Key.Tab);
        window.NewKeyDownEvent(Key.Enter);

        window.NewKeyDownEvent(Key.Tab);
        Assert.Null(window.ExpandedAgent);
        Assert.True(window.DispatcherExpanded);

        window.NewKeyDownEvent(Key.Tab);
        Assert.False(window.DispatcherExpanded);
        Assert.Equal(0, window.ExpandedAgent);
    }

    [Fact]
    public void Output_newer_than_the_last_dispatch_line_fails_the_title_and_shows_at_the_foot_of_the_whole_log()
    {
        WriteDispatchLog("2026-10-04T10:00:00Z a-team dev: started 1 on 0.1.12");
        File.SetLastWriteTimeUtc(Path.Combine(_root, "dispatch.log"), DateTime.UtcNow.AddMinutes(-3));
        File.WriteAllLines(Path.Combine(_root, "launchd.log"), ["jq: parse error"]);
        using var window = Open(agents: Agents(2));
        window.Refresh();

        Assert.EndsWith(" · last pass failed", window.Dispatcher.Title);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), window.Dispatcher.SchemeName);

        window.NewKeyDownEvent(Key.Tab.WithShift);
        window.NewKeyDownEvent(Key.Enter);

        Assert.DoesNotContain("last pass failed", window.Dispatcher.Title);
        Assert.Equal(
            [
                new LogLine("10:00 a-team dev: started 1 on 0.1.12", LogLineKind.Prose),
                new LogLine("── the dispatcher's own output since then ──", LogLineKind.DispatchFailed),
                new LogLine("jq: parse error", LogLineKind.DispatchFailed),
            ],
            window.WholeDispatchLog.Lines);
    }

    [Fact]
    public void Output_older_than_the_last_dispatch_line_shows_neither()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllLines(Path.Combine(_root, "launchd.log"), ["jq: parse error"]);
        File.SetLastWriteTimeUtc(Path.Combine(_root, "launchd.log"), DateTime.UtcNow.AddMinutes(-3));
        WriteDispatchLog("2026-10-04T10:00:00Z a-team dev: started 1 on 0.1.12");
        using var window = Open(agents: Agents(2));
        window.Refresh();

        Assert.DoesNotContain("last pass failed", window.Dispatcher.Title);
        window.NewKeyDownEvent(Key.Tab.WithShift);
        window.NewKeyDownEvent(Key.Enter);
        Assert.Single(window.WholeDispatchLog.Lines);
    }

    [Fact]
    public void Up_and_down_step_through_a_panes_runs_before_moving_on_to_the_next_pane()
    {
        WriteRun("team0", "dev", 246, 100);
        WriteRun("team0", "dev", 303, 300);
        using var window = Open(agents: Agents(4));
        window.Refresh();
        window.NewKeyDownEvent(Key.Tab);
        window.NewKeyDownEvent(Key.CursorRight);

        window.NewKeyDownEvent(Key.CursorUp);
        Assert.Equal(1, Selected(window));
        Assert.EndsWith("#246 Task 246", window.Panes[1].Title);

        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(1, Selected(window));
        Assert.EndsWith("#303 Task 303", window.Panes[1].Title);

        window.NewKeyDownEvent(Key.CursorDown);
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
    public void Up_and_down_select_agents_on_the_grid_and_log_lines_while_expanded()
    {
        using var window = Open(agents: Agents(4));
        window.NewKeyDownEvent(Key.Tab);

        Assert.True(window.Commands.IsEnabled("agent.up"));
        Assert.True(window.Commands.IsEnabled("agent.down"));
        Assert.False(window.Commands.IsEnabled("log.lineUp"));
        Assert.False(window.Commands.IsEnabled("log.copyLines"));

        window.NewKeyDownEvent(Key.Enter);

        Assert.False(window.Commands.IsEnabled("agent.up"));
        Assert.False(window.Commands.IsEnabled("agent.down"));
        Assert.DoesNotContain(window.Commands.Enabled, command => command.Id is "agent.up" or "agent.down");
        Assert.All(
            new[] { "log.lineUp", "log.lineDown", "log.extendUp", "log.extendDown", "log.copyLines", "log.copyAll" },
            id => Assert.True(window.Commands.IsEnabled(id), id));
    }

    [Fact]
    public void l_copies_the_highlighted_lines_in_full_and_says_so()
    {
        var clipboard = new FakeClipboard();
        WriteLog("a-team", "dev",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"reading"}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash","input":{"command":"cat <<'PY'\nprint(1)\nPY"}}]}}""");
        using var window = Open(clipboard: clipboard);
        window.Refresh();
        SelectAgent(window, 1);
        window.NewKeyDownEvent(Key.Enter);

        Assert.True(window.NewKeyDownEvent(new Key('l')));

        Assert.Equal("cat <<'PY'\nprint(1)\nPY", clipboard.GetClipboardData());
        Assert.Equal("copied 22 characters", window.Message.Says);

        window.NewKeyDownEvent(Key.CursorUp);
        window.NewKeyDownEvent(Key.CursorUp.WithShift);
        window.NewKeyDownEvent(new Key('l'));
        Assert.Equal("reading\n", clipboard.GetClipboardData());
        Assert.Equal("copied 2 lines, 8 characters", window.Message.Says);
    }

    [Fact]
    public void L_copies_the_whole_log()
    {
        var clipboard = new FakeClipboard();
        WriteLog("a-team", "dev",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Read","input":{"file_path":"a"}},{"type":"tool_use","name":"Read","input":{"file_path":"b"}}]}}""");
        using var window = Open(clipboard: clipboard);
        window.Refresh();
        SelectAgent(window, 1);
        window.NewKeyDownEvent(Key.Enter);

        Assert.True(window.NewKeyDownEvent(new Key('L')));

        Assert.Equal("a\nb", clipboard.GetClipboardData());
    }

    [Fact]
    public void e_writes_the_whole_session_to_a_file_and_hands_it_to_the_editor()
    {
        var handed = new List<Handover>();
        WriteLog("a-team", "dev",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"reading"}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash","input":{"command":"cat <<'PY'\nprint(1)\nPY"}}]}}""");
        using var window = Open(handOver: handed.Add);
        window.Refresh();
        SelectAgent(window, 1);

        Assert.False(window.Commands.IsEnabled("log.editor"));
        window.NewKeyDownEvent(Key.Enter);
        Assert.True(window.NewKeyDownEvent(new Key('e')));

        var handover = Assert.IsType<EditorHandover>(Assert.Single(handed));
        try
        {
            Assert.Equal(("a-team", "dev", Area.Dashboard), (handover.Team, handover.Role, handover.Area));
            Assert.Equal(handover.File, handover.Arguments[^1]);
            Assert.Equal(["reading", "", "Bash cat <<'PY'", "print(1)", "PY"], File.ReadAllLines(handover.File));
        }
        finally
        {
            File.Delete(handover.File);
        }
    }

    [Fact]
    public void Back_from_the_editor_the_pane_is_expanded_where_you_left_it()
    {
        var handed = new List<Handover>();
        WriteLog("a-team", "dev", """{"type":"assistant","message":{"content":[{"type":"text","text":"one\ntwo\nthree"}]}}""");
        using (var window = Open(handOver: handed.Add))
        {
            window.Refresh();
            SelectAgent(window, 1);
            window.NewKeyDownEvent(Key.Enter);
            window.NewKeyDownEvent(Key.CursorUp);
            window.NewKeyDownEvent(Key.CursorUp);
            window.NewKeyDownEvent(Key.CursorUp.WithShift);
            window.NewKeyDownEvent(new Key('e'));
        }
        var handover = Assert.IsType<EditorHandover>(Assert.Single(handed));
        File.Delete(handover.File);

        using var back = Open(resume: handover);
        back.Refresh();
        LayOut(back, 120, 40);
        back.FocusResumed();

        Assert.Equal(1, back.ExpandedAgent);
        Assert.Equal(1, Selected(back));
        Assert.Equal("one\ntwo", back.Panes[1].CopySelection().Text);
        Assert.Contains("[scrolled]", back.Panes[1].Title, StringComparison.Ordinal);
    }

    [Fact]
    public void An_editor_that_failed_says_so_in_red_once_you_re_back()
    {
        var place = new PanePlace(null, new LogPlace([], 0, true, 0, 0, false));
        using var window = Open(resume: new EditorHandover("a-team", "dev", place, "x.log", ["vim"]) { Failure = "vim exited 1" });
        window.Refresh();
        LayOut(window, 120, 40);
        window.FocusResumed();

        Assert.Equal(1, window.ExpandedAgent);
        Assert.Equal("vim exited 1", window.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), window.Message.SchemeName);
    }

    [Fact]
    public void With_no_clipboard_copying_says_so_in_red_and_nothing_moves()
    {
        WriteLog("a-team", "dev", """{"type":"assistant","message":{"content":[{"type":"text","text":"one\ntwo"}]}}""");
        using var window = Open(clipboard: new FakeClipboard(isSupportedAlwaysFalse: true));
        window.Refresh();
        SelectAgent(window, 1);
        window.NewKeyDownEvent(Key.Enter);
        window.NewKeyDownEvent(Key.CursorUp);

        window.NewKeyDownEvent(new Key('l'));

        Assert.Equal("there's no clipboard to copy to", window.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), window.Message.SchemeName);
        Assert.Equal("two", window.Panes[1].CopySelection().Text);
    }

    [Fact]
    public void Expanding_again_starts_at_the_tail_following()
    {
        WriteLog("a-team", "dev", """{"type":"assistant","message":{"content":[{"type":"text","text":"one\ntwo\nthree"}]}}""");
        using var window = Open();
        window.Refresh();
        SelectAgent(window, 1);
        window.NewKeyDownEvent(Key.Enter);
        window.NewKeyDownEvent(Key.CursorUp);
        window.NewKeyDownEvent(Key.CursorUp);

        window.NewKeyDownEvent(Key.Esc);
        window.NewKeyDownEvent(Key.Enter);

        Assert.DoesNotContain("[scrolled]", window.Panes[1].Title, StringComparison.Ordinal);
        window.NewKeyDownEvent(Key.CursorUp);
        Assert.Equal("three", window.Panes[1].CopySelection().Text);
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
    public void Every_action_the_dashboard_has_is_a_command_you_can_run_by_name()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal(
            [
                "Select the next agent", "Select the previous agent", "Select the agent to the right",
                "Select the agent to the left", "Select the agent below", "Select the agent above",
                "Select the next line of the log", "Select the line above in the log", "Extend the selection down",
                "Extend the selection up", "Expand the selected agent", "Scroll the log up", "Scroll the log down",
                "Jump to the top of the log", "Jump to the bottom of the log", "Show tool calls in full",
                "Copy the selected lines", "Copy the whole log", "Open the whole log in your editor",
                "Select the column to the right", "Select the column to the left", "Select the card below",
                "Select the card above", "Open", "Set priority", "Try", "Open on GitHub",
                "Approve the pitch you're reading", "Accept", "Comment on the item you're reading",
                "Show only what's your move", "Read what's waiting again", "Dashboard", "Work",
                "Pause selected agent's role", "Interrupt selected agent", "Commands", "Settings", "Teams", "New team", "Keys", "Guide", "About", "Back to the agent grid", "Quit",
            ],
            window.Commands.Registered.Select(command => command.Label));
    }

    [Fact]
    public void Help_opens_with_F1()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal(Key.F1, window.Commands.Registered.Single(c => c.Id == "help").Key);
    }

    [Theory]
    [InlineData(Area.Work, GuideDialog.Work)]
    [InlineData(Area.Dashboard, GuideDialog.Dashboard)]
    public void Guide_opens_on_the_page_for_the_area_and_has_no_key(Area area, string page)
    {
        var opened = new List<string>();
        using var window = Open(agents: Agents(4), area: area, showGuide: opened.Add);

        window.Commands.Execute("guide");

        Assert.Equal([page], opened);
        Assert.Equal(Key.Empty, window.Commands.KeyFor("guide"));
    }

    [Fact]
    public void Settings_opens_with_s_and_Ctrl_comma_is_gone()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal(new Key('s'), window.Commands.Registered.Single(c => c.Id == "settings").Key);
        Assert.DoesNotContain(window.Commands.Registered, c => c.Key == new Key(',').WithCtrl);
        Assert.False(window.NewKeyDownEvent(new Key(',').WithCtrl));
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
    public void The_commands_list_names_the_key_the_file_bound()
    {
        using var window = Open(agents: Agents(4), keys: "{ \"commands\": \"Ctrl+K\" }");

        using var dialog = new CommandsDialog(window.Commands.Registered);

        Assert.Contains(dialog.Matches.Select(command => command.Key), key => key == Key.K.WithCtrl);
    }

    [Fact]
    public void The_status_bar_names_no_keys_and_no_version_on_the_grid_expanded_or_in_Work()
    {
        using var window = Open(agents: Agents(4));
        window.NewKeyDownEvent(Key.Tab);
        AssertNoHints(window);

        window.NewKeyDownEvent(Key.Enter);
        AssertNoHints(window);

        window.Commands.Execute("view.work");
        AssertNoHints(window);
    }

    [Fact]
    public void The_status_bar_is_the_last_row_of_the_window_in_a_band_of_its_own()
    {
        using var window = Open(agents: Agents(4));

        LayOut(window, 120, 30);

        Assert.Equal(new Rectangle(0, window.Viewport.Height - 1, window.Viewport.Width, 1), window.Status.Frame);
        Assert.Equal(StatusBar.Scheme, window.Status.SchemeName);
        Assert.Equal(0, window.Message.Lines);
    }

    [Fact]
    public void A_message_shows_at_the_left_of_the_status_bar_in_its_colours_and_takes_no_row_of_its_own()
    {
        using var window = Open(agents: Agents(4), run: _ => new TaskCompletionSource<string?>().Task);
        LayOut(window, 120, 30);
        var status = window.Status.Frame;
        var dispatcher = window.Dispatcher.Frame;
        SelectAgent(window, 0);

        window.Commands.Execute("agent.hold");
        LayOut(window, 120, 30);

        Assert.Equal("Pausing this role…", window.Message.Says);
        Assert.Same(window.Status, window.Message.SuperView);
        Assert.Equal(new Rectangle(0, 0, window.Status.Viewport.Width, 1), window.Message.Frame);
        Assert.Equal(StatusBar.Scheme, window.Message.SchemeName);
        Assert.Equal(status, window.Status.Frame);
        Assert.Equal(dispatcher, window.Dispatcher.Frame);
    }

    [Fact]
    public void Clicking_a_hint_runs_the_command_it_names()
    {
        using var bar = new StatusBar();
        string? ran = null;
        bar.Show("", [new HintedCommand("save", "Enter save")], id => (ran = id) is not null);

        bar.Hints.Single().InvokeCommand(Command.Accept);

        Assert.Equal("save", ran);
    }

    [Fact]
    public void A_hint_is_a_clickable_one_of_its_own_that_claims_no_key()
    {
        using var bar = new StatusBar();
        bar.Show("", [new HintedCommand("save", "Enter save"), new HintedCommand("cancel", "Esc cancel")], _ => true);

        Assert.Equal(2, bar.Hints.Count);
        Assert.All(bar.Hints, hint =>
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
    public void With_nothing_installed_the_dispatcher_frame_says_so_in_Error_and_its_lines_do_not()
    {
        using var window = Open(agents: Agents(4));
        LayOut(window, 120, 30);

        window.Refresh();

        Assert.Equal("dispatcher · nothing installed · run: a-team install", window.Dispatcher.Title);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), window.Dispatcher.SchemeName);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Base), window.DispatchLog.SchemeName);
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
    public void A_message_leaves_the_grid_its_full_height()
    {
        using var window = Open(agents: Agents(4), run: _ => new TaskCompletionSource<string?>().Task);
        var before = LayOut(window, 120, 30);
        SelectAgent(window, 0);

        window.Commands.Execute("agent.hold");
        var after = LayOut(window, 120, 30);

        Assert.Equal("Pausing this role…", window.Message.Says);
        Assert.Equal(window.Viewport.Height - 7, window.Dispatcher.Frame.Y);
        Assert.Equal(before, after);
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
        Assert.Contains("teams.new", ids);
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

    [Fact]
    public void With_two_runs_i_asks_to_stop_the_shown_run_by_its_task_and_stops_only_that_one()
    {
        var calls = new List<string[]>();
        var asked = new List<RunTask>();
        WriteRun("team0", "dev", 246, 100);
        WriteRun("team0", "dev", 303, 300);
        using var window = Open(agents: Agents(2), confirmStop: task =>
        {
            asked.Add(task);
            return true;
        }, run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        SelectAgent(window, 1);
        window.NewKeyDownEvent(Key.CursorUp);

        Assert.True(window.NewKeyDownEvent(new Key('i')));

        Assert.Equal(246, Assert.Single(asked).Number);
        Assert.Equal([["stop", "team0", "dev", "246"]], calls);
    }

    [Fact]
    public void Declining_the_stop_leaves_the_run_going()
    {
        var calls = 0;
        WriteRun("team0", "dev", 246, 100);
        WriteRun("team0", "dev", 303, 300);
        using var window = Open(agents: Agents(2), confirmStop: _ => false, run: _ =>
        {
            calls++;
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        SelectAgent(window, 1);

        window.Commands.Execute("agent.interrupt");

        Assert.Equal(0, calls);
        Assert.Equal("", window.Message.Says);
    }

    [Fact]
    public void With_two_runs_i_steps_into_the_shown_run_alone()
    {
        var handed = new List<Handover>();
        WriteRun("team0", "dev", 246, 100);
        WriteRun("team0", "dev", 303, 300);
        WriteSession("team0", "dev", 303);
        using var window = Open(agents: Agents(2), handOver: handed.Add, confirmStop: _ => throw new InvalidOperationException());
        window.Refresh();
        SelectAgent(window, 1);

        Assert.True(window.NewKeyDownEvent(new Key('i')));

        Assert.Equal(["attach", "team0", "dev", "303"], Assert.IsType<AttachHandover>(Assert.Single(handed)).Arguments);
    }

    [Fact]
    public void A_run_stopped_by_task_is_offered_Let_it_start_again_which_lets_only_that_task_go()
    {
        var calls = new List<string[]>();
        WriteRun("team0", "dev", 246, 100);
        WriteRun("team0", "dev", 303, 300);
        WriteRunHeld("team0", "dev", 303);
        using var window = Open(agents: Agents(2), run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        SelectAgent(window, 1);

        Assert.False(window.Panes[1].Held);
        Assert.Equal("Let selected agent start again", Label(window, "agent.interrupt"));
        Assert.Equal("Pause selected agent's role", Label(window, "agent.hold"));
        Assert.True(window.NewKeyDownEvent(new Key('i')));

        Assert.Equal([["resume", "team0", "dev", "303"]], calls);
    }

    [Fact]
    public void h_still_holds_the_whole_role_with_two_runs_going()
    {
        var calls = new List<string[]>();
        WriteRun("team0", "dev", 246, 100);
        WriteRun("team0", "dev", 303, 300);
        using var window = Open(agents: Agents(2), run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        SelectAgent(window, 1);
        window.NewKeyDownEvent(Key.CursorUp);

        window.Commands.Execute("agent.hold");

        Assert.Equal([["pause", "team0", "dev"]], calls);
    }

    [Fact]
    public void With_one_run_i_stops_the_role_as_before_without_asking()
    {
        var calls = new List<string[]>();
        WriteRun("team0", "dev", 246, 100);
        using var window = Open(agents: Agents(2), confirmStop: _ => throw new InvalidOperationException(), run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        SelectAgent(window, 1);

        window.Commands.Execute("agent.interrupt");

        Assert.Equal([["stop", "team0", "dev"]], calls);
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

    private static void AssertNoHints(DashboardWindow window)
    {
        Assert.Empty(window.Status.Hints);
        Assert.Equal("", window.Status.Says);
        Assert.DoesNotContain("a-team", window.Message.Says);
    }

    private const int Dispatcher = -2;

    private static int Stop(DashboardWindow window) => window.DispatcherSelected ? Dispatcher : Selected(window);

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
        Handover? resume = null,
        Action<string>? showGuide = null,
        Func<RunTask, bool>? confirmStop = null,
        IClipboard? clipboard = null,
        TeamChecks? checks = null)
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
            (_, _, _, _, _, _, _) => { },
            area,
            auto,
            handOver,
            resume,
            showGuide: showGuide,
            confirmStop: confirmStop,
            clipboard: clipboard,
            checks: checks);
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

    private void WriteRun(string team, string role, int task, long started)
    {
        var dir = Path.Combine(_root, team, role, "runs", task.ToString());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "pid"), Environment.ProcessId.ToString());
        File.WriteAllText(Path.Combine(dir, "last-start"), started.ToString());
        File.WriteAllText(Path.Combine(dir, "task"), $$"""{"number":{{task}},"title":"Task {{task}}"}""");
    }

    private void WriteSession(string team, string role, int? task = null)
    {
        var dir = task is { } n ? Path.Combine(_root, team, role, "runs", n.ToString()) : Path.Combine(_root, team, role);
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "latest.jsonl"),
            "{\"type\":\"system\",\"subtype\":\"init\",\"session_id\":\"sess-" + team + role + "\"}\n");
    }

    private void WriteLog(string team, string role, params string[] events)
    {
        var dir = Path.Combine(_root, team, role);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "latest.jsonl"), string.Join("\n", events) + "\n");
    }

    private void WriteRunHeld(string team, string role, int task)
    {
        var dir = Path.Combine(_root, team, role, "runs", task.ToString());
        File.WriteAllText(Path.Combine(dir, "pid"), "999999");
        File.WriteAllText(Path.Combine(dir, "held"), "100");
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
