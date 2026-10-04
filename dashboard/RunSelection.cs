namespace ATeam.Dashboard;

/// <summary>Which of a role's runs its pane shows: the newest, until ↑/↓ choose another and keep to it.</summary>
internal sealed class RunSelection
{
    private IReadOnlyList<DevRun> _live = [];
    private string? _chosen;

    /// <summary>The live runs in the order they started, and the one shown after it finishes, until you move off it.</summary>
    public IReadOnlyList<DevRun> Runs { get; private set; } = [];

    /// <summary>The run whose log fills the pane, or -1 with no runs.</summary>
    public int Shown => _chosen is { } dir ? IndexOf(dir) : Runs.Count - 1;

    /// <summary>The run picked with ↑/↓, or null while the newest is shown.</summary>
    public string? Chosen
    {
        get => _chosen;
        set => _chosen = value;
    }

    public DevRun? Current => Shown >= 0 ? Runs[Shown] : null;

    public bool IsLive(DevRun run) => _live.Any(live => live.Dir == run.Dir);

    /// <param name="latest">The run started last, shown while none is chosen, even once it finishes.</param>
    public void Update(IReadOnlyList<DevRun> live, DevRun? latest = null)
    {
        _live = live;
        var finished = (_chosen is null ? latest : Current) is { } shown && !IsLive(shown) ? shown : null;
        Runs = finished is null ? live : [.. live.Append(finished).OrderBy(run => run.Started)];
    }

    /// <summary>Shows the next run in <paramref name="step"/>'s direction; false past either end, or with one run.</summary>
    public bool Move(int step)
    {
        var next = Shown + step;
        if (Runs.Count < 2 || next < 0 || next >= Runs.Count)
            return false;
        _chosen = Runs[next].Dir;
        Runs = [.. Runs.Where(run => run.Dir == _chosen || IsLive(run))];
        return true;
    }

    private int IndexOf(string dir)
    {
        for (var i = 0; i < Runs.Count; i++)
        {
            if (Runs[i].Dir == dir)
                return i;
        }
        return -1;
    }
}
