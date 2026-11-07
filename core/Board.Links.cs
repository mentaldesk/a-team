using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ATeam.Core;

/// <summary>What the links group asks of the record and GitHub: the decision register, upstream waits, and who holds a held task.</summary>
internal sealed partial class Board
{
    private const int AskedBefore = 600;
    private const string DecisionColumns = """
        id, team, CASE WHEN everyone THEN 'all' ELSE 'team' END AS scope, decision, why, who, kind, item, link,
          said, recorded, confirmed, confirmed_on AS confirmedOn, replaced_by AS replacedBy, retired, yours
        """;
    private const string RegisterSchema = """
        CREATE TABLE IF NOT EXISTS decisions (id INTEGER PRIMARY KEY, team TEXT NOT NULL,
            everyone INTEGER NOT NULL DEFAULT 0, decision TEXT NOT NULL, why TEXT NOT NULL, who TEXT NOT NULL,
            kind TEXT NOT NULL, item INTEGER NOT NULL, link TEXT NOT NULL, said TEXT NOT NULL, recorded TEXT NOT NULL,
            confirmed TEXT NOT NULL, confirmed_on INTEGER, replaced_by INTEGER, retired TEXT, yours TEXT);
          CREATE VIRTUAL TABLE IF NOT EXISTS decisions_text USING fts5(decision, why, content='decisions', content_rowid='id');
          CREATE TRIGGER IF NOT EXISTS decisions_added AFTER INSERT ON decisions BEGIN
            INSERT INTO decisions_text (rowid, decision, why) VALUES (new.id, new.decision, new.why); END;
          CREATE TRIGGER IF NOT EXISTS decisions_reworded AFTER UPDATE OF decision, why ON decisions BEGIN
            INSERT INTO decisions_text (decisions_text, rowid, decision, why) VALUES ('delete', old.id, old.decision, old.why);
            INSERT INTO decisions_text (rowid, decision, why) VALUES (new.id, new.decision, new.why); END;
        """;

    /// <summary>The decision register, set up apart from the record's schema so a SQLite without FTS5 loses only the register.</summary>
    private Record Register
    {
        get
        {
            _record.Exec(RegisterSchema);
            return _record;
        }
    }

    /// <summary>That entry of this team's register, or one for all teams; null if there's none, or no record yet.</summary>
    internal Row? DecisionEntry(string id)
    {
        if (!File.Exists(Path.Combine(State, "history.db"))) return null;
        return Register.Query($"SELECT {DecisionColumns} FROM decisions WHERE id = {id} AND (team = {TeamQ} OR everyone);").FirstOrDefault();
    }

    /// <summary>Adds an entry, replacing the one named if any, and gives back its id.</summary>
    internal string RecordDecision(bool everyone, string decision, string why, string who, string kind, string item, string link, string said, string old)
    {
        Register.Exec($"""
            BEGIN; INSERT INTO decisions (team, everyone, decision, why, who, kind, item, link, said,
                recorded, confirmed) VALUES ({TeamQ}, {(everyone ? 1 : 0)}, {Reconciler.Q(decision)},
                {Reconciler.Q(why)}, {Reconciler.Q(who)}, {Reconciler.Q(kind)}, {item}, {Reconciler.Q(link)}, {Reconciler.Q(said)},
                {Reconciler.Q(Now())}, {Reconciler.Q(said)});
              {(old.Length > 0 ? $"UPDATE decisions SET replaced_by = last_insert_rowid() WHERE id = {old};" : "")}
              COMMIT;
            """);
        return _record.Scalar("SELECT last_insert_rowid();")!;
    }

    internal void ChangeDecisions(string sql) => Register.Exec(sql);

    /// <summary>#n is no longer anyone's sub-issue, as far as the record knows.</summary>
    internal void ForgetParent(string n)
    {
        if (DryRun) return;
        _record.Exec($"UPDATE board SET content = json_remove(content, '$.parent') WHERE team = {TeamQ} AND item = {n};");
    }

    /// <summary>What task #n still waits on in other repos, stopping where GitHub won't say.</summary>
    internal JsonArray WaitsOnTask(string n) =>
        WaitsIn(Must(_gh.Run("api", "--paginate", $"repos/{_repo}/issues/{n}/comments")), Bot);

