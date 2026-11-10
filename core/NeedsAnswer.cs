using System.Text.RegularExpressions;

namespace ATeam.Core;

/// <summary>A pitch body's "Needs your answer" section.</summary>
internal static class NeedsAnswer
{
    /// <summary>For each line, whether it's inside a ``` or ~~~ code block, its fences included.</summary>
    internal static List<bool> Fenced(string[] lines)
    {
        var fenced = new List<bool>(lines.Length);
        string? open = null;
        foreach (var line in lines)
        {
            if (open is null)
            {
                var fence = Regex.Match(line, "^ {0,3}(?<f>`{3,}|~{3,})");
                open = fence.Success ? fence.Groups["f"].Value : null;
                fenced.Add(open is not null);
            }
            else
            {
                fenced.Add(true);
                if (Regex.IsMatch(line, $"^ {{0,3}}{Regex.Escape(open[..1])}{{{open.Length},}}[ \\t]*$"))
                    open = null;
            }
        }
        return fenced;
    }

    /// <summary>The section, heading and all, or "" when nothing's in it.</summary>
    internal static string In(string body)
    {
        var lines = body.Replace("\r", "").Split('\n');
        var fenced = Fenced(lines);
        var at = Enumerable.Range(0, lines.Length).Cast<int?>()
            .FirstOrDefault(i => !fenced[i!.Value] && Regex.IsMatch(lines[i.Value], "^##[ \\t]+Needs your answer[ \\t]*$", RegexOptions.IgnoreCase));
        if (at is null) return "";
        var stop = Enumerable.Range(at.Value + 1, lines.Length - at.Value - 1).Cast<int?>()
            .FirstOrDefault(i => !fenced[i!.Value] && Regex.IsMatch(lines[i.Value], "^#{1,2}[ \\t]|^---+[ \\t]*$|<!-- a-team:")) ?? lines.Length;
        var text = string.Join("\n", lines[(at.Value + 1)..stop]).Trim();
        return text.Length == 0 || Regex.IsMatch(text, "^none\\.?$", RegexOptions.IgnoreCase) ? "" : $"## Needs your answer\n\n{text}";
    }
}
