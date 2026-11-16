using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

public partial class WorkAreaTests
{
    /// <summary>team1's PR #130 holds up team0's #672, which holds up #673; team0's #828 holds up #831 alone.</summary>
    private static string Blocking(string team) => team == "team0"
        ? """
          [{"number": 672, "title": "Resizable Assistant", "status": "Ready", "url": "https://github.com/mentaldesk/team0/issues/672",
            "team": "team0", "turn": "upstream", "reason": "waiting on team1#130", "upstream": "team1#130: open",
            "chains": [[{"team": "team1", "refs": [130], "what": "team1#130", "shown": "open"}]], "frees": [673]},
           {"number": 828, "title": "Worth considering", "status": "In review", "url": "https://github.com/mentaldesk/team0/issues/828",
            "team": "team0", "turn": "you", "reason": "awaiting your acceptance", "pr": 829, "frees": [831]},
           {"number": 6, "title": "The agents can't say what they'd change", "status": "Idea",
            "url": "https://github.com/mentaldesk/team0/issues/6", "team": "team0", "turn": "you", "reason": "waiting to be ranked"}]
          """
        : """
          [{"number": 130, "title": "Pane width follows the window", "status": "In review", "url": "https://github.com/mentaldesk/team1/pull/130",
            "team": "team1", "turn": "you", "reason": "awaiting your acceptance", "pr": 130}]
          """;

    private DashboardWindow OpenBlocking(Func<bool>? blocking = null)
    {
        var window = Open(read: team => Task.FromResult(new Reading(blocking?.Invoke() == false ? Waiting(team) : Blocking(team), null)));
        window.Refresh();
        LayOut(window, 160, 30);
        return window;
    }

    [Fact]
    public void Work_shows_a_Priority_Inbox_of_my_moves_that_hold_up_other_work_ranked_by_what_each_frees()
    {
        using var window = OpenBlocking();

        Assert.True(window.Inbox.Visible);
        Assert.Equal("Priority Inbox 2", window.Inbox.Title);
        Assert.Equal(["team1  PR #130  review  frees team0 #672 · #673", "team0  #828     review  frees team0 #831"], window.Inbox.Lines);
        Assert.Equal(160, window.Inbox.FrameToScreen().Right);
        Assert.Equal(window.Inbox.Frame.X, window.Work.Frame.Width);
    }

    [Fact]
    public void The_inbox_shows_the_same_rows_whichever_team_tab_is_active()
    {
        using var window = OpenBlocking();
        window.Work.PickTab(0);
        var first = window.Inbox.Lines.ToList();

        window.Work.PickTab(1);

        Assert.Equal(first, window.Inbox.Lines);
    }

    [Fact]
    public void With_nothing_blocking_the_inbox_is_gone_and_comes_back_when_a_blocker_turns_up()
    {
        var blocking = false;
        using var window = OpenBlocking(() => blocking);

        Assert.False(window.Inbox.Visible);
        Assert.Equal(160, window.Work.Frame.Width);
        Assert.False(window.Commands.IsEnabled("view.inbox"));

        blocking = true;
        window.Commands.Execute("work.refresh");
        window.Refresh();
        LayOut(window, 160, 30);

        Assert.True(window.Inbox.Visible);
        Assert.True(window.Commands.IsEnabled("view.inbox"));
        Assert.True(window.Work.Frame.Width < 160);

        blocking = false;
        window.Commands.Execute("work.refresh");
        window.Refresh();
        LayOut(window, 160, 30);

        Assert.False(window.Inbox.Visible);
        Assert.Equal(160, window.Work.Frame.Width);
    }

    [Fact]
    public void Ctrl_G_P_focuses_the_inbox_and_its_row_puts_the_whole_chain_on_the_status_bar()
    {
        using var window = OpenBlocking();

        window.Press("Ctrl+G P");

        Assert.True(window.Inbox.List.HasFocus);
        Assert.Equal("⛓ team1 PR #130 → team0 #672 → team0 #673", window.Status.RestingText);
        window.NewKeyDownEvent(Key.F6);
        Assert.False(window.Inbox.HasFocus);
        Assert.True(window.Work.HasFocus);
        window.NewKeyDownEvent(Key.F6);
        Assert.True(window.Inbox.List.HasFocus);
    }

    [Fact]
    public void Enter_on_a_row_goes_to_its_card_on_its_team_s_tab()
    {
        using var window = OpenBlocking();
        window.Work.PickTab(0);
        window.Press("Ctrl+G P");

        window.NewKeyDownEvent(Key.Enter);

        Assert.Equal("team1", window.Work.Current!.Team);
        Assert.Equal(130, window.Work.Selected!.Number);
        Assert.True(window.Work.HasFocus);
    }

    [Fact]
    public void The_inbox_resizes_and_resets_to_the_width_it_had()
    {
        using var window = OpenBlocking();
        var before = window.Inbox.Frame.Width;
        window.Press("Ctrl+G P");

        window.Commands.Execute("app.widenPane");
        LayOut(window, 160, 30);
        var widened = window.Inbox.Frame.Width;
        window.Commands.Execute("app.resetPaneWidth");
        LayOut(window, 160, 30);

        Assert.Equal(before + 5, widened);
        Assert.Equal(before, window.Inbox.Frame.Width);
    }

    [Fact]
    public void The_arrow_keys_move_down_the_inbox_and_a_read_leaves_focus_in_it()
    {
        using var window = OpenBlocking();
        window.Press("Ctrl+G P");

        window.NewKeyDownEvent(Key.CursorDown);
        window.Commands.Execute("work.refresh");
        window.Refresh();

        Assert.True(window.Inbox.List.HasFocus);
        Assert.Equal(828, window.Inbox.Selected!.Item.Number);
    }
}
