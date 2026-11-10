namespace ATeam.Core;

/// <summary>
/// The one way a card's status changes: who may move it from where to where, and the only SQL that writes it.
/// A sync follows what's already true on GitHub, so it answers to no table, but it writes through here too.
/// </summary>
internal static class StatusRoute
{
    internal static readonly string[] Statuses = ["Idea", "Exploring", "Pitched", "Approved", "Building", "Ready", "In progress", "In review", "Done"];
    internal static readonly string[] Roles = ["lead", "dev", "customer", "reviewer"];
    internal const string None = "None";
    internal const string Dispatcher = "dispatcher";

    /// <summary>What `move` and `add` let a role ask for.</summary>
    private static readonly HashSet<(string Role, string From, string To)> Moves =
    [
        ("lead", "Idea", "Exploring"),
        ("lead", "Exploring", "Pitched"),
        ("lead", "Exploring", "Idea"),
        ("lead", "Pitched", "Exploring"),
        ("lead", "Approved", "Building"),
        ("lead", None, "Idea"),
        ("lead", None, "Exploring"),
        ("lead", None, "Pitched"),
        ("lead", None, "Ready"),
        ("dev", None, "Idea"),
        ("dev", "Ready", "In progress"),
        ("dev", "In progress", "In review"),
        ("dev", "In progress", "Ready"),
        ("customer", None, "Idea"),
        ("customer", None, "Pitched"),
        ("customer", None, "In review"),
        ("you", None, "Idea"),
    ];

    /// <summary>What the commands that carry a gate of their own may do.</summary>
    private static readonly HashSet<(string Who, string From, string To)> Gates =
    [
        ("you", "Pitched", "Approved"),
        ("lead", "Pitched", "Approved"),
        ("you", "Idea", "Done"),
        ("you", "Pitched", "Done"),
        ("you", "In review", "Done"),
        ("you", "Pitched", "Idea"),
        ("lead", "Pitched", "Idea"),
        ("dev", None, "In progress"),
        ("dev", None, "In review"),
        (Dispatcher, "In progress", "In review"),
    ];

    /// <summary>Dependabot's PRs, which the dispatcher merges, or hands to the Dev, wherever they've got to.</summary>
    private static readonly string[] DispatcherTargets = ["Done", "In progress", "Ready"];

    internal static bool Allowed(string role, string from, string to) => Moves.Contains((role, from, to));

    internal static bool Permits(string who, string from, string to) =>
        Allowed(who, from, to) || Gates.Contains((who, from, to)) || (who == Dispatcher && DispatcherTargets.Contains(to));

    internal static IEnumerable<(string Role, string From, string To)> All => Moves;

    internal static IEnumerable<(string Who, string From, string To)> Everything()
    {
        foreach (var move in Moves) yield return move;
        foreach (var gate in Gates) yield return gate;
        foreach (var from in Statuses.Prepend(None))
            foreach (var to in DispatcherTargets)
                yield return (Dispatcher, from, to);
    }

    /// <summary>Where a sync can take a card that nobody moved: closed or merged on GitHub, or opened again.</summary>
    internal static IEnumerable<(string From, string To)> Follows()
    {
        foreach (var status in Statuses.Prepend(None))
        {
            if (status != "Done") yield return (status, "Done");
            else foreach (var to in Statuses.Where(to => to != "Done")) yield return ("Done", to);
        }
    }

    internal static string Set(string teamQ, string n, string to, string? since, string also = "") =>
        $"UPDATE board SET status = {Reconciler.Q(to)}{(since is null ? "" : $", since = {Reconciler.Q(since)}")} WHERE team = {teamQ} AND item = {n}{also};\n";

    internal static string Enter(string teamQ, string n, string nodeQ, string status, string since) =>
        $"INSERT OR IGNORE INTO board (team, item, node, status, since) VALUES ({teamQ}, {n}, {nodeQ}, {Reconciler.Q(status)}, {Reconciler.Q(since)});";

    internal static string Arrive(string teamQ, string n, string nodeQ, string status, string? priority, string since, string contentQ, string? mirror) =>
        $"INSERT INTO board (team, item, node, status, priority, since, content, mirror, mirrored_status,\n"
        + $" mirrored_priority) VALUES ({teamQ}, {n}, {nodeQ}, {Reconciler.Q(status)},\n"
        + $" {Reconciler.Q(priority)}, {Reconciler.Q(since)}, {contentQ}, {Reconciler.Q(mirror)}, {Reconciler.Q(status)}, {Reconciler.Q(priority ?? "none")});\n";
}
