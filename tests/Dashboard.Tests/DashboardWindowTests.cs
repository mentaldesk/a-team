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

    private DashboardWindow Open()
    {
        Directory.CreateDirectory(_root);
        return new DashboardWindow(
            [("a-team", "lead"), ("a-team", "dev")],
            _root,
            new DashboardSettings(Path.Combine(_root, "config")));
    }
}
