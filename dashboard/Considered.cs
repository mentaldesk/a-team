using System.Text.RegularExpressions;

namespace ATeam.Dashboard;

/// <summary>The points of a review's <b>Worth considering</b> section, one per top-level list item, read as the board's
/// <c>considered</c> counts them in <c>scripts/board.sh</c>, and the comment asking the Dev to fix some.</summary>
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
                points.Add([line[item.Length..].Trim()]);
                open = true;
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

    [GeneratedRegex(@"^(?:[-*+]|[0-9]+[.)])[ \t]+")]
    private static partial Regex Item();
}
