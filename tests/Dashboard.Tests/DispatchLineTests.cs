namespace ATeam.Dashboard.Tests;

public class DispatchLineTests
{
    [Theory]
    [InlineData("2026-09-20T17:01:17Z tuicode dev: started 41234: 1 task Ready", LogLineKind.Prose)]
    [InlineData("2026-09-20T17:01:17Z a-team lead: would start: 3 Ideas to shape", LogLineKind.DispatchSkipped)]
    [InlineData("2026-09-20T17:01:17Z tuicode dev: triggers failed: gh: Not Found (HTTP 404)", LogLineKind.DispatchFailed)]
    [InlineData("2026-09-20T17:01:17Z a-team dev: killed run 41234 after 121 minutes", LogLineKind.DispatchFailed)]
    [InlineData("2026-09-20T17:01:17Z something else entirely", LogLineKind.Prose)]
    public void A_line_is_coloured_by_what_it_says(string line, LogLineKind kind) =>
        Assert.Equal(kind, DispatchLine.KindOf(line));

    [Fact]
    public void The_timestamp_is_cut_to_the_hour_and_minute_it_already_says() =>
        Assert.Equal(
            "17:01 tuicode dev: started 41234: 1 task Ready",
            DispatchLine.Shorten("2026-09-20T17:01:17Z tuicode dev: started 41234: 1 task Ready"));

    [Theory]
    [InlineData("not a timestamp at all")]
    [InlineData("2026-09-20 17:01:17 tuicode dev: started")]
    [InlineData("")]
    public void A_line_that_does_not_start_with_one_is_left_alone(string line) =>
        Assert.Equal(line, DispatchLine.Shorten(line));
}
