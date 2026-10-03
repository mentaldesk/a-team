namespace ATeam.Dashboard.Tests;

public class RunSelectionTests
{
    private static readonly DevRun A = Run("a", 100);
    private static readonly DevRun B = Run("b", 200);
    private static readonly DevRun C = Run("c", 300);

    [Fact]
    public void The_newest_run_is_shown_until_another_is_chosen()
    {
        var selection = new RunSelection();
        selection.Update([A, B, C]);

        Assert.Equal(C, selection.Current);
    }

    [Fact]
    public void Down_and_up_step_through_the_runs_and_stop_at_either_end()
    {
        var selection = new RunSelection();
        selection.Update([A, B, C]);

        Assert.False(selection.Move(+1));
        Assert.True(selection.Move(-1));
        Assert.Equal(B, selection.Current);
        Assert.True(selection.Move(-1));
        Assert.Equal(A, selection.Current);
        Assert.False(selection.Move(-1));
        Assert.Equal(A, selection.Current);
        Assert.True(selection.Move(+1));
        Assert.Equal(B, selection.Current);
    }

    [Fact]
    public void A_single_run_or_none_leaves_the_arrows_to_the_panes()
    {
        var selection = new RunSelection();
        Assert.False(selection.Move(+1));
        Assert.Null(selection.Current);

        selection.Update([A]);
        Assert.False(selection.Move(-1));
        Assert.False(selection.Move(+1));
        Assert.Equal(A, selection.Current);
    }

    [Fact]
    public void The_chosen_run_stays_chosen_as_others_start_and_finish()
    {
        var selection = new RunSelection();
        selection.Update([A, B]);
        selection.Move(-1);

        selection.Update([A, B, C]);
        Assert.Equal(A, selection.Current);

        selection.Update([A, C]);
        Assert.Equal(A, selection.Current);
        Assert.Equal([A, C], selection.Runs);
    }

    [Fact]
    public void A_chosen_run_that_finishes_stays_shown_until_you_move_off_it()
    {
        var selection = new RunSelection();
        selection.Update([A, B, C]);
        selection.Move(-1);

        selection.Update([A, C]);
        Assert.Equal(B, selection.Current);
        Assert.Equal([A, B, C], selection.Runs);
        Assert.False(selection.IsLive(B));

        Assert.True(selection.Move(+1));
        Assert.Equal(C, selection.Current);
        Assert.Equal([A, C], selection.Runs);
    }

    [Fact]
    public void With_nothing_running_the_last_run_stays_shown()
    {
        var selection = new RunSelection();
        selection.Update([A, C], latest: C);

        selection.Update([A], latest: C);
        Assert.Equal(C, selection.Current);
        Assert.Equal([A, C], selection.Runs);

        selection.Update([], latest: C);
        Assert.Equal(C, selection.Current);
        Assert.Equal([C], selection.Runs);
    }

    private static DevRun Run(string dir, long started) =>
        new(dir, new RunTask((int)started, dir), DateTimeOffset.FromUnixTimeSeconds(started));
}
