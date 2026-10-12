namespace ATeam.Dashboard.Tests;

public class OpenedTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 12, 7, 0, 0, TimeSpan.Zero);

    private readonly string _stateRoot = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_stateRoot))
            Directory.Delete(_stateRoot, recursive: true);
    }

    [Fact]
    public void On_first_run_no_card_has_been_opened()
    {
        Assert.Null(new Opened(_stateRoot).Last("a-team", 485));
    }

    [Fact]
    public void Each_card_keeps_its_own_time_across_a_restart()
    {
        new Opened(_stateRoot).Open("a-team", 485, At);
        new Opened(_stateRoot).Open("other", 485, At.AddHours(1));
        new Opened(_stateRoot).Open("a-team", 485, At.AddHours(2));

        var restarted = new Opened(_stateRoot);
        Assert.Equal(At.AddHours(2), restarted.Last("a-team", 485));
        Assert.Equal(At.AddHours(1), restarted.Last("other", 485));
        Assert.Null(restarted.Last("a-team", 486));
    }

    [Fact]
    public void A_file_that_wont_read_counts_as_never_opened()
    {
        Directory.CreateDirectory(_stateRoot);
        File.WriteAllText(Path.Combine(_stateRoot, "opened.json"), "not json");

        Assert.Null(new Opened(_stateRoot).Last("a-team", 485));
    }
}
