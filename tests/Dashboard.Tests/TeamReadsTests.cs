namespace ATeam.Dashboard.Tests;

public class TeamReadsTests
{
    [Fact]
    public void Each_read_is_handed_back_as_it_lands_without_waiting_for_the_rest()
    {
        var slow = new TaskCompletionSource<Reading>();
        var reads = new TeamReads(["a", "b"], team => team == "a" ? Task.FromResult(new Reading("[]", null)) : slow.Task);

        Assert.Equal(["a"], reads.Landed().Select(landed => landed.Team));
        Assert.False(reads.Done);
        Assert.Empty(reads.Landed());

        slow.SetResult(new Reading("", "offline"));

        Assert.Equal([("b", new Reading("", "offline"))], reads.Landed());
        Assert.True(reads.Done);
        Assert.True(reads.AnyRead);
    }

    [Fact]
    public void A_read_that_throws_lands_as_a_failure()
    {
        var reads = new TeamReads(["a"], _ => Task.FromException<Reading>(new InvalidOperationException("gh is missing")));

        Assert.Equal([("a", new Reading("", "gh is missing"))], reads.Landed());
        Assert.False(reads.AnyRead);
    }

    [Fact]
    public void A_board_that_came_back_short_says_how_short()
    {
        var reads = new TeamReads(["a"], _ => Task.FromResult(new Reading("",
            "board.sh: the board read came back incomplete (100 of 358 items), twice. Nothing was changed: end this run, and the next one reads the board again.")));

        Assert.Equal([("a", new Reading("", "Board read incomplete (100 of 358 items)"))], reads.Landed());
    }

    [Fact]
    public void A_team_already_being_read_isn_t_read_twice()
    {
        var asked = 0;
        var reads = new TeamReads(["a"], _ =>
        {
            asked++;
            return new TaskCompletionSource<Reading>().Task;
        });

        reads.Add("a");

        Assert.Equal(1, asked);
    }
}
