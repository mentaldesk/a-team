using System.Globalization;
using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>One team's record, as <c>a-team board &lt;team&gt; trend</c> reports it: when it began, how many were
/// waiting a week ago if it reaches back that far, how many items the stakeholders accepted in the last 7 days, and
/// how much of your time it took in them, from <paramref name="SpentSince"/> when timing began.</summary>
public sealed record WorkTrend(DateTimeOffset? Since, int? WeekAgo, int Accepted, DateTimeOffset? SpentSince = null, TimeSpan Spent = default)
{
    public const int Week = 7;

    public static WorkTrend? Of(Reading reading) =>
        reading.Failure is { Length: > 0 } ? null : Parse(reading.Output);

    /// <summary>Work's title: what's waiting now, then what the record can say about the teams together. The
    /// comparison needs every team to reach back a week, and accepted counts the days the record covers.</summary>
    public static string Title(int? waiting, IReadOnlyList<WorkTrend> trends, DateTimeOffset now)
    {
        if (waiting is not { } count)
            return "Work";
        var title = $"Work · {count} waiting on you";
        if (trends.Count == 0 || trends.All(trend => trend.Since is null))
            return title;
        if (trends.All(trend => trend.WeekAgo is not null))
            title += $" ({trends.Sum(trend => trend.WeekAgo!.Value)} a week ago)";
        var since = trends.Where(trend => trend.Since is not null).Min(trend => trend.Since!.Value);
        var days = Math.Clamp((int)Math.Ceiling((now - since).TotalDays), 1, Week);
        var accepted = trends.Sum(trend => trend.Accepted);
        title += $" · {accepted} accepted in {days} day{(days == 1 ? "" : "s")}";
        if (trends.All(trend => trend.SpentSince is null))
            return title;
        var spent = trends.Aggregate(TimeSpan.Zero, (sum, trend) => sum + trend.Spent);
        title += $" · {Visit.Duration(spent)} of yours";
        return PerHour(accepted, spent) is { } perHour ? $"{title} · {perHour} per hour" : title;
    }

    /// <summary>Accepted per hour of yours, or null with under a minute to divide by.</summary>
    public static string? PerHour(int accepted, TimeSpan spent) =>
        spent < TimeSpan.FromMinutes(1) ? null : (accepted / spent.TotalHours).ToString("0.0", CultureInfo.InvariantCulture);

    private static WorkTrend? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            return new WorkTrend(
                root.TryGetProperty("since", out var since) && since.ValueKind == JsonValueKind.String ? since.GetDateTimeOffset() : null,
                root.TryGetProperty("weekAgo", out var weekAgo) && weekAgo.ValueKind == JsonValueKind.Number ? weekAgo.GetInt32() : null,
                root.GetProperty("accepted").GetInt32(),
                root.TryGetProperty("spentSince", out var spentSince) && spentSince.ValueKind == JsonValueKind.String
                    ? spentSince.GetDateTimeOffset() : null,
                root.TryGetProperty("spent", out var spent) && spent.ValueKind == JsonValueKind.Number
                    ? TimeSpan.FromSeconds(spent.GetDouble()) : TimeSpan.Zero);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