    /// <summary>This repo's open Dependabot PRs; none where GitHub won't say.</summary>
    internal JsonArray OpenBumps()
    {
        var read = _gh.Run("pr", "list", "-R", _repo, "--author", "app/dependabot", "--state", "open", "--json", "number,title");
        return read.Ok && Json.TryParse(read.Out) is JsonArray bumps ? bumps : [];
    }

    /// <summary>A released wait naming a package is still held while a Dependabot PR bumping it is open, which it says.</summary>
    internal static void WithBumps(List<JsonObject> waits, JsonArray bumps)
    {
        foreach (var wait in waits.Where(wait => Json.Text(wait["state"]) == "released" && Json.Truthy(wait["package"])))
        {
            var package = Json.Text(wait["package"]);
            var bump = bumps.FirstOrDefault(pr => Regex.Split(Json.Text(pr?["title"]) ?? "", "[ ,]+").Contains(package, StringComparer.OrdinalIgnoreCase));
            if (bump is null) continue;
            wait["met"] = false;
            wait["bump"] = bump.DeepClone();
        }
    }

    /// <summary>Each wait with how far it's got, and whether it's `met`.</summary>
    internal List<JsonObject> UpstreamState(JsonArray waits)
    {
        var found = new List<JsonObject>();
        foreach (var repo in waits.Select(wait => Json.Text(wait?["repo"])!).Distinct().Order(StringComparer.Ordinal))
        {
            var theirs = waits.Where(wait => Json.Text(wait?["repo"]) == repo).OfType<JsonNode>().ToList();
            var got = UpstreamItems(repo, theirs);
            found.AddRange(theirs.Select(wait => UpstreamWait(wait, got)));
        }
        foreach (var wait in found.Where(wait => Json.Text(wait["until"]) == "released" && Json.Text(wait["state"]) == "merged"))
            Released(wait, ReleasedIn(Json.Text(wait["repo"])!, Json.Text(wait["sha"])!), after: false);
        foreach (var wait in found.Where(wait => Json.Text(wait["until"]) == "released" && Json.Text(wait["state"]) == "closed" && wait["closed"] is not null))
            Released(wait, ReleasedAfter(Json.Text(wait["repo"])!, Json.Text(wait["closed"])!), after: true);
        foreach (var wait in found)
        {
            var state = Json.Text(wait["state"]);
            wait["met"] = Json.Text(wait["until"]) == "released" ? state == "released" : state is not ("open" or "unknown");
        }
        return found;
    }

    private static void Released(JsonObject wait, JsonObject? release, bool after)
    {
        if (release is null) return;
        wait["state"] = "released";
        wait["release"] = release;
        if (after) wait["after"] = true;
    }

    /// <summary>An item that's gone fails the call but still comes back with the others.</summary>
    private JsonObject? UpstreamItems(string repo, List<JsonNode> waits)
    {
        var query = string.Concat(waits.Select(wait => wait["n"]!.ToJsonString()).Select(n => $$"""
             i{{n}}: issueOrPullRequest(number: {{n}}) { __typename
                    ... on Issue { state stateReason closedAt timelineItems(last: 1, itemTypes: [CLOSED_EVENT]) { nodes { ... on ClosedEvent {
                        closer { __typename ... on PullRequest { merged mergeCommit { oid } } ... on Commit { oid } } } } }
                      closedByPullRequestsReferences(first: 5, includeClosedPrs: true) { nodes { merged mergeCommit { oid } } } }
                    ... on PullRequest { state merged mergeCommit { oid } } }
            """));
        var page = _gh.Run("api", "graphql", "-F", $"owner={repo[..repo.IndexOf('/')]}", "-F", $"name={repo[(repo.IndexOf('/') + 1)..]}", "-f",
            $"query=query UpstreamWaits($owner: String!, $name: String!) {{\n      repository(owner: $owner, name: $name) {{{query}}} }}");
        return Json.TryParse(page.Out)?["data"]?["repository"] as JsonObject;
    }

