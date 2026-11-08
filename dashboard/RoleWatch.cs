namespace ATeam.Dashboard;

/// <summary>What a role's line says about it: the state its icon comes from.</summary>
public enum RoleStatus
{
    NeverRun,
    Ok,
    Failed,
    CutShort,
    Running,
    Paused,
    Held,
    StoppedByYou,
    Misconfigured,
    Stuck,
}

/// <summary>One role of one team, as the dispatcher's files have it: whether it's running and on which cards, how its
/// last run went, and what its line in the team's panel says.</summary>
public sealed class RoleWatch(string team, string role, string stateDir)
{
    private const string Stopped = "dispatcher not running";

    private readonly SessionLog _log = new();

    internal string Team { get; } = team;

    internal string Role { get; } = role;

    internal bool Running { get; private set; }

    /// <summary>Held by you: <c>a-team pause</c>, or stopped and not yet let go.</summary>
    internal bool Held { get; private set; }

    internal RoleStatus Status { get; private set; } = RoleStatus.NeverRun;

    /// <summary>The cards the role's live runs were started for.</summary>
    internal IReadOnlyList<int> Working { get; private set; } = [];

    /// <summary>The role's live runs under runs/, each on its own task, leaving out any stopped by task.</summary>
    internal IReadOnlyList<DevRun> Runs { get; private set; } = [];

    /// <summary>The role's latest session, live or not: the newest of its runs under runs/ where it has any.</summary>
    internal string? LogPath { get; private set; }

    internal string? SessionId => _log.SessionId;

    /// <summary>What the dispatcher gave up on for the role, for its lane to say; null where it hasn't.</summary>
    internal string? GaveUp { get; private set; }

    /// <summary>What the team's panel says about the role, after its name.</summary>
    internal string Line { get; private set; } = "";

    /// <summary>Changes whenever one of the role's runs starts or finishes.</summary>
    internal string Activity
    {
        get
        {
            var state = AgentState.Read(stateDir);
            return $"{state.Running} {state.LastStart} {string.Join(' ', state.Runs.Select(run => run.Dir))}";
        }
    }

    public void Refresh(
        DateTimeOffset now, DateTimeOffset? nextCheck, bool paused, bool held, TeamProblem? misconfigured = null, bool passing = false)
    {
        var state = AgentState.Read(stateDir);
        Runs = [.. state.Runs.Where(run => run.Held is null)];
        Working = [.. Runs.Where(run => run.Task is not null).Select(run => run.Task!.Number)
            .Concat(state is { Running: true, Task: { } task } ? [task.Number] : [])
            .Concat(state is { Running: true, Card: { } card } ? [card] : []).Distinct()];
        Running = state.Running || Runs.Count > 0;
        Held = held;
        GaveUp = state.GaveUp;
        LogPath = (Runs.LastOrDefault() ?? state.Latest) is { } newest && AgentState.Read(newest.Dir).LogPath is { } log ? log : state.LogPath;
        _log.Refresh(LogPath);
        var running = state with { Running = Running };
        Status = StatusOf(running, paused, held, _log.Verdict, misconfigured is not null);
        Line = Status == RoleStatus.Stuck ? SaysStuck(state.Stuck) : Says(running, Working, now, nextCheck, Status, held, paused, passing);
    }

    /// <summary>A misconfigured team wins, then a stuck run, then a held role that isn't running, then pause, then running,
    /// then how the last run went.</summary>
    internal static RoleStatus StatusOf(AgentState state, bool paused, bool held, RunVerdict verdict, bool misconfigured = false) =>
        misconfigured ? RoleStatus.Misconfigured
            : state.Stuck.Count > 0 ? RoleStatus.Stuck
            : held && !state.Running ? (verdict == RunVerdict.None ? RoleStatus.StoppedByYou : RoleStatus.Held)
            : paused ? RoleStatus.Paused
            : state.Running ? RoleStatus.Running
            : verdict switch
            {
                RunVerdict.Ok => RoleStatus.Ok,
                RunVerdict.Error => RoleStatus.Failed,
                _ => state.LastStart is null ? RoleStatus.NeverRun : RoleStatus.CutShort,
            };

    /// <summary>Running for how long and on which cards, or idle with when it last ran and the next check, or paused.</summary>
    internal static string Says(
        AgentState state, IReadOnlyList<int> working, DateTimeOffset now, DateTimeOffset? nextCheck, RoleStatus status, bool held,
        bool teamPaused = false, bool passing = false)
    {
        if (state.Running)
        {
            var on = working.Count == 0 ? "" : " · " + string.Join(", ", working.Select(number => $"#{number}"));
            return (state.LastStart is { } started ? $"running {Ages.Clock(now - started)}" : "running") + on + (held ? " · paused" : "");
        }
        if (status == RoleStatus.StoppedByYou)
            return state.Stopped is { } stopped ? $"paused · stopped by you {Ages.Ago(now - stopped)} ago" : "paused · stopped by you";
        var ran = state.LastStart is { } last ? $"ran {Ages.Ago(now - last)} ago" : "never run";
        var went = status switch
        {
            RoleStatus.CutShort => " · cut short",
            RoleStatus.Failed => " · failed",
            _ => "",
        };
        if (held)
            return $"paused · {ran}{went}";
        return $"idle · {ran}{went} · {(teamPaused ? "team paused" : NextCheck(now, nextCheck, passing))}";
    }

    /// <summary>The oldest stuck run, and how many more there are.</summary>
    internal static string SaysStuck(IReadOnlyList<StuckRun> stuck) =>
        $"stuck · {stuck[0].Says}" + (stuck.Count > 1 ? $" · +{stuck.Count - 1} more" : "");

    internal static string Misconfigured(TeamProblem problem) =>
        problem.StopsTheTeam ? $"stopped: {problem.Detail}" : $"misconfigured: {problem.Topic} {problem.Detail}";

    private static string NextCheck(DateTimeOffset now, DateTimeOffset? nextCheck, bool passing) => nextCheck switch
    {
        null => "dispatcher hasn't run",
        { } next when next - now >= TimeSpan.Zero => $"next check {Ages.Clock(next - now)}",
        { } next when passing || now - next < TimeSpan.FromMinutes(1) => "checking now",
        _ => Stopped,
    };
}
