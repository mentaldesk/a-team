using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>An issue's body as <c>a-team board &lt;team&gt; body &lt;n&gt;</c> reports it, or the one line saying
/// why it couldn't be read.</summary>
public sealed record IssueBody(string Text = "", string? Failure = null)
{
    /// <summary>What the command printed, or its own first line of stderr, which says what failed in its terms.</summary>
    public static IssueBody Of(Reading reading, int number) =>
        reading.Failure is { Length: > 0 } failure ? new IssueBody(Failure: failure)
        : Parse(reading.Output) is { } text ? new IssueBody(text)
        : new IssueBody(Failure: $"couldn't read #{number}");

    /// <summary>The body's own lines, with no trailing carriage returns, to draw as written.</summary>
    public IReadOnlyList<LogLine> Lines =>
        [.. Text.Split('\n').Select(line => new LogLine(line.TrimEnd('\r'), LogLineKind.Prose))];

    private static string? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("body", out var body)
                && body.ValueKind == JsonValueKind.String
                ? body.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
