namespace ATeam.Dashboard.Tests;

public class AttentionTests
{
    private static readonly WaitingItem Task49 = new(49, "I can't change any of the keys", "In review", "", "team0", Pr: 122);

    private readonly Clock _clock = new();
    private readonly List<Visit> _visits = [];

    private Attention Timer() => new(_clock, _visits.Add);

    private void After(int minutes, Attention timer)
    {
        _clock.Now += TimeSpan.FromMinutes(minutes);
        timer.Key();
    }

    [Fact]
    public void A_visit_counts_from_opening_the_reader_to_closing_it_and_cuts_idle_stretches_at_five_minutes()
    {
        var timer = Timer();
        var opened = _clock.Now;

        timer.Open(Task49, "Review");
        After(2, timer);
        After(20, timer);
        After(1, timer);
        _clock.Now += TimeSpan.FromMinutes(1);
        timer.Close();

        var visit = Assert.Single(_visits);
        Assert.Equal(new Visit("team0", 49, "Review", opened, TimeSpan.FromMinutes(2 + 5 + 1 + 1), []), visit with { Did = [] });
        Assert.Equal("read", visit.What);
    }

    [Fact]
    public void A_try_counts_in_full_up_to_thirty_minutes()
    {
        var timer = Timer();
        timer.Open(Task49, "Review");
        After(1, timer);
        timer.Did("tried");
        timer.HandOver();
        _clock.Now += TimeSpan.FromMinutes(12);
        timer.TakeBack();
        timer.Open(Task49, "Review");
        After(1, timer);
        timer.Did("tried");
        timer.HandOver();
        _clock.Now += TimeSpan.FromMinutes(45);
        timer.TakeBack();
        timer.Did("accepted");
        timer.Close();

        var visit = Assert.Single(_visits);
        Assert.Equal(TimeSpan.FromMinutes(1 + 12 + 1 + 30), visit.Spent);
        Assert.Equal("tried, accepted", visit.What);
    }

    [Fact]
    public void Time_between_cards_is_Other_for_the_selected_lane_s_team_and_none_off_Work()
    {
        var timer = Timer();
        timer.Browsing("team0");
        After(3, timer);
        timer.Browsing("team1");
        After(2, timer);
        timer.Open(Task49, "Review");
        After(1, timer);
        timer.Browsing("team1");
        timer.Close();
        timer.Browsing(null);
        After(4, timer);
        timer.Browsing("team0");
        After(1, timer);
        timer.Stop();

        Assert.Equal(
            [("team0", (int?)null, "Other", 3), ("team1", null, "Other", 2), ("team0", 49, "Review", 1), ("team0", null, "Other", 1)],
            _visits.Select(visit => (visit.Team, visit.Item, visit.Gate, (int)visit.Spent.TotalMinutes)));
    }

    [Fact]
    public void A_visit_is_recorded_on_the_board_with_what_was_done_in_it()
    {
        var visit = new Visit("team0", 49, "Review", new DateTimeOffset(2026, 10, 3, 13, 55, 0, TimeSpan.FromHours(2)),
            TimeSpan.FromSeconds(421.6), ["commented", "tried", "commented"]);

        Assert.Equal(["board", "team0", "spent", "you", "49", "2026-10-03T11:55:00Z", "421", "Review", "commented, tried"], visit.Arguments);
        Assert.Equal(["board", "team1", "spent", "you", "-", "2026-10-03T11:55:00Z", "60", "Other", ""],
            (visit with { Team = "team1", Item = null, Gate = "Other", Spent = TimeSpan.FromMinutes(1) }).Arguments);
    }

    [Theory]
    [InlineData(0, "0m")]
    [InlineData(45 * 60 + 20, "45m")]
    [InlineData(3 * 3600 + 10 * 60, "3h 10m")]
    [InlineData(2 * 3600 + 5 * 60 + 40, "2h 06m")]
    public void Durations_read_in_hours_and_minutes(int seconds, string said) =>
        Assert.Equal(said, Visit.Duration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void A_card_s_time_is_split_by_the_gate_it_waits_at()
    {
        Assert.Equal("Triage", WorkView.GateOf(new WaitingItem(6, "", "Idea", "", "t")));
        Assert.Equal("Pitches", WorkView.GateOf(new WaitingItem(7, "", "Pitched", "", "t", Priority: "High", Pitch: true)));
        Assert.Equal("Questions", WorkView.GateOf(new WaitingItem(8, "", "Ready", "", "t", Question: "Which?")));
        Assert.Equal("Review", WorkView.GateOf(Task49));
        Assert.Equal("Review", WorkView.GateOf(new WaitingItem(9, "", "In review", "", "t")));
        Assert.Equal("Other", WorkView.GateOf(new WaitingItem(10, "", "Done", "", "t")));
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
