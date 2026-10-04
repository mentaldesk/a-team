namespace ATeam.Dashboard;

/// <summary>Each team's latest <c>check</c>, shared by the Agents panes and Settings → Teams. A team is checked when
/// first asked about and again when its file changes, never on a timer.</summary>
public sealed class TeamChecks(Func<string, Task<TeamHealth>> check, Func<string, string?> stamp)
{
    private readonly Dictionary<string, (string? Stamp, Task<TeamHealth> Health)> _checks = [];
    private readonly Dictionary<string, TeamHealth> _answered = [];
    private readonly HashSet<Task<TeamHealth>> _announced = [];

    /// <summary>The team's check, started if it has none yet.</summary>
    public Task<TeamHealth> For(string team) =>
        _checks.TryGetValue(team, out var known) ? known.Health : Check(team);

    /// <summary>The team's check, if it has had one.</summary>
    public Task<TeamHealth>? Latest(string team) => _checks.TryGetValue(team, out var known) ? known.Health : null;

    /// <summary>Checks the team again, whatever it last said.</summary>
    public Task<TeamHealth> Check(string team)
    {
        var health = check(team);
        _checks[team] = (stamp(team), health);
        return health;
    }

    /// <summary>Checks each team not checked yet, or whose file has changed since.</summary>
    public void Follow(IEnumerable<string> teams)
    {
        foreach (var team in teams)
            if (!_checks.TryGetValue(team, out var known) || known.Stamp != stamp(team))
                Check(team);
    }

    /// <summary>The first problem that stops the team running, from the last check that answered.</summary>
    public TeamProblem? Fatal(string team)
    {
        if (_checks.TryGetValue(team, out var known) && known.Health.Status == TaskStatus.RanToCompletion)
            _answered[team] = known.Health.Result;
        return _answered.GetValueOrDefault(team)?.Fatal.FirstOrDefault();
    }

    /// <summary>The message bar's line for each check that has answered since the last call and found the team can't run.</summary>
    public IReadOnlyList<string> Answered()
    {
        List<string> failed = [];
        foreach (var (team, (_, health)) in _checks)
        {
            if (!health.IsCompleted || !_announced.Add(health))
                continue;
            if (health.Status == TaskStatus.RanToCompletion && health.Result.Failed(team) is { } line)
                failed.Add(line);
        }
        return failed;
    }
}
