using System.Text.Json;

namespace ATeam.Dashboard;

public enum TrendMeasure
{
    Waiting,
    Accepted,
}

public sealed record QueueCount(DateTimeOffset At, int Waiting);

/// <summary>One team's last fortnight, as <c>a-team board &lt;team&gt; trends</c> reports it.</summary>
public sealed record TeamRecord(DateTimeOffset? Since, IReadOnlyList<QueueCount> Queue, IReadOnlyList<DateTimeOffset> Accepted, decimal Cost)
{
    public const int Days = 14;

    public static readonly TeamRecord Empty = new(null, [], [], 0);

    public static TeamRecord? Of(Reading reading) =>
        reading.Failure is { Length: > 0 } ? null : Parse(reading.Output);

    /// <summary>The last <see cref="Days"/> days in <paramref name="zone"/>, today last.</summary>
    public static IReadOnlyList<DateOnly> Dates(DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = Day(now, zone);
        return [.. Enumerable.Range(0, Days).Select(back => today.AddDays(back - Days + 1))];
    }

    /// <summary>One value per day of <paramref name="dates"/>; null for a day before the record began, or for a
    /// waiting day with no count.</summary>
    public IReadOnlyList<int?> Series(TrendMeasure measure, IReadOnlyList<DateOnly> dates, TimeZoneInfo zone)
    {
        if (Since is not { } since)
            return [.. dates.Select(_ => (int?)null)];
        var first = Day(since, zone);
        return [.. dates.Select(date => date < first ? null : measure switch
        {
            TrendMeasure.Waiting => Queue.LastOrDefault(count => Day(count.At, zone) == date)?.Waiting,
            _ => (int?)Accepted.Count(at => Day(at, zone) == date),
        })];
    }

    /// <summary>The latest count from between 7 and 8 days ago, as Work's title compares with.</summary>
    public int? WeekAgo(DateTimeOffset now) =>
        Queue.LastOrDefault(count => count.At <= now.AddDays(-WorkTrend.Week) && count.At > now.AddDays(-WorkTrend.Week - 1))?.Waiting;

    public int AcceptedInWeek(DateTimeOffset now) => Accepted.Count(at => at > now.AddDays(-WorkTrend.Week));

    private static DateOnly Day(DateTimeOffset at, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

    private static TeamRecord? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            return new TeamRecord(
                root.TryGetProperty("since", out var since) && since.ValueKind == JsonValueKind.String ? since.GetDateTimeOffset() : null,
                [.. root.GetProperty("queue").EnumerateArray().Select(count =>
                    new QueueCount(count.GetProperty("at").GetDateTimeOffset(), count.GetProperty("waiting").GetInt32()))],
                [.. root.GetProperty("accepted").EnumerateArray().Select(at => at.GetDateTimeOffset())],
                root.GetProperty("cost").GetDecimal());
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}

/// <summary>A row of the Trends table: <paramref name="WaitingNow"/> is Work's own count where it has read one, or
/// the record's latest.</summary>
public sealed record TeamTrendRow(string Team, int? WaitingNow, int? WeekAgo, int Accepted, decimal Cost)
{
    public static TeamTrendRow Of(string team, int? waitingNow, TeamRecord record, DateTimeOffset now) =>
        new(team, waitingNow ?? record.Queue.LastOrDefault()?.Waiting, record.WeekAgo(now), record.AcceptedInWeek(now), record.Cost);
}
