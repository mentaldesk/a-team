using System.Globalization;
using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>One thing a-team did to an item, as <c>a-team board &lt;team&gt; history &lt;n&gt;</c> reports it.
/// <paramref name="Spent"/> is a visit's time, said before what you did in it.</summary>
public sealed record HistoryEvent(DateTimeOffset At, string Who, string What, Run? Run = null, TimeSpan? Spent = null)
{
    public string Line =>
        $"{At.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.InvariantCulture),-12} {Who,-4}  {Run?.Describe(At) ?? Visited ?? What}";

    private string? Visited => Spent is { } spent
        ? $"{Visit.Duration(spent < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : spent)} · {What}"
        : null;
}

/// <summary>A run started at an event's time: still going while <paramref name="Ended"/> is null.</summary>
public sealed record Run(DateTimeOffset? Ended, decimal? Cost, string? Outcome)
{
    public string Describe(DateTimeOffset started)
    {
        if (Ended is not { } ended)
            return $"running since {started.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)}";
        var minutes = Math.Max(1, (int)Math.Round((ended - started).TotalMinutes));
        return string.Join(" · ", new[]
        {
            $"run {minutes} min",
            Cost is { } cost ? $"${cost.ToString("0.00", CultureInfo.InvariantCulture)}" : null,
            Outcome,
        }.Where(part => !string.IsNullOrEmpty(part)));
    }
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
                e.GetProperty("what").GetString() ?? "",
                e.TryGetProperty("run", out var run) && run.ValueKind == JsonValueKind.Object ? ParseRun(run) : null,
                e.TryGetProperty("spent", out var spent) && spent.ValueKind == JsonValueKind.Number
                    ? TimeSpan.FromSeconds(spent.GetDouble()) : null))], since);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static Run ParseRun(JsonElement run) => new(
        run.TryGetProperty("ended", out var ended) && ended.ValueKind == JsonValueKind.String ? ended.GetDateTimeOffset() : null,
        run.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Number ? cost.GetDecimal() : null,
        run.TryGetProperty("outcome", out var outcome) && outcome.ValueKind == JsonValueKind.String ? outcome.GetString() : null);
}

/// <summary>Whether the reader shows History beside the body: as you last left it, until the dashboard restarts.</summary>
public sealed class ReaderPanes
{
    public const int HistoryWidth = 48;

    /// <summary>Narrower than this, the body would be too cramped beside History.</summary>
    public const int NarrowestSplit = 100;

    public const int CommentWidth = 36;

    /// <summary>Narrower than this, the comment, the body and History can't all fit.</summary>
    public const int NarrowestThree = 120;

    public bool HistoryShown { get; set; } = true;

    public bool OpensWithHistory(int width) => HistoryShown && width >= NarrowestSplit;
}
