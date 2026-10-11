using System.Text.Json;

namespace ATeam.Dashboard;

public enum TrendMeasure
{
    Waiting,
    Accepted,
    Cycle,
    Spent,
}

public sealed record QueueCount(DateTimeOffset At, int Waiting);

/// <summary>A visit's worth of your time, from <paramref name="At"/>, at one of Work's gates or Other.</summary>
public sealed record SpentTime(DateTimeOffset At, TimeSpan Spent, string Gate);

/// <summary>A task's span from first entering Ready to being accepted, and how long of it waited on you.</summary>
public sealed record Cycle(DateTimeOffset Ready, DateTimeOffset Accepted, TimeSpan WaitedOnYou)
{
    public double Hours => (Accepted - Ready).TotalHours;

    public static double? Median(IEnumerable<Cycle> cycles)
    {
        var hours = cycles.Select(cycle => cycle.Hours).Order().ToList();
        if (hours.Count == 0)
            return null;
        var middle = hours.Count / 2;
        return hours.Count % 2 == 1 ? hours[middle] : (hours[middle - 1] + hours[middle]) / 2;
    }

    /// <summary>The share of the cycles' time, all together, that waited on you.</summary>
    public static double? WithYou(IEnumerable<Cycle> cycles)
    {
        var list = cycles.ToList();
        var total = list.Sum(cycle => cycle.Hours);
        return total > 0 ? list.Sum(cycle => cycle.WaitedOnYou.TotalHours) / total : null;
    }
}

/// <summary>One team's last fortnight, as <c>a-team board &lt;team&gt; trends</c> reports it, with your time from
/// <paramref name="SpentSince"/> when timing began.</summary>
public sealed record TeamRecord(DateTimeOffset? Since, IReadOnlyList<QueueCount> Queue, IReadOnlyList<DateTimeOffset> Accepted,
    IReadOnlyList<Cycle> Cycles, decimal Cost, DateTimeOffset? SpentSince = null, IReadOnlyList<SpentTime>? Spent = null)
{
    public const int Days = 14;

    public static readonly string[] Gates = [.. WorkView.Gates.Select(gate => gate.Name), Attention.Other];

    public static readonly TeamRecord Empty = new(null, [], [], [], 0);

    public static TeamRecord? Of(Reading reading) =>
        reading.Failure is { Length: > 0 } ? null : Parse(reading.Output);

    /// <summary>The last <see cref="Days"/> days in <paramref name="zone"/>, today last.</summary>
    public static IReadOnlyList<DateOnly> Dates(DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = Day(now, zone);
        return [.. Enumerable.Range(0, Days).Select(back => today.AddDays(back - Days + 1))];
    }

    /// <summary>One value per day of <paramref name="dates"/>; null for a day before the record began, or for your
    /// time before timing began, a waiting day with no count, or a cycle day with nothing accepted. Your time is
    /// in minutes.</summary>
    public IReadOnlyList<int?> Series(TrendMeasure measure, IReadOnlyList<DateOnly> dates, TimeZoneInfo zone)
    {
        if ((measure == TrendMeasure.Spent ? SpentSince : Since) is not { } since)
            return [.. dates.Select(_ => (int?)null)];
        var first = Day(since, zone);
        return [.. dates.Select(date => date < first ? null : measure switch
        {
            TrendMeasure.Waiting => Queue.LastOrDefault(count => Day(count.At, zone) == date)?.Waiting,
            TrendMeasure.Cycle => Cycle.Median(Cycles.Where(cycle => Day(cycle.Accepted, zone) == date)) is { } hours
                ? (int)Math.Round(hours) : null,
            TrendMeasure.Spent => (int)Math.Round((Spent ?? []).Where(spent => Day(spent.At, zone) == date)
                .Sum(spent => spent.Spent.TotalMinutes)),
            _ => (int?)Accepted.Count(at => Day(at, zone) == date),
        })];
    }

    /// <summary>The latest count from between 7 and 8 days ago, as Work's title compares with.</summary>
    public int? WeekAgo(DateTimeOffset now) =>
        Queue.LastOrDefault(count => count.At <= now.AddDays(-WorkTrend.Week) && count.At > now.AddDays(-WorkTrend.Week - 1))?.Waiting;

    public int AcceptedInWeek(DateTimeOffset now) => Accepted.Count(at => at > now.AddDays(-WorkTrend.Week));

    public IEnumerable<Cycle> CyclesInWeek(DateTimeOffset now) => Cycles.Where(cycle => cycle.Accepted > now.AddDays(-WorkTrend.Week));

    /// <summary>Your time in the last 7 days, or null before timing began.</summary>
    public TimeSpan? SpentInWeek(DateTimeOffset now, string? gate = null) => SpentSince is null ? null
        : (Spent ?? []).Where(spent => spent.At > now.AddDays(-WorkTrend.Week) && (gate is null || spent.Gate == gate))
            .Aggregate(TimeSpan.Zero, (sum, spent) => sum + spent.Spent);

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
                [.. root.GetProperty("cycles").EnumerateArray().Select(cycle => new Cycle(cycle.GetProperty("ready").GetDateTimeOffset(),
                    cycle.GetProperty("accepted").GetDateTimeOffset(), TimeSpan.FromSeconds(cycle.GetProperty("withYou").GetDouble())))],
                root.GetProperty("cost").GetDecimal(),
                root.TryGetProperty("spentSince", out var spentSince) && spentSince.ValueKind == JsonValueKind.String
                    ? spentSince.GetDateTimeOffset() : null,
                root.TryGetProperty("spent", out var spent) && spent.ValueKind == JsonValueKind.Array
                    ? [.. spent.EnumerateArray().Select(each => new SpentTime(each.GetProperty("at").GetDateTimeOffset(),
                        TimeSpan.FromSeconds(each.GetProperty("seconds").GetDouble()), each.GetProperty("gate").GetString() ?? Attention.Other))]
                    : []);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}

