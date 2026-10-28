namespace ATeam.Dashboard;

/// <summary>Reads of several teams at once, each handed back on its own as it lands, so one that fails or lags
/// holds up none of the others.</summary>
internal sealed class TeamReads
{
    private readonly Func<string, Task<Reading>> _read;
    private readonly Dictionary<string, Task<Reading>> _left = [];

    internal TeamReads(IEnumerable<string> teams, Func<string, Task<Reading>> read)
    {
        _read = read;
        foreach (var team in teams)
            Add(team);
    }

    /// <summary>Whether every read has been handed back.</summary>
    internal bool Done => _left.Count == 0;

    /// <summary>Whether any read handed back so far succeeded.</summary>
    internal bool AnyRead { get; private set; }

    /// <summary>Starts reading <paramref name="team"/>, unless it's already being read.</summary>
    internal void Add(string team)
    {
        if (!_left.ContainsKey(team))
            _left[team] = _read(team);
    }

    /// <summary>The reads that have finished since last asked, a read that threw as a failed one.</summary>
    internal IReadOnlyList<(string Team, Reading Read)> Landed()
    {
        var landed = _left.Where(entry => entry.Value.IsCompleted).Select(entry => (entry.Key, Of(entry.Value))).ToList();
        foreach (var (team, read) in landed)
        {
            _left.Remove(team);
            AnyRead |= read.Failure is not { Length: > 0 };
        }
        return landed;
    }

    internal static Reading Of(Task<Reading> read) =>
        read.Status == TaskStatus.RanToCompletion
            ? read.Result
            : new Reading("", read.Exception?.GetBaseException().Message ?? "the read didn't finish");
}
