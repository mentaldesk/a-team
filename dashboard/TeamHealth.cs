using System.Text.RegularExpressions;
using Terminal.Gui.Text;

namespace ATeam.Dashboard;

/// <summary>One thing <c>a-team board &lt;team&gt; check</c> found wrong, as the topic it's about and what's wrong.</summary>
public sealed record TeamProblem(string Topic, string Detail)
{
    private const int TopicWidth = 10;

    public override string ToString() => $"{Topic.PadRight(TopicWidth)}{Detail}";

    /// <summary>The problem in rows no wider than <paramref name="width"/>, its detail wrapping under itself.</summary>
    public IEnumerable<string> Rows(int width)
    {
        var room = width - TopicWidth;
        if (room < 1 || Detail.Length <= room)
            return [ToString()];
        var lines = TextFormatter.Format(
            Detail, room, Alignment.Start, wordWrap: true, preserveTrailingSpaces: false, tabWidth: 4,
            TextDirection.LeftRight_TopBottom, multiLine: false, textFormatter: null, preserveTabs: false);
        return lines.Select((line, index) => $"{(index == 0 ? Topic : "").PadRight(TopicWidth)}{line}");
    }
}

/// <summary>What stops a team working, as <c>check</c> reports it, one problem a line.</summary>
public sealed partial record TeamHealth(IReadOnlyList<TeamProblem> Problems)
{
    internal const string Checking = "checking…";

    /// <summary>What <c>board setup</c> puts right: the Status options and the labels.</summary>
    private static readonly string[] Repairable = ["status", "labels"];

    /// <summary>What <c>check</c> reports that a team can still run with.</summary>
    private static readonly string[] Notes = ["vision", "docs", "labels"];

    public bool CanRepair => Problems.Any(problem => Repairable.Contains(problem.Topic));

    /// <summary>The Teams page's health column.</summary>
    public string Column => Problems.Count switch
    {
        0 => "ok",
        1 => "1 problem",
        var count => $"{count} problems",
    };

    /// <summary>The problems that stop the team running.</summary>
    public IReadOnlyList<TeamProblem> Fatal => [.. Problems.Where(problem => !Notes.Contains(problem.Topic))];

    /// <summary>The message bar's line for a team that can't run; null for one that can.</summary>
    public string? Failed(string team)
    {
        var fatal = Fatal;
        if (fatal.Count == 0)
            return null;
        var topics = string.Join(", ", fatal.Select(problem => problem.Topic).Distinct());
        return $"{team}: {fatal.Count} {(fatal.Count == 1 ? "check" : "checks")} failed — {topics}";
    }

    /// <summary>A file the Teams page couldn't read, which <c>check</c> would only say again.</summary>
    public static TeamHealth Unreadable(string problem) => new([new TeamProblem("config", problem)]);

    /// <summary>The problem lines <c>check</c> printed. Where it failed without printing any, its failure is the one.</summary>
    public static TeamHealth Parse(Reading reading)
    {
        List<TeamProblem> problems = [];
        foreach (var line in reading.Output.Split('\n'))
            if (ProblemLine().Match(line.TrimEnd()) is { Success: true } match)
                problems.Add(new TeamProblem(match.Groups[1].Value, match.Groups[2].Value));
        if (problems.Count == 0 && reading.Failure is { } failure)
            problems.Add(new TeamProblem("check", failure));
        return new TeamHealth(problems);
    }

    [GeneratedRegex(@"^([a-z]+) {2,}(\S.*)$")]
    private static partial Regex ProblemLine();
}
