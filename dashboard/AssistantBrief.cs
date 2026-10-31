using System.Text.Json.Nodes;

namespace ATeam.Dashboard;

/// <summary>What Claude Code is told when an Assistant conversation starts, so it can answer about the teams without
/// being shown around first.</summary>
internal static class AssistantBrief
{
    internal static string For(string root, string teamsDirectory, string stateRoot, IReadOnlyList<string> teams) => $"""
        You are the Assistant in the a-team dashboard. The person asking is the stakeholder of these a-team teams: {string.Join(", ", teams)}.
        Answer their questions about the teams: the board, the runs, the agents, the dispatcher and the spending.

        How a-team works: {Path.Combine(root, "process.md")} has the rules every role follows, and {Path.Combine(root, "roles")}/<role>.md
        what each role (lead, dev, customer, reviewer) does on a run. Together they're the brief each run starts from.
        Each team's config: {teamsDirectory}/<team>.json.
        Under {stateRoot}:
        - <team>/<role>/: each role's runs and session logs.
        - history.db: the run record, an SQLite file of every run, move and comment, with its cost. `a-team board <team> history <n>`,
          `spending` and `trend` read it.
        - dispatch.log, dispatcher.json, next-pass and launchd.log: the dispatcher's log and state. `a-team status` sums up what each
          role is doing.

        Each question starts with what the stakeholder is looking at, in brackets: the area of the dashboard (Work or Overseer) and
        the card, role or team selected there, or the dispatcher's log, like `[on a-team #499 in Work]`. Answer about that without asking
        which they mean.

        Look things up with the files above, `a-team status`, `gh`, and `a-team board <team> <command>`, whose commands process.md
        lists: `body <n>`, `conversation <n>` and `history <n>` for a card, `list`, `wip` and `waiting` for the board, `spending`
        and `trend` for the record. To change anything, act as the stakeholder through `a-team board <team> <command> you ...`,
        never `gh project` or another way round the board script. When it refuses, that's the answer: say why. Every command you
        run is shown to the stakeholder first, to allow or deny.

        Keep answers short: they're read in a narrow pane.
        """;

    /// <summary>The brief appended to Claude Code's own system prompt, with none of the user's settings loaded, so an
    /// allow rule of theirs can't run a command without the stakeholder seeing it first.</summary>
    internal static JsonObject Meta(string brief) => new()
    {
        ["systemPrompt"] = new JsonObject { ["append"] = brief },
        ["claudeCode"] = new JsonObject
        {
            ["options"] = new JsonObject
            {
                ["settingSources"] = new JsonArray(),
                ["allowDangerouslySkipPermissions"] = false,
            },
        },
    };

    /// <summary>What a question is about, as the pane shows it: the selection, then the area. The team is named only
    /// when there's more than one, since a number alone would then be ambiguous.</summary>
    internal static string Context(Area area, AskedAbout? about, bool manyTeams)
    {
        var where = area == Area.Work ? "in Work" : "in Overseer";
        var on = about switch
        {
            { Number: { } number } => manyTeams ? $"{about.Team} #{number}" : $"#{number}",
            { Role: { } role } => $"{about.Team}'s {Capitalised(role)}",
            { Dispatcher: true } => "the dispatcher",
            { Team: { } team } when manyTeams => team,
            _ => null,
        };
        return on is null ? where : $"on {on} {where}";
    }

    private static string Capitalised(string role) => role.Length == 0 ? role : char.ToUpperInvariant(role[0]) + role[1..];
}

/// <summary>The selection a question is about: a card, a role, the dispatcher, or just a team.</summary>
internal sealed record AskedAbout(string? Team, int? Number = null, string? Role = null, bool Dispatcher = false);
