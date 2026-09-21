using System.Drawing;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

public class DashboardWindowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
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
    public void Two_teams_at_120_by_30_give_each_agent_a_readable_width()
    {
        using var window = Open(agents: Agents(4));

        var cells = LayOut(window, 120, 30);

        Assert.All(cells, cell => Assert.Equal(59, cell.Width));
    }

    [Fact]
    public void The_dispatcher_strip_stays_at_the_bottom_full_width()
    {
        using var window = Open(agents: Agents(4));
        LayOut(window, 120, 30);

        var strip = window.Dispatcher.Frame;

        Assert.Equal(new Rectangle(0, window.Viewport.Height - 6, window.Viewport.Width, 6), strip);
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
    public void Esc_with_nothing_expanded_is_left_alone_so_it_still_quits()
    {
        using var window = Open(agents: Agents(4));
        window.NewKeyDownEvent(Key.Tab);

        Assert.False(window.NewKeyDownEvent(Key.Esc));
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
    public void The_title_offers_Enter_to_expand_and_Esc_to_go_back_from_there()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal(
            "a-team 1.2.3 · arrows: select · Enter: expand · Ctrl+E: commands · Esc: quit",
            DashboardWindow.Hints("1.2.3", expanded: false, window.Commands));
        Assert.Equal(
            "a-team 1.2.3 · PgUp/PgDn: scroll · Ctrl+E: commands · Esc: back",
            DashboardWindow.Hints("1.2.3", expanded: true, window.Commands));
    }

    [Fact]
    public void Both_titles_fit_the_76_columns_an_80_column_window_gives_them()
    {
        using var window = Open(agents: Agents(4));

        Assert.All(
            new[] { false, true },
            expanded => Assert.InRange(DashboardWindow.Hints("1.2.3", expanded, window.Commands).Length, 1, 76));
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
                "Pause team0", "Commands", "Settings", "Back to the agent grid", "Quit",
            ],
            window.Commands.Registered.Select(command => command.Label));
    }

    [Fact]
    public void Settings_opens_with_s_and_Ctrl_comma_is_gone()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal(new Key('s'), window.Commands.Registered.Single(c => c.Id == "settings").Key);
        Assert.DoesNotContain(window.Commands.Registered, c => c.Key == new Key(',').WithCtrl);
        Assert.False(window.NewKeyDownEvent(new Key(',').WithCtrl));
        Assert.All(
            new[] { false, true },
            expanded => Assert.DoesNotContain("Ctrl+,", DashboardWindow.Hints("1.2.3", expanded, window.Commands)));
    }

    [Fact]
    public void The_window_title_follows_the_view_it_is_showing()
    {
        using var window = Open(agents: Agents(4));
        window.NewKeyDownEvent(Key.Tab);

        window.NewKeyDownEvent(Key.Enter);
        Assert.EndsWith("Esc: back", window.Title);

        window.NewKeyDownEvent(Key.Esc);
        Assert.EndsWith("Esc: quit", window.Title);
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
        Assert.Equal(window.Viewport.Height - 6, window.Dispatcher.Frame.Y);
    }

    [Fact]
    public void A_message_shrinks_the_grid_above_it_instead_of_covering_anything()
    {
        using var window = Open(agents: Agents(4), run: (_, _) => new TaskCompletionSource<string?>().Task);
        var before = LayOut(window, 120, 30);

        window.Commands.Execute("team.pause");
        var after = LayOut(window, 120, 30);

        Assert.Equal("Pausing…", window.Message.Text);
        Assert.Equal(new Rectangle(0, window.Viewport.Height - 1, window.Viewport.Width, 1), window.Message.Frame);
        Assert.Equal(window.Viewport.Height - 7, window.Dispatcher.Frame.Y);
        Assert.Equal(before.Sum(cell => cell.Height) - 2, after.Sum(cell => cell.Height));
        AssertTiles(AgentArea(window), after);
    }

    [Fact]
    public void Pausing_runs_a_team_pause_for_the_selected_agents_team_and_says_so_while_it_runs()
    {
        var calls = new List<(string Verb, string Team)>();
        var finish = new TaskCompletionSource<string?>();
        using var window = Open(agents: Agents(4), run: (verb, team) =>
        {
            calls.Add((verb, team));
            return finish.Task;
        });
        SelectAgent(window, 2);

        window.Commands.Execute("team.pause");

        Assert.Equal([("pause", "team1")], calls);
        Assert.Equal("Pausing…", window.Message.Text);
    }

    [Fact]
    public void A_second_go_is_refused_until_the_first_one_resolves()
    {
        var calls = 0;
        var finish = new TaskCompletionSource<string?>();
        using var window = Open(agents: Agents(4), run: (_, _) =>
        {
            calls++;
            return finish.Task;
        });

        window.Commands.Execute("team.pause");
        window.Commands.Execute("team.pause");
        Assert.Equal(1, calls);

        finish.SetResult(null);
        window.Refresh();
        window.Commands.Execute("team.pause");
        Assert.Equal(2, calls);
    }

    [Fact]
    public void A_command_that_worked_leaves_the_message_block_empty_again()
    {
        using var window = Open(agents: Agents(4));

        window.Commands.Execute("team.pause");
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(0, window.Message.Lines);
        Assert.Equal(window.Viewport.Height - 6, window.Dispatcher.Frame.Y);
    }

    [Fact]
    public void A_command_that_failed_shows_one_line_and_leaves_the_dashboard_usable()
    {
        using var window = Open(
            agents: Agents(4),
            run: (_, _) => Task.FromResult<string?>("a-team pause: can't write /nope/team0.json\nstack\ntrace"));

        window.Commands.Execute("team.pause");
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("a-team pause: can't write /nope/team0.json", window.Message.Text);
        Assert.Equal(1, window.Message.Lines);
        Assert.True(window.NewKeyDownEvent(Key.Tab));
        Assert.Equal(0, Selected(window));
    }

    [Fact]
    public void A_paused_team_is_offered_Resume_instead()
    {
        WriteTeam("team0", enabled: false);
        WriteTeam("team1", enabled: true);
        using var window = Open(agents: Agents(4));
        window.Refresh();

        Assert.Equal("Resume team0", Label(window, "team.pause"));
        SelectAgent(window, 2);
        Assert.Equal("Pause team1", Label(window, "team.pause"));
    }

    [Fact]
    public void Pausing_a_team_reaches_its_panes_on_the_next_refresh()
    {
        WriteTeam("team0", enabled: true);
        using var window = Open(agents: Agents(2), run: (_, team) =>
        {
            WriteTeam(team, enabled: false);
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        Assert.All(window.Panes, pane => Assert.False(pane.Paused));

        window.Commands.Execute("team.pause");
        window.Refresh();

        Assert.All(window.Panes, pane => Assert.True(pane.Paused));
        Assert.Equal("Resume team0", Label(window, "team.pause"));
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
        new(0, 0, window.Viewport.Width, window.Dispatcher.Frame.Y);

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
        Func<string, string, Task<string?>>? run = null)
    {
        Directory.CreateDirectory(_root);
        var settings = new DashboardSettings(Config);
        if (expandToolCalls)
            settings.WriteExpandToolCalls(true);
        return new DashboardWindow(
            agents ?? [("a-team", "lead"), ("a-team", "dev")],
            _root,
            settings,
            new TeamConfigs(Config),
            run ?? ((_, _) => Task.FromResult<string?>(null)));
    }

    private string Config => Path.Combine(_root, "config");

    private void WriteTeam(string team, bool enabled)
    {
        var teams = Path.Combine(Config, "teams");
        Directory.CreateDirectory(teams);
        File.WriteAllText(
            Path.Combine(teams, team + ".json"),
            "{\"dispatch\": {\"enabled\": " + (enabled ? "true" : "false") + "}}");
    }
}
