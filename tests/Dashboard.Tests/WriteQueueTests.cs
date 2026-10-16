namespace ATeam.Dashboard.Tests;

public class WriteQueueTests
{
    private readonly List<string[]> _calls = [];
    private readonly List<TaskCompletionSource<string?>> _finishes = [];

    private WriteQueue Queue() => new(arguments =>
    {
        _calls.Add(arguments);
        var finish = new TaskCompletionSource<string?>();
        _finishes.Add(finish);
        return finish.Task;
    });

    private static Write Accept(int number, int pr) => new(WriteKind.Accept, $"team0#{number}",
        ["board", "team0", "accept", "you", number.ToString()], $"Merging PR #{pr}…", $"#{number} is already being merged",
        $"merging PR #{pr}");

    [Fact]
    public void A_write_starts_at_once_and_a_second_waits_until_the_first_has_finished()
    {
        var queue = Queue();

        queue.Add(Accept(49, 122));
        queue.Add(Accept(50, 123));

        Assert.Equal([["board", "team0", "accept", "you", "49"]], _calls);
        Assert.Equal("Merging PR #122… · 1 more queued", queue.Status());

        _finishes[0].SetResult(null);
        Assert.Equal([("team0#49", (string?)null)], queue.Settle().Select(done => (done.Write.Key, done.Failure)));

        Assert.Equal(["49", "50"], _calls.Select(call => call[^1]));
        Assert.Equal("Merging PR #123…", queue.Status());
    }

    [Fact]
    public void A_refused_write_comes_back_with_why_and_the_next_still_runs()
    {
        var queue = Queue();
        queue.Add(Accept(49, 122));
        queue.Add(Accept(50, 123));

        _finishes[0].SetResult("board.sh: can't merge PR #122");
        var done = Assert.Single(queue.Settle());

        Assert.Equal("board.sh: can't merge PR #122", done.Failure);
        Assert.Equal(2, _calls.Count);
    }

    [Fact]
    public void A_write_that_throws_comes_back_as_refused()
    {
        var queue = new WriteQueue(_ => throw new InvalidOperationException("no a-team on PATH"));

        queue.Add(Accept(49, 122));

        Assert.Equal("no a-team on PATH", Assert.Single(queue.Settle()).Failure);
        Assert.Equal("", queue.Status());
    }

    [Fact]
    public void It_knows_the_write_in_hand_for_a_card_whether_it_runs_or_waits()
    {
        var queue = Queue();
        queue.Add(Accept(49, 122));
        queue.Add(Accept(50, 123));

        Assert.Equal("#49 is already being merged", queue.Holding("team0#49")?.Already);
        Assert.Equal("#50 is already being merged", queue.Holding("team0#50")?.Already);
        Assert.Null(queue.Holding("team0#51"));
    }

    [Fact]
    public void Dropping_leaves_the_write_under_way_and_deletes_what_the_dropped_ones_would_have_read()
    {
        var queue = Queue();
        var file = Path.GetTempFileName();
        queue.Add(Accept(49, 122));
        queue.Add(Accept(50, 123) with { Scratch = file });

        Assert.Equal(["team0#50"], queue.Drop().Select(write => write.Key));

        Assert.Equal("Merging PR #122…", queue.Status());
        Assert.False(File.Exists(file));
        _finishes[0].SetResult(null);
        queue.Settle();
        Assert.Single(_calls);
    }

    [Fact]
    public void A_finished_write_s_file_is_deleted()
    {
        var queue = Queue();
        var file = Path.GetTempFileName();
        queue.Add(Accept(49, 122) with { Scratch = file });

        _finishes[0].SetResult(null);
        queue.Settle();

        Assert.False(File.Exists(file));
    }
}

public class QuitDialogTests
{
    private static Write Write(string doing, string queued) =>
        new(WriteKind.Accept, queued, [], doing, "", queued);

    [Fact]
    public void It_names_the_write_under_way_and_the_one_it_drops()
    {
        using var dialog = QuitDialog.Create(Write("Merging PR #459…", "merging PR #459"), [Write("Approving #457…", "approving #457")]);

        Assert.Equal("Quit?", dialog.Title);
        Assert.Equal("Merging PR #459, and approving #457 is still queued. Quitting lets that finish and drops it.",
            string.Join(' ', dialog.Lines.Where(line => line.Length > 0)));
    }

    [Fact]
    public void F1_says_Esc_Stay_as_its_button_does()
    {
        using var dialog = QuitDialog.Create(Write("Merging PR #459…", "merging PR #459"), [Write("Approving #457…", "approving #457")]);

        Assert.Equal(("Esc", "Stay"), DialogTests.Rows(DialogTests.F1(dialog))[^1]);
    }

    [Fact]
    public void Several_queued_are_named_together() =>
        Assert.Equal("Merging PR #459, and approving #457 and setting #6 to High are still queued.\n\nQuitting lets that finish and drops them.",
            QuitDialog.Says(Write("Merging PR #459…", ""), [Write("", "approving #457"), Write("", "setting #6 to High")]));
}
