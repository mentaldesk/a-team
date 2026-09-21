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

    private DashboardWindow Open(bool expandToolCalls = false, IReadOnlyList<(string, string)>? agents = null)
    {
        Directory.CreateDirectory(_root);
        var settings = new DashboardSettings(Path.Combine(_root, "config"));
        if (expandToolCalls)
            settings.WriteExpandToolCalls(true);
        return new DashboardWindow(agents ?? [("a-team", "lead"), ("a-team", "dev")], _root, settings);
    }
}
