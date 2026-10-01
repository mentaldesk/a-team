using System.Text.RegularExpressions;

namespace ATeam.Dashboard;

/// <summary>One thing <c>a-team board &lt;team&gt; check</c> found wrong, as the topic it's about and what's wrong.</summary>
public sealed record TeamProblem(string Topic, string Detail)
{
    private const int TopicWidth = 10;

    public override string ToString() => $"{Topic.PadRight(TopicWidth)}{Detail}";
}

/// <summary>What stops a team working, as <c>check</c> reports it, one problem a line.</summary>
public sealed partial record TeamHealth(IReadOnlyList<TeamProblem> Problems)
{
    internal const string Checking = "checking…";

    /// <summary>What <c>board setup</c> puts right: the Status options and the labels.</summary>
    private static readonly string[] Repairable = ["status", "labels"];

    public bool CanRepair => Problems.Any(problem => Repairable.Contains(problem.Topic));

    /// <summary>The Teams page's health column.</summary>
    public string Column => Problems.Count switch
    {
        0 => "ok",
        1 => "1 problem",
        var count => $"{count} problems",
    };

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
