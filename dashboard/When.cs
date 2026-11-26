using System.Globalization;
using MentalDesk.Tui;

namespace ATeam.Dashboard;

/// <summary>The style guide's <see cref="Times"/>, read at a given now, and the one way the dashboard writes a date alone.</summary>
public static class When
{
    public static Times System { get; } = new();

    public static Times At(DateTimeOffset now, TimeZoneInfo? zone = null) => new(new Fixed(now, zone ?? TimeZoneInfo.Local));

    /// <summary><c>9 Nov</c>, or <c>9 Nov 2025</c> outside this year.</summary>
    public static string Day(DateOnly day, DateOnly today) =>
        day.ToString(day.Year == today.Year ? "d MMM" : "d MMM yyyy", CultureInfo.InvariantCulture);

    public static string Day(DateTimeOffset at, Times? times = null)
    {
        var clock = (times ?? System).Clock;
        return Day(Local(at, clock.LocalTimeZone), Local(clock.GetUtcNow(), clock.LocalTimeZone));
    }

    /// <summary>A card's chip: its age in its largest whole unit, counting days past a week rather than giving the date.</summary>
    public static string Chip(DateTimeOffset since, Times times) =>
        times.Clock.GetUtcNow() - since is var waited && waited >= TimeSpan.FromDays(7) ? Times.Took(waited).Split(' ')[0] : times.Age(since);

    private static DateOnly Local(DateTimeOffset at, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

    private sealed class Fixed(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => zone;
    }
}