/// <summary>A row of the Trends table: <paramref name="WaitingNow"/> is Work's own count where it has read one, or
/// the record's latest. <paramref name="Cycle"/> is the week's median hours to accept, <paramref name="WithYou"/>
/// the share of them In review, and <paramref name="Yours"/> your time in the week, null before timing began.</summary>
public sealed record TeamTrendRow(string Team, int? WaitingNow, int? WeekAgo, int Accepted, decimal Cost, double? Cycle, double? WithYou,
    TimeSpan? Yours = null)
{
    /// <summary>Accepted per hour of yours.</summary>
    public string? PerHour => Yours is { } yours ? WorkTrend.PerHour(Accepted, yours) : null;

    public static TeamTrendRow Of(string team, int? waitingNow, TeamRecord record, DateTimeOffset now)
    {
        var week = record.CyclesInWeek(now).ToList();
        return new(team, waitingNow ?? record.Queue.LastOrDefault()?.Waiting, record.WeekAgo(now), record.AcceptedInWeek(now),
            record.Cost, Dashboard.Cycle.Median(week), Dashboard.Cycle.WithYou(week), record.SpentInWeek(now));
    }

    /// <summary>The teams summed, and the week's cycles of all of them together; a count none of them has stays blank.</summary>
    public static TeamTrendRow All(IReadOnlyList<TeamTrendRow> rows, IReadOnlyList<Dashboard.Cycle> week) =>
        new("All", Sum(rows.Select(row => row.WaitingNow)), Sum(rows.Select(row => row.WeekAgo)),
            rows.Sum(row => row.Accepted), rows.Sum(row => row.Cost), Dashboard.Cycle.Median(week), Dashboard.Cycle.WithYou(week),
            rows.Select(row => row.Yours).Aggregate((TimeSpan?)null, (sum, yours) => yours is null ? sum : (sum ?? TimeSpan.Zero) + yours));

    private static int? Sum(IEnumerable<int?> values) => values.Aggregate((int?)null, (sum, value) => value is null ? sum : (sum ?? 0) + value);
}
