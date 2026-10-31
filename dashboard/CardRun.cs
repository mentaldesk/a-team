using System.Globalization;

namespace ATeam.Dashboard;

/// <summary>The run on one card: the live one, or else the last there was. <paramref name="OnTask"/> is a Dev or
/// Reviewer run kept under <c>runs/&lt;n&gt;</c>, which stops and resumes on its own; any other run is its role's.</summary>
public sealed record CardRun(string Role, AgentState State, bool OnTask, bool Held)
{
    /// <summary>A role whose own files have moved on from the card falls back to <paramref name="last"/>, the run
    /// record's last run on it.</summary>
    public static CardRun? For(string stateRoot, string team, IEnumerable<string> roles, int number, LastRun? last = null) =>
        roles.Select(role => Of(Path.Combine(stateRoot, team, role), role, number)).OfType<CardRun>()
            .OrderByDescending(run => run.State.Running)
            .ThenByDescending(run => run.State.LastStart)
            .FirstOrDefault() ?? Recorded(Path.Combine(stateRoot, team), last);

    private static CardRun? Recorded(string teamDir, LastRun? last)
    {
        if (last is null)
            return null;
        var log = File.Exists(last.Log) ? new FileInfo(last.Log).FullName : null;
        var role = AgentState.Read(Path.Combine(teamDir, last.Role));
        var running = role.Running && log is not null && role.LogPath == log;
        return new(last.Role, new AgentState(running, last.Started, [], log), OnTask: false, Held: false);
    }

    private static CardRun? Of(string dir, string role, int number)
    {
        var run = Path.Combine(dir, "runs", number.ToString(CultureInfo.InvariantCulture));
        if (Directory.Exists(run))
            return new(role, AgentState.Read(run), OnTask: true, Held: File.Exists(Path.Combine(run, "held")));
        if (!Directory.Exists(dir))
            return null;
        var state = AgentState.Read(dir);
        return state.Card == number || state.Task?.Number == number ? new(role, state, OnTask: false, Held: false) : null;
    }

    /// <summary>Who's on the card and for how long, or when it last ran, and why.</summary>
    internal static string Says(CardRun? run, DateTimeOffset now, bool held)
    {
        if (run is null)
            return "No agent has run on this card yet";
        var state = run.State;
        var role = OverseerView.RoleName(run.Role);
        var when = state.Running
            ? state.LastStart is { } started ? $"running {AgentPane.Clock(now - started)}" : "running"
            : state.LastStart is { } last ? $"last ran {AgentPane.Ago(now - last)} ago" : "last ran";
        var stopped = held ? " · held" : "";
        var why = state.Reasons.Count == 0 ? "" : " · why: " + string.Join("; ", state.Reasons);
        return $"{role} · {when}{stopped}{why}";
    }
}
