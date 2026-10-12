using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>An issue's body as <c>a-team board &lt;team&gt; body &lt;n&gt;</c> reports it, or the one line saying
/// why it couldn't be read, and its <paramref name="History"/> where that was read too.</summary>
public sealed record IssueBody(string Text = "", string? Failure = null, History? History = null)
{
    /// <summary>What's been said on it, each with the line its heading is on.</summary>
    public IReadOnlyList<RemarkAt> Remarks { get; init; } = [];

    /// <summary>When you last opened it on this machine, or null where you never have.</summary>
    public DateTimeOffset? Opened { get; init; }

    /// <summary>What a document pitch changes, where it is one and that was read.</summary>
    public DocumentPitch? Document { get; init; }

    /// <summary>A document pitch's body, followed by the one file it changes, as it is on its branch now.</summary>
    public IssueBody With(DocumentPitch document) => this with
    {
        Text = document.Editable ? $"{Text.TrimEnd()}\n\n---\n\n# {document.Path}\n\n{document.Text}" : Text,
        Document = document,
    };

    /// <summary>What the command printed, or its own first line of stderr, which says what failed in its terms.</summary>
    public static IssueBody Of(Reading reading, int number) =>
        reading.Failure is { Length: > 0 } failure ? new IssueBody(Failure: failure)
        : Parse(reading.Output) is { } text ? new IssueBody(text)
        : new IssueBody(Failure: $"couldn't read #{number}");

    /// <summary>The conversation under the body, its remarks posted after <paramref name="since"/> headed <c>new</c>.</summary>
    public IssueBody With(Conversation conversation, DateTimeOffset? since = null)
    {
        var line = Text.Count(c => c == '\n');
        List<RemarkAt> remarks = [.. Remarks];
        foreach (var remark in conversation.Failure is null ? conversation.Remarks : [])
        {
            remarks.Add(new RemarkAt(remark, line + 2));
            line += 4 + remark.Body.Count(c => c == '\n');
        }
        return this with { Text = Text + conversation.Markdown(since), Remarks = remarks };
    }

    public bool Equals(IssueBody? other) =>
        other is not null && Text == other.Text && Failure == other.Failure && History == other.History
        && Document == other.Document && Opened == other.Opened && Remarks.SequenceEqual(other.Remarks);

    public override int GetHashCode() => HashCode.Combine(Text, Failure, History, Document, Opened, Remarks.Count);

    /// <summary>The body's own lines, with no trailing carriage returns, to draw as written.</summary>
    public IReadOnlyList<LogLine> Lines => Markdown.Lines(Text);

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

public sealed record RemarkAt(Remark Remark, int Line);
