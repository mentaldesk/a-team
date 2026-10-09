namespace ATeam.Dashboard;

/// <summary>What the New idea dialog holds when you add it.</summary>
public sealed record IdeaDraft(string Team, string Title, string Description, Rank Rank);

/// <summary>Adds an idea in three board commands: open the issue as you, put it on the team's board as an Idea, and
/// rank it. A retry starts from the step that failed, so it never opens a second issue.</summary>
public sealed class IdeaFiler(Func<string[], Task<Reading>> board)
{
    private bool _added;

    /// <summary>The issue opened so far, or null before one is.</summary>
    public (string Team, int Number)? Opened { get; private set; }

    /// <returns>Null once the idea is on the board as asked; otherwise which step failed, and why.</returns>
    public async Task<string?> Add(IdeaDraft draft)
    {
        if (Opened is null && await Open(draft).ConfigureAwait(false) is { } unopened)
            return $"couldn't open the issue: {unopened}";
        var (team, number) = Opened!.Value;
        if (!_added)
        {
            if ((await board(["board", team, "add", "you", number.ToString(), "Idea"]).ConfigureAwait(false)).Failure is { } unadded)
                return $"#{number} is open, but couldn't go on {team}'s board: {unadded}";
            _added = true;
        }
        if (draft.Rank != Rank.None
            && (await board(["board", team, "priority", "you", number.ToString(), Priorities.Value(draft.Rank)]).ConfigureAwait(false))
                .Failure is { } unranked)
            return $"#{number} is on the board, but couldn't be ranked: {unranked}";
        return null;
    }

    private async Task<string?> Open(IdeaDraft draft)
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, $"{draft.Title.Trim()}\n{draft.Description}");
            var read = await board(["board", draft.Team, "new", "you", file]).ConfigureAwait(false);
            if (read.Failure is { } failure)
                return failure;
            if (!int.TryParse(read.Output.Trim(), out var number))
                return $"no issue number in '{read.Output.Trim()}'";
            Opened = (draft.Team, number);
            return null;
        }
        finally
        {
            File.Delete(file);
        }
    }
}
