using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ATeam.Core;

/// <summary>One thing a run noticed, kept against the run, its role and its card.</summary>
internal sealed record Note(long Id, long Run, string Role, long? Card, string Started, string Text);

/// <summary>The notes that say the same thing.</summary>
internal sealed record NoteGroup(IReadOnlyList<Note> Notes)
{
    internal string Text => Notes[0].Text;
}

/// <summary>A run's <c>Noticed</c> list: read from its final summary, grouped for the Lead, and said as evidence.</summary>
internal static partial class Notes
{
    internal const int MostKept = 20;
    internal const int LongestKept = 500;

    [GeneratedRegex(@"^\s*(?:#{1,6}\s*)?(?:\*\*|__)?noticed(?:\*\*|__)?\s*(?::\s*(?:\*\*|__)?\s*(?<rest>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s*(?:[-*•+]|\d+[.)])\s+(?<note>.+)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotWord();

    private static readonly string[] Nothing = ["none", "nothing", "n/a", "-", "nothing new", "nothing else"];

    /// <summary>The last <c>Noticed</c> list in <paramref name="summary"/>, one entry per note: none for "none" or no list.</summary>
    internal static List<string> Parse(string? summary)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(summary)) return found;
        var lines = summary.Replace("\r", "", StringComparison.Ordinal).Split('\n');
        var start = Array.FindLastIndex(lines, line => Heading().IsMatch(line));
        if (start < 0) return found;
        var rest = Heading().Match(lines[start]).Groups["rest"].Value;
        Keep(found, rest);
        int? indent = null;
        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0)
            {
                if (indent is not null) break;
                continue;
            }
            var listed = indent is not null;
            var depth = line.Length - line.TrimStart().Length;
            if (Bullet().Match(line) is { Success: true } bullet)
            {
                if (listed && depth > indent && found.Count > 0)
                {
                    found[^1] = Clean($"{found[^1]}; {bullet.Groups["note"].Value.Trim()}");
                    continue;
                }
                Keep(found, bullet.Groups["note"].Value);
                indent ??= depth;
            }
            else if (listed && char.IsWhiteSpace(line[0]) && found.Count > 0)
            {
                found[^1] = Clean($"{found[^1]} {line.Trim()}");
            }
            else
            {
                break;
            }
        }
        return [.. found.Take(MostKept)];
    }

    private static void Keep(List<string> found, string said)
    {
        var note = Clean(said);
        if (note.Length > 0 && !Nothing.Contains(note.TrimEnd('.').ToLowerInvariant()))
            found.Add(note);
    }

    private static string Clean(string said)
    {
        var note = said.Replace("<!--", "", StringComparison.Ordinal).Replace("-->", "", StringComparison.Ordinal).Trim();
        return note.Length <= LongestKept ? note : $"{note[..LongestKept]}…";
    }

    /// <summary>What a note says with its case, punctuation and markup set aside, so two wordings of it meet.</summary>
    internal static string Key(string text) => NotWord().Replace(text.ToLowerInvariant(), " ").Trim();

    /// <summary>The notes grouped where they say the same thing, oldest first.</summary>
    internal static List<NoteGroup> Group(IEnumerable<Note> notes) =>
        [.. notes.OrderBy(note => note.Id).GroupBy(note => Key(note.Text)).Select(group => new NoteGroup([.. group]))];

    internal static string RoleName(string role) => role switch
    {
        "lead" => "Lead",
        "dev" => "Dev",
        "customer" => "Customer lead",
        "reviewer" => "Reviewer",
        _ => role,
    };

    private static readonly string[] RoleOrder = ["dev", "lead", "customer", "reviewer"];

    private static IEnumerable<IGrouping<string, Note>> ByRole(NoteGroup group) =>
        group.Notes.DistinctBy(note => note.Run).GroupBy(note => note.Role)
            .OrderByDescending(role => role.Count()).ThenBy(role => Array.IndexOf(RoleOrder, role.Key));

    /// <summary>Who noticed it and how often, as an Idea's Evidence says it: <c>Noticed by the Dev on #801 twice and #818, and the Lead once</c>.</summary>
    internal static string Evidence(NoteGroup group)
    {
        var said = ByRole(group).Select(role =>
        {
            var cards = role.Where(note => note.Card is not null).GroupBy(note => note.Card!.Value).OrderBy(card => card.Key).ToList();
            var others = role.Count(note => note.Card is null);
            var on = cards.Count == 0 ? "" : $" on {And(cards.Select(card => card.Count() == 1 ? $"#{card.Key}" : $"#{card.Key} {Times(card.Count())}"))}";
            var times = others == 0 ? "" : cards.Count == 0 ? $" {Times(others)}" : $" and {Times(others)} more";
            return $"the {RoleName(role.Key)}{on}{times}";
        }).ToList();
        var who = said.Count == 1 ? said[0] : $"{string.Join(", ", said[..^1])}, and {said[^1]}";
        return $"Noticed by {who}";
    }

    /// <summary>The Lead's brief section for the unhandled notes, or nothing when there are none.</summary>
    internal static string Brief(string team, IReadOnlyList<NoteGroup> groups, int room)
    {
        if (groups.Count == 0) return "";
        var notes = groups.Sum(group => group.Notes.Count);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"## Noticed since the last Lead run ({Count(groups.Count, "group")}, {Count(notes, "note")})\n\n");
        text.Append(CultureInfo.InvariantCulture,
            $"Handle every group with `a-team board {team} noticed lead ...`, as your role's *Handle what the runs noticed* says. Room for {Count(Math.Max(room, 0), "more Idea")} from them.\n\n");
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var who = string.Join(", ", ByRole(group).Select(role => $"{RoleName(role.Key)} ×{role.Count()}"));
            text.Append(CultureInfo.InvariantCulture, $"{i + 1}. {who}: {group.Text}\n");
            var runs = group.Notes.DistinctBy(note => note.Run).Select(note =>
                $"{(note.Card is { } card ? $"#{card}" : RoleName(note.Role))} {Day(note.Started)}");
            text.Append(CultureInfo.InvariantCulture, $"   notes: {string.Join(" ", group.Notes.Select(note => note.Id))} · runs: {string.Join(" · ", runs)}\n");
            text.Append(CultureInfo.InvariantCulture, $"   evidence: {Evidence(group)}\n");
        }
        return text.ToString();
    }

    private static string Day(string at) =>
        DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when)
            ? when.UtcDateTime.ToString("d MMM", CultureInfo.InvariantCulture)
            : at;

    private static string Count(int n, string what) => n == 1 ? $"1 {what}" : $"{n} {what}s";

    private static string Times(int n) => n switch { 1 => "once", 2 => "twice", _ => $"{n} times" };

    private static string And(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count == 1 ? list[0] : $"{string.Join(", ", list[..^1])} and {list[^1]}";
    }

    /// <summary><paramref name="body"/> with <paramref name="evidence"/> as the first line under its Evidence heading, or
    /// under a new one at its end.</summary>
    internal static string WithEvidence(string body, string evidence)
    {
        var lines = body.TrimEnd('\n').Split('\n').ToList();
        var heading = lines.FindIndex(line => Regex.IsMatch(line, @"^\s*(?:#{1,6}\s*Evidence\b|\*\*Evidence\*\*)", RegexOptions.IgnoreCase));
        if (heading < 0)
        {
            var marker = lines.FindLastIndex(line => line.StartsWith("<!-- a-team:", StringComparison.Ordinal));
            var at = marker < 0 ? lines.Count : marker;
            lines.InsertRange(at, ["", "**Evidence**", "", $"{evidence}.", ""]);
        }
        else
        {
            lines.InsertRange(heading + 1, ["", $"{evidence}."]);
        }
        return string.Join("\n", lines).Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
    }

    /// <summary>What a run ended by saying: the text of its log's last result, or null for a run cut short.</summary>
    internal static string? FinalSummary(string log)
    {
        if (!File.Exists(log)) return null;
        string? said = null;
        foreach (var line in File.ReadLines(log))
        {
            if (!line.Contains("\"type\":\"result\"", StringComparison.Ordinal)) continue;
            try
            {
                if (Json.Text(Json.Parse(line)?["result"]) is { } text) said = text;
            }
            catch (System.Text.Json.JsonException)
            {
            }
        }
        return said;
    }
}
