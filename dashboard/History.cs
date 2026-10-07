using System.Globalization;
using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>One thing a-team did to an item, as <c>a-team board &lt;team&gt; history &lt;n&gt;</c> reports it.</summary>
public sealed record HistoryEvent(DateTimeOffset At, string Who, string What)
{
    public string Line =>
        $"{At.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.InvariantCulture),-12} {Who,-4}  {What}";
}

/// <summary>What a-team recorded on an item, newest first, from <paramref name="Since"/> when it began recording, or
/// the one line saying why it couldn't be read.</summary>
public sealed record History(IReadOnlyList<HistoryEvent> Events, DateTimeOffset? Since = null, string? Failure = null)
{
    public static History Of(Reading reading, int number) =>
        reading.Failure is { Length: > 0 } failure ? new History([], Failure: failure)
        : Parse(reading.Output) ?? new History([], Failure: $"couldn't read #{number}'s history");

    /// <summary>With <paramref name="happened"/> at the top, as the next read will show it.</summary>
    public History With(HistoryEvent happened) => this with { Events = [happened, .. Events] };

    public IReadOnlyList<LogLine> Lines =>
        Failure is { Length: > 0 } failure ? [new LogLine(failure, LogLineKind.Prose)]
        : Events.Count == 0 ? [new LogLine(Since is { } since
            ? $"Nothing recorded yet. a-team keeps history from {since.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture)}."
            : "Nothing recorded yet. a-team keeps history from now on.", LogLineKind.Prose)]
        : [.. Events.Select(e => new LogLine(e.Line, LogLineKind.Prose))];

    private static History? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            DateTimeOffset? since = root.TryGetProperty("since", out var at) && at.ValueKind == JsonValueKind.String
                ? at.GetDateTimeOffset()
                : null;
            return new History([.. root.GetProperty("events").EnumerateArray().Select(e => new HistoryEvent(
                e.GetProperty("at").GetDateTimeOffset(),
                e.GetProperty("who").GetString() ?? "",
                e.GetProperty("what").GetString() ?? ""))], since);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}

/// <summary>Whether the reader shows History beside the body: as you last left it, until the dashboard restarts.</summary>
public sealed class ReaderPanes
{
    public const int HistoryWidth = 48;

    /// <summary>Narrower than this, the body would be too cramped beside History.</summary>
    public const int NarrowestSplit = 100;

    public bool HistoryShown { get; set; } = true;

    public bool OpensWithHistory(int width) => HistoryShown && width >= NarrowestSplit;
}
