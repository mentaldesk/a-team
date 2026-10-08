using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>One team's record, as <c>a-team board &lt;team&gt; trend</c> reports it: when it began, how many were
/// waiting a week ago if it reaches back that far, and how many items the stakeholders accepted in the last 7 days.</summary>
public sealed record WorkTrend(DateTimeOffset? Since, int? WeekAgo, int Accepted)
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
        return $"{title} · {trends.Sum(trend => trend.Accepted)} accepted in {days} day{(days == 1 ? "" : "s")}";
    }

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
                root.GetProperty("accepted").GetInt32());
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
