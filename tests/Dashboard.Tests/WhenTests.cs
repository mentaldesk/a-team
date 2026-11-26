namespace ATeam.Dashboard.Tests;

public class WhenTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test +13", TimeSpan.FromHours(13), "Test +13", "Test +13");
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 1, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("2026-11-20T01:05:00Z", "14:05")]
    [InlineData("2026-11-09T01:05:00Z", "9 Nov 14:05")]
    [InlineData("2025-11-09T01:05:00Z", "9 Nov 2025 14:05")]
    public void A_conversation_s_heading_gives_its_local_time_with_the_day_and_year_only_when_they_differ(string at, string shown) =>
        Assert.Equal($"───── you · {shown} ─────",
            new Remark("you", DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture), "").Heading(times: When.At(Now, Zone)));

    [Theory]
    [InlineData("2026-11-08T23:30:00Z", "9 Nov")]
    [InlineData("2025-11-08T23:30:00Z", "9 Nov 2025")]
    [InlineData("2026-12-31T10:00:00Z", "31 Dec")]
    [InlineData("2026-12-31T11:00:00Z", "1 Jan 2027")]
    public void A_date_alone_is_the_local_day_with_the_year_only_outside_this_one(string at, string shown) =>
        Assert.Equal(shown, When.Day(DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture), When.At(Now, Zone)));
}
