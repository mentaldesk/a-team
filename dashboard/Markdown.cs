using System.Text.RegularExpressions;

namespace ATeam.Dashboard;

/// <summary>Where a marked-up span sits in a row's text, and what it is.</summary>
public readonly record struct MarkdownSpan(int Start, int Length, LogLineKind Kind);

/// <summary>The markdown in a body, found line by line so the reader can colour it without changing a character.</summary>
public static partial class Markdown
{
    public static IReadOnlyList<LogLine> Lines(string text)
    {
        var lines = new List<LogLine>();
        var fenced = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.TrimStart();
            var fence = trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal);
            lines.Add(new LogLine(line, fenced || fence ? LogLineKind.Code : Kind(trimmed)));
            if (fence)
                fenced = !fenced;
        }
        return lines;
    }

    /// <summary>The list marker, inline code and bold in a prose or quote row; a span a wrap cuts in two is left plain.</summary>
    public static IEnumerable<MarkdownSpan> Spans(LogRow row)
    {
        if (row.Kind is not (LogLineKind.Prose or LogLineKind.Quote))
            yield break;
        if (row.Kind == LogLineKind.Prose && !row.Wrapped && ListMarker().Match(row.Text) is { Success: true } marker)
            yield return new MarkdownSpan(marker.Groups[1].Index, marker.Groups[1].Length, LogLineKind.ListMarker);
        foreach (Match span in Inline().Matches(row.Text))
            yield return new MarkdownSpan(span.Index, span.Length,
                span.Value[0] == '`' ? LogLineKind.InlineCode : LogLineKind.Strong);
    }

    private static LogLineKind Kind(string trimmed) =>
        Heading().IsMatch(trimmed) ? LogLineKind.Heading
        : trimmed.StartsWith('>') ? LogLineKind.Quote
        : LogLineKind.Prose;

    [GeneratedRegex(@"^#{1,6}(\s|$)")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s*([-*+]|\d+[.)])\s")]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"`[^`]+`|\*\*[^*]+\*\*|__[^_]+__")]
    private static partial Regex Inline();
}
