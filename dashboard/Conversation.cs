using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>One thing said on an item or its PR, as <c>a-team board &lt;team&gt; conversation &lt;n&gt;</c> reports it.</summary>
public sealed record Remark(string Who, DateTimeOffset At, string Body, int? Pr = null, bool Description = false, string? Url = null)
{
    /// <summary>Headed <c>new</c> when it was posted after <paramref name="since"/>.</summary>
    public string Heading(DateTimeOffset? since = null) =>
        $"───── {(Description ? $"PR #{Pr} · " : "")}{Who} · {At.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.InvariantCulture)}{(IsNewSince(since) ? " · new" : "")} ─────";

    public bool IsNewSince(DateTimeOffset? since) => since is { } last && At > last;
}

/// <summary>Everything said on an item since its body, oldest first, or the one line saying why it couldn't be read.</summary>
public sealed record Conversation(IReadOnlyList<Remark> Remarks, string? Failure = null)
{
    public static Conversation Of(Reading reading, int number) =>
        reading.Failure is { Length: > 0 } failure ? new Conversation([], failure)
        : Parse(reading.Output) is { } remarks ? new Conversation(remarks)
        : new Conversation([], $"couldn't read the conversation on #{number}");

    /// <summary>The remarks under their headings, or the failure line; nothing at all when nobody has said anything.</summary>
    public string Markdown(DateTimeOffset? since = null)
    {
        if (Failure is { Length: > 0 } failure)
            return $"\n\n{failure}";
        var text = new StringBuilder();
        foreach (var remark in Remarks)
            text.Append("\n\n").Append(remark.Heading(since)).Append("\n\n").Append(remark.Body);
        return text.ToString();
    }

    private static List<Remark>? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;
            return [.. document.RootElement.EnumerateArray().Select(remark => new Remark(
                remark.GetProperty("who").GetString() ?? "",
                remark.GetProperty("at").GetDateTimeOffset(),
                remark.GetProperty("body").GetString() ?? "",
                remark.TryGetProperty("pr", out var pr) && pr.ValueKind == JsonValueKind.Number ? pr.GetInt32() : null,
                remark.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.True,
                remark.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String ? url.GetString() : null))];
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
