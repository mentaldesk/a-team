using System.Text.RegularExpressions;

namespace ATeam.Dashboard;

/// <summary>The points of a review's <b>Worth considering</b> section the Dev hasn't ticked as fixed, one per top-level
/// list item, read as the board's <c>Considered</c> counts them, and the comment asking the Dev to fix some.</summary>
public static partial class Considered
{
    /// <summary>The points of the Reviewer's review of PR <paramref name="pr"/>, or none where it left none.</summary>
    public static IReadOnlyList<string> Points(IEnumerable<Remark> remarks, int pr) =>
        remarks.LastOrDefault(remark => remark.Who == "reviewer" && remark.Pr == pr) is { } review ? Points(review.Body) : [];

    public static IReadOnlyList<string> Points(string review)
    {
        var lines = review.Replace("\r", "").Split('\n');
        var at = Array.FindIndex(lines, line => Heading().IsMatch(line));
        if (at < 0)
            return [];
        List<List<string>> points = [];
        var open = false;
        foreach (var line in lines.Skip(at + 1).TakeWhile(line => !SectionEnd().IsMatch(line)))
        {
            if (Item().Match(line) is { Success: true } item)
            {
                open = !item.Groups["box"].Success || item.Groups["box"].Value == " ";
                if (open)
                    points.Add([line[item.Length..].Trim()]);
            }
            else if (line.Trim().Length == 0)
            {
                if (open)
                    points[^1].Add("");
            }
            else if (open && (points[^1][^1].Length > 0 || char.IsWhiteSpace(line[0])))
                points[^1].Add(line.Trim());
            else
                open = false;
        }
        return [.. points.Select(point => string.Join('\n', point).TrimEnd())];
    }

    /// <summary>The review as the Review tab shows it: each point of Needs changing and Worth considering with a box,
    /// ticked once the Dev has fixed it.</summary>
    public static string Boxed(string review) =>
        string.Join('\n', Lines(review).Select(line => line.Point is { } item
            ? $"[{(item.Groups["box"].Value is "x" or "X" ? 'x' : ' ')}] {line.Text[item.Length..]}"
            : line.Text));

    /// <summary>How many points of Needs changing and Worth considering the Dev hasn't ticked.</summary>
    public static int Open(string review) =>
        Lines(review).Count(line => line.Point is { } item && item.Groups["box"].Value is not ("x" or "X"));

    private static IEnumerable<(string Text, Match? Point)> Lines(string review)
    {
        var inside = false;
        foreach (var line in review.Replace("\r", "").Split('\n'))
        {
            if (AnyHeading().IsMatch(line) || line.Contains("<!-- a-team:", StringComparison.Ordinal))
                inside = Sections().IsMatch(line);
            yield return (line, inside && Item().Match(line) is { Success: true } item ? item : null);
        }
    }

    /// <summary>The one comment: each point quoted, then the ask and the note, if any.</summary>
    public static string Request(IEnumerable<string> points, string note)
    {
        var quotes = points.Select(point => string.Join('\n', point.Split('\n').Select(line => line.Length == 0 ? ">" : $"> {line}")));
        var ask = $"Please fix these. {note.Trim()}".TrimEnd();
        return string.Join("\n\n", [.. quotes, ask]);
    }

    [GeneratedRegex(@"^#{1,6}[ \t]+Worth considering[ \t]*$", RegexOptions.IgnoreCase)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^#{1,6}[ \t]|<!-- a-team:")]
    private static partial Regex SectionEnd();

    [GeneratedRegex(@"^#{1,6}[ \t]+(?:Needs changing|Worth considering)[ \t]*$", RegexOptions.IgnoreCase)]
    private static partial Regex Sections();

    [GeneratedRegex(@"^#{1,6}[ \t]")]
    private static partial Regex AnyHeading();

    [GeneratedRegex(@"^(?:[-*+]|[0-9]+[.)])[ \t]+(?:\[(?<box>[ xX])\][ \t]*)?")]
    private static partial Regex Item();
}