    private static JsonObject UpstreamWait(JsonNode wait, JsonObject? got)
    {
        var it = got?[$"i{wait["n"]!.ToJsonString()}"];
        var sha = MergedAs(it);
        var kind = Json.Text(it?["__typename"]);
        var state = it is null ? "unknown"
            : Json.Text(it["state"]) == "OPEN" ? "open"
            : sha is not null ? "merged"
            : kind == "PullRequest" ? "unmerged"
            : Json.Text(it["stateReason"]) == "NOT_PLANNED" ? "dropped" : "closed";
        var known = wait.DeepClone().AsObject();
        known["kind"] = kind;
        known["sha"] = sha;
        known["closed"] = it?["closedAt"]?.DeepClone();
        known["state"] = state;
        return known;
    }

    /// <summary>The commit an upstream item's change landed as, if it did.</summary>
    private static string? MergedAs(JsonNode? it)
    {
        if (it is null) return null;
        if (Json.Text(it["__typename"]) == "PullRequest")
            return Json.Truthy(it["merged"]) ? Json.Text(it["mergeCommit"]?["oid"]) : null;
        var commits = new List<string?>();
        foreach (var closer in (it["timelineItems"]?["nodes"]?.AsArray() ?? []).Select(node => node?["closer"]).OfType<JsonNode>())
        {
            if (Json.Text(closer["__typename"]) == "Commit") commits.Add(Json.Text(closer["oid"]));
            else if (Json.Truthy(closer["merged"])) commits.Add(Json.Text(closer["mergeCommit"]?["oid"]));
        }
        foreach (var pr in it["closedByPullRequestsReferences"]?["nodes"]?.AsArray() ?? [])
            if (Json.Truthy(pr?["merged"]))
                commits.Add(Json.Text(pr?["mergeCommit"]?["oid"]));
        return commits.FirstOrDefault();
    }

    /// <summary>The earliest published release whose tag contains that commit. A pre-release or a draft doesn't count.</summary>
    private JsonObject? ReleasedIn(string repo, string sha)
    {
        var releases = _gh.Run("api", $"repos/{repo}/releases?per_page=30", "--jq", """
            [.[] | select((.draft or .prerelease) | not)]
                | sort_by(.published_at) | reverse | .[] | [.tag_name, .published_at] | @tsv
            """);
        JsonObject? found = null;
        foreach (var line in releases.Out.Split('\n'))
        {
            var fields = line.Split('\t');
            if (fields[0].Length == 0) continue;
            var compared = _gh.Run("api", $"repos/{repo}/compare/{fields[0]}...{sha}", "--jq", ".status");
            if (!compared.Ok || compared.Said is not ("behind" or "identical")) break;
            found = new JsonObject { ["tag"] = fields[0], ["at"] = fields.ElementAtOrDefault(1) ?? "" };
        }
        return found;
    }

    /// <summary>The first published release since that time.</summary>
    private JsonObject? ReleasedAfter(string repo, string since)
    {
        var read = _gh.Run("api", $"repos/{repo}/releases?per_page=30");
        var first = (Json.TryParse(read.Out) as JsonArray ?? [])
            .Where(release => !Json.Truthy(release?["draft"]) && !Json.Truthy(release?["prerelease"])
                && string.CompareOrdinal(Json.Text(release?["published_at"]), since) >= 0)
            .OrderBy(release => Json.Text(release?["published_at"]), StringComparer.Ordinal).FirstOrDefault();
        return first is null ? null : new JsonObject { ["tag"] = first["tag_name"]?.DeepClone(), ["at"] = first["published_at"]?.DeepClone() };
    }

    private static long Epoch(string? at) =>
        DateTimeOffset.Parse(at!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUnixTimeSeconds();

    /// <summary>Who holds this held task, if anyone said so, and whose turn it is, as the app's Waiting says.</summary>
    internal (string? HeldBy, string Turn) HoldOn(JsonNode it)
    {
        var (page, code, _) = GatedTalk([Json.Long(it["number"])]);
        if (page is null) throw new Stop(code == 0 ? 1 : code, "");
        var hold = Holds([it], GatedComments(page), GatedBlocked([it]))[0];
        return (Json.Text(hold["heldBy"]), Json.Text(hold["turn"]) ?? "you");
    }

    private static string? Newest(IEnumerable<JsonObject> comments) => comments.Select(c => Json.Text(c["at"])).Max(StringComparer.Ordinal);

}
