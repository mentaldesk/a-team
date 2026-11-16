using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace ATeam.Core;

/// <summary>A refusal: "board.sh: " and this, on stderr, and exit 1.</summary>
internal sealed class Die(string message) : Exception(message);

/// <summary>A command stopped by what it ran: this exit code, and what that said on stderr.</summary>
internal sealed class Stop(int code, string said) : Exception(said)
{
    internal int Code => code;
}

/// <summary>A team's board, as one command sees it: its config, its record and GitHub.</summary>
internal sealed partial class Board
{
    private const string IssueFields = """
        ... on Issue { id number title url state stateReason createdAt repository { nameWithOwner } labels(first: 20) { nodes { name } }
                         author { __typename login }
                         parent { number }
                         issueDependenciesSummary { blockedBy } }
        """;
    private const string PullFields = """
        ... on PullRequest { id number title url state isDraft createdAt repository { nameWithOwner } labels(first: 20) { nodes { name } }
                             author { login }
                             closingIssuesReferences(first: 20) { nodes { number repository { nameWithOwner } } } }
        """;
    private const string ContentFields = $$"""
        __typename
          {{IssueFields}}
          {{PullFields}}
        """;

    private readonly string _team;
    private readonly JsonObject _config;
    private readonly string _repo;
    private readonly string _priority;
    private readonly int _readEvery;
    private readonly Lazy<Record> _opened;
    private readonly Gh _gh;
    private bool _synced;

    private Record _record => _opened.Value;

    internal string Team => _team;

    internal string Repo => _repo;

    internal bool DryRun { get; }


    internal Board(string team, JsonObject config, Func<Record> record, Gh gh, bool dryRun)
    {
        _team = team;
        _config = config;
        _opened = new Lazy<Record>(record);
        _gh = gh;
        DryRun = dryRun;
        _repo = Text("repo");
        _priority = OrElse(Text("priorityField"), "Priority");
        _readEvery = int.TryParse(Environment.GetEnvironmentVariable("A_TEAM_BOARD_READ_EVERY"), out var every) ? every : 900;
    }

    private static string OrElse(string text, string other) => text.Length > 0 ? text : other;

    internal string Text(params string[] path)
    {
        JsonNode? node = _config;
        foreach (var key in path)
            node = node?[key];
        return node is null || !Json.Truthy(node) ? "" : Json.Text(node) ?? node.ToJsonString();
    }

    internal const string IsoFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    internal static string Now() => DateTime.UtcNow.ToString(IsoFormat, CultureInfo.InvariantCulture);

    private static string Iso(long seconds) =>
        DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString(IsoFormat, CultureInfo.InvariantCulture);

    private string TeamQ => Reconciler.Q(_team);

    private void BoardReady()
    {
        var columns = _record.Query("SELECT 'board.' || name AS name FROM pragma_table_info('board') UNION ALL SELECT 'boards.' || name FROM pragma_table_info('boards');")
            .Select(row => row.First).ToHashSet();
        if (!columns.Contains("board.marks"))
            AddColumn("board", "marks");
        foreach (var column in new[] { "repaired", "issues_read" })
            if (!columns.Contains($"boards.{column}"))
                AddColumn("boards", column);
        _record.Exec($"INSERT OR IGNORE INTO boards (team, repaired) VALUES ({TeamQ}, {Reconciler.Q(Now())});");
        RepairLastMoves();
    }

    private void AddColumn(string table, string column)
    {
        try
        {
            _record.Exec($"ALTER TABLE {table} ADD COLUMN {column} TEXT;");
        }
        catch (InvalidOperationException e) when (e.Message.StartsWith("duplicate column name", StringComparison.Ordinal))
        {
        }
    }

    /// <summary>Once per team, puts each open card whose status differs from its last recorded move back at that move:
    /// what a Project's false set-backs undid before a status there stopped being a move.</summary>
    private void RepairLastMoves()
    {
        if (_record.Scalar($"SELECT COUNT(*) FROM boards WHERE team = {TeamQ} AND repaired IS NULL;") == "0") return;
        var plan = new List<(string N, string From, string To, string Who, string At)>();
        foreach (var row in _record.Query($"""
            SELECT b.item, b.status, json_group_array(json_object('id', e.id, 'at', e.at, 'who', e.who, 'what', e.what)) AS events
              FROM board b JOIN events e ON e.team = b.team AND e.item = b.item
              WHERE b.team = {TeamQ} AND b.status != 'Done' AND json_extract(b.content, '$.shut') IS NULL
              GROUP BY b.item ORDER BY b.item;
            """))
        {
            var last = Json.Parse(row["events"]!)!.AsArray().OrderBy(e => e!["id"]!.GetValue<long>())
                .Where(e => Reconciler.Landed(Json.Text(e!["what"])!) is not null).LastOrDefault();
            if (last is null) continue;
            var to = Reconciler.Landed(Json.Text(last["what"])!)!;
            var from = row["status"]!;
            if (to == from || to == "Done") continue;
            if (!DateTimeOffset.TryParseExact(Json.Text(last["at"]), IsoFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moved))
                return;
            plan.Add((row["item"]!, from, to, Json.Text(last["who"])!, Libc.Local(moved).ToString("d MMM", CultureInfo.InvariantCulture)));
        }
        if (!MayMoveState())
        {
            if (plan.Count > 0) RefuseMove("move the board forward, putting cards back at their last moves");
            return;
        }
        var now = Now();
        var unrepaired = $"(SELECT repaired FROM boards WHERE team = {TeamQ}) IS NULL";
        var changes = new StringBuilder();
        foreach (var (n, from, to, who, at) in plan)
        {
            changes.Append(StatusRoute.Set(TeamQ, n, to, now, $" AND {unrepaired}"));
            if (!DryRun)
                changes.Append($"INSERT INTO events (team, item, at, who, what) SELECT {TeamQ}, {n}, {Reconciler.Q(now)}, 'board',\n {Reconciler.Q($"{from} → {to} · repaired to its last move ({who}, {at})")} WHERE {unrepaired};\n");
        }
        try
        {
            _record.Exec($"BEGIN IMMEDIATE; {changes} UPDATE boards SET repaired = {Reconciler.Q(now)} WHERE team = {TeamQ} AND repaired IS NULL; COMMIT;");
        }
        catch (InvalidOperationException)
        {
            _record.TryRollback();
        }
    }

    /// <summary>Whether this a-team may move the state forward: only the one the dispatcher runs, or a run's copy of that
    /// release, moves the real state.</summary>
    private bool MayMoveState()
    {
        if (DryRun) return true;
        var xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        var real = Path.Combine(string.IsNullOrEmpty(xdg) ? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", ".local/state") : xdg, "a-team");
        if (Libc.RealPath(State) is not { } here || here != Libc.RealPath(real)) return true;
        var bin = InstalledBin();
        if (bin.Length == 0) return true;
        var installedRoot = Libc.RealPath(bin) is { } target ? Libc.RealPath(Path.Combine(Path.GetDirectoryName(target)!, "..")) : null;
        if (Libc.RealPath(Root) == installedRoot) return true;
        var version = Path.Combine(Root, "VERSION");
        return File.Exists(version) && File.ReadAllText(version).TrimEnd('\n') == Proc.Say(bin, "version");
    }

    private static string InstalledBin()
    {
        try
        {
            return Json.Text(Json.Parse(File.ReadAllText(Path.Combine(State, "dispatcher.json")))?["bin"]) ?? "";
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
        {
            return "";
        }
    }

    private static void RefuseMove(string what)
    {
        var bin = InstalledBin();
        if (File.Exists(Path.Combine(Root, "VERSION")) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("A_TEAM_RUN_TEAM")))
            throw new Die($"this run is on {Proc.Say(Path.Combine(Root, "bin", "a-team"), "version")}, and only the installed a-team ({Proc.Say(bin, "version")}) may {what}: stopping");
        throw new Die($"this a-team ({Root}) would {what}, and only the installed one ({bin}) may: try it with --dry-run, or with A_TEAM_STATE set to a copy");
    }

    /// <summary>Carries the old a-team labels on open items into the record, then takes them off on GitHub. A `blocked`
    /// keeps who added it and when, and a shelve the date in its hidden comment.</summary>
    private void AdoptLabels()
    {
        var rows = _record.Query($"SELECT item, marks, content FROM board WHERE team = {TeamQ} AND status != 'Done' AND json_extract(content, '$.shut') IS NULL;")
            .Select(row => (Row: row, All: Json.Parse(row["content"]!)?["labels"]?.AsArray() ?? []))
            .Select(card => (card.Row, card.All, Old: card.All.Select(Json.Text).OfType<string>().Where(IsStateLabel).ToList()))
            .Where(card => card.Old.Count > 0).ToList();
        if (rows.Count == 0) return;
        if (!MayMoveState()) RefuseMove("move the board forward, taking its old labels off the issues");
        var held = HeldSince(rows.Where(card => card.Old.Contains("blocked")).Select(card => card.Row["item"]!).ToList());
        foreach (var (row, all, old) in rows)
        {
            var n = row["item"]!;
            var labels = new JsonArray();
            foreach (var label in old)
                labels.Add((JsonNode?)label);
            var marks = Json.Merge(Json.Parse(string.IsNullOrEmpty(row["marks"]) ? "{}" : row["marks"]!)!.AsObject(), MarksOf(labels));
            if (Json.Truthy(marks["held"]) && held[n] is { } since)
                marks["held"] = since.DeepClone();
            if (Json.Truthy(marks["shelved"]) && old.Contains("a-team:shelved") && Shelved(n) is { } shelf)
                marks["shelved"] = shelf;
            _record.Exec($"UPDATE board SET marks = {Reconciler.Q(Json.Compact(marks))} WHERE team = {TeamQ} AND item = {n};");
            if (Json.Truthy(marks["upstream"]) && old.Contains("a-team:upstream")) CopyWaits(n);
            var gone = new HashSet<string>();
            foreach (var label in old)
                if (Write($"unlabel #{n} {label}", "api", "-X", "DELETE", $"repos/{_repo}/issues/{n}/labels/{Uri.EscapeDataString(label)}").Ok)
                    gone.Add(label);
            var kept = new JsonArray();
            foreach (var label in all)
                if (Json.Text(label) is not { } name || !gone.Contains(name))
                    kept.Add(label?.DeepClone());
            _record.Exec($"UPDATE board SET content = json_set(content, '$.labels', json({Reconciler.Q(Json.Compact(kept))})) WHERE team = {TeamQ} AND item = {n};");
        }
    }

    internal static string State { get; set; } = "";

    internal static string Root { get; set; } = "";

    internal static string ConfigDir { get; set; } = "";

    private static bool IsStateLabel(string label) =>
        label.StartsWith("a-team:", StringComparison.Ordinal) || label.StartsWith("rank:", StringComparison.Ordinal)
        || label.StartsWith("theme: ", StringComparison.Ordinal) || label == "blocked";

    /// <summary>Once a command, reads the open items, and every so often the repo's open issues and PRs. Where GitHub can't be
    /// read, the record stands as it is. <paramref name="cached"/> only moves the board over and carries any old labels into it.</summary>
    internal void Sync(bool cached = false)
    {
        if (_synced) return;
        _synced = true;
        BoardReady();
        AdoptLabels();
        if (cached) return;
        if (Stale("issues_read") && OpenIssues() is { } open && OpenOnRepo("RepoPullRequests", "pullRequests", PullFields) is { } pulls)
        {
            foreach (var pull in pulls)
                open.Add(pull?.DeepClone());
            Arrive(open);
            _record.Exec($"UPDATE boards SET issues_read = {Reconciler.Q(Now())} WHERE team = {TeamQ};");
        }
        var ids = _record.Query($"SELECT node FROM board WHERE team = {TeamQ} AND status != 'Done' AND node != '';")
            .Select(row => row.First!).Distinct().Order(StringComparer.Ordinal).ToList();
        if (ids.Count > 0 && FreshNodes(ids) is { } fresh)
            Reconcile(fresh);
        AdoptLabels();
    }

    private bool Stale(string column) =>
        _record.Scalar($"SELECT COUNT(*) FROM boards WHERE team = {TeamQ} AND ({column} IS NULL OR {column} <= strftime('%Y-%m-%dT%H:%M:%SZ', 'now', '-{_readEvery} seconds'));") != "0";

    internal JsonArray? OpenIssues() => OpenOnRepo("RepoIssues", "issues", IssueFields);

    /// <summary>Every open issue or PR on the repo, read again once if it comes back short; null if it can't be read whole.</summary>
    private JsonArray? OpenOnRepo(string name, string connection, string fields)
    {
        var query = $$"""
            query {{name}}($owner: String!, $name: String!, $endCursor: String) {
                  repository(owner: $owner, name: $name) { {{connection}}(states: OPEN, first: 100, after: $endCursor) {
                    totalCount pageInfo { hasNextPage endCursor }
                    nodes { __typename {{fields}} } } } }
            """;
        var slash = _repo.IndexOf('/');
        if (slash < 0) return null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var read = _gh.Run("api", "graphql", "--paginate", "-F", $"owner={_repo[..slash]}", "-F", $"name={_repo[(slash + 1)..]}", "-f", $"query={query}");
            if (!read.Ok) return null;
            List<JsonNode?> pages;
            try
            {
                pages = Json.ParseMany(read.Out);
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
            var issues = new JsonArray();
            var total = -1L;
            foreach (var page in pages)
            {
                if (page?["data"]?["repository"]?[connection] is not { } found) return null;
                foreach (var node in found["nodes"]?.AsArray() ?? [])
                    issues.Add(node?.DeepClone());
                if (found["totalCount"] is { } count)
                    total = Math.Max(total, count.GetValue<long>());
            }
            if (total >= 0 && issues.Count >= total) return issues;
        }
        return null;
    }

    /// <summary>Those issues and PRs as GitHub has them now.</summary>
    private JsonArray? FreshNodes(IReadOnlyList<string> ids)
    {
        var result = new JsonArray();
        for (var at = 0; at < ids.Count; at += 100)
        {
            var list = new JsonArray();
            foreach (var id in ids.Skip(at).Take(100))
                list.Add((JsonNode?)id);
            var page = _gh.Run("api", "graphql", "-f", $"query=query BoardRefresh {{ nodes(ids: {Json.Compact(list)}) {{ {ContentFields} }} }}");
            if (!page.Ok || Json.TryParse(page.Out) is not { } read) return null;
            foreach (var node in read["data"]?["nodes"]?.AsArray() ?? [])
                if (node is not null)
                    result.Add(node.DeepClone());
        }
        return result;
    }

    private Dictionary<string, Known> Had()
    {
        var had = new Dictionary<string, Known>();
        foreach (var row in _record.Query($"""
            SELECT item, status, json_extract(content, '$.shut') AS shut FROM board WHERE team = {TeamQ};
            """))
        {
            had[row["item"]!] = new Known(row["status"]!, row["shut"] == "1");
        }
        return had;
    }

    /// <summary>Brings each open issue and PR the board hasn't got onto it, and one Done on it back as it was.</summary>
    private void Arrive(JsonArray issues)
    {
        var plan = Reconciler.Arrivals(issues, Had(), _team, _repo, Now());
        _record.Exec($"BEGIN; {plan.Sql} COMMIT;");
        foreach (var (n, to) in plan.Arrived)
            Log("board", n, $"None → {to} · opened on GitHub");
        if (plan.Reopened.Count > 0 && FreshNodes(plan.Reopened) is { } fresh)
            Reconcile(fresh);
    }

    private void Reconcile(JsonArray nodes)
    {
        var plan = Reconciler.Plan(nodes, Had(), _team, _repo, Now());
        _record.Exec($"BEGIN; {plan.Sql} COMMIT;");
        foreach (var (n, type) in plan.Reopened)
        {
            string? to = null;
            foreach (var row in _record.Query($"SELECT what FROM events WHERE team = {TeamQ} AND item = {n} ORDER BY id;"))
                if (Reconciler.Landed(row.First!) is { } landed && landed != "Done")
                    to = landed;
            to ??= "Idea";
            if (to == "In review" && type == "Issue") to = "Ready";
            _record.Exec(StatusRoute.Set(TeamQ, n, to, Now()));
            Log("board", n, $"Done → {to} · reopened");
        }
    }

    /// <summary>One line of a card's history, after the change it describes.</summary>
    internal void Log(string who, string n, string what)
    {
        if (DryRun) return;
        try
        {
            _record.Exec($"INSERT INTO events (team, item, at, who, what) VALUES ({TeamQ}, {Reconciler.Q(n)}, {Reconciler.Q(Now())}, {Reconciler.Q(who)}, {Reconciler.Q(what)});");
        }
        catch (InvalidOperationException)
        {
            Console.Error.WriteLine($"board.sh: couldn't record '{what}' in #{n}'s history");
        }
    }

    /// <summary>Sets one of #n's marks in the record.</summary>
    internal void Mark(string n, string key, string json)
    {
        if (Would($"mark #{n} {key} {json}")) return;
        _record.Exec($"UPDATE board SET marks = json_set(COALESCE(marks, '{{}}'), '$.{key}', json({Reconciler.Q(json)})) WHERE team = {TeamQ} AND item = {n};");
    }

    internal void ClearMark(string n, string key)
    {
        if (Would($"clear #{n}'s {key}")) return;
        _record.Exec($"UPDATE board SET marks = json_remove(COALESCE(marks, '{{}}'), '$.{key}') WHERE team = {TeamQ} AND item = {n};");
    }

    /// <summary>Moves #n on the board. Who may do that is the route's to say, whatever the command.</summary>
    internal void SetStatus(string n, string from, string to, string who)
    {
        if (!StatusRoute.Permits(who, from, to))
            throw new InvalidOperationException($"the status route has no {who}: {from} -> {to}");
        if (Would($"set #{n} to '{to}'")) return;
        _record.Exec(StatusRoute.Set(TeamQ, n, to, Now()));
    }

    /// <summary>Puts an issue or PR on the board, with no status yet.</summary>
    internal void AddToBoard(string n, string node)
    {
        if (Would($"add #{n} to the board")) return;
        _record.Exec(StatusRoute.Enter(TeamQ, n, Reconciler.Q(node), StatusRoute.None, Now()));
        if (FreshNodes([node]) is { } nodes)
            Reconcile(nodes);
    }

    /// <summary>The board's items, as the record has them.</summary>
    internal JsonArray Items(bool open = false)
    {
        Sync();
        var items = new JsonArray();
        foreach (var row in _record.Query($"""
            SELECT b.item, b.node, b.status, b.priority, b.since, b.content, b.marks, up.priority AS pitch_priority
              FROM board b LEFT JOIN board up ON up.team = b.team AND up.item = json_extract(b.content, '$.parent')
              WHERE b.team = {TeamQ}{(open ? " AND b.status != 'Done'" : "")} ORDER BY b.rowid;
            """))
        {
            var c = Json.Parse(row["content"]!)!;
            var all = c["labels"]?.AsArray() ?? [];
            var labels = new JsonArray();
            foreach (var label in all)
                if (Json.Text(label) is { } name && !IsStateLabel(name))
                    labels.Add((JsonNode?)name);
            var pitch = all.Any(label => Json.Text(label) == "pitch");
            var status = row["status"]!;
            var follows = status != "Idea" && !pitch ? row["pitch_priority"] : null;
            var priority = row["priority"] ?? follows;
            var item = new JsonObject
            {
                ["number"] = long.Parse(row["item"]!, CultureInfo.InvariantCulture),
                ["node"] = row["node"],
                ["type"] = c["type"]?.DeepClone(),
                ["title"] = c["title"]?.DeepClone(),
                ["url"] = c["url"]?.DeepClone(),
                ["labels"] = labels,
                ["marks"] = string.IsNullOrEmpty(row["marks"]) ? MarksOf(all) : Json.Parse(row["marks"]!),
                ["blockedBy"] = Json.Truthy(c["blockedBy"]) ? c["blockedBy"]!.DeepClone() : 0,
                ["priority"] = priority,
                ["since"] = row["since"],
                ["parent"] = c["parent"]?.DeepClone(),
                ["status"] = status,
            };
            if (follows is not null) item["pitchPriority"] = follows;
            if (follows is not null && row["priority"] is null) item["inherited"] = true;
            if (Json.Truthy(c["closed"])) item["closed"] = c["closed"]!.DeepClone();
            if (Json.Truthy(c["dependabot"])) item["dependabot"] = true;
            if (Json.Truthy(c["author"])) item["author"] = c["author"]!.DeepClone();
            items.Add((JsonNode?)item);
        }
        return items;
    }

    internal List<JsonNode> AllCards() => Items().OfType<JsonNode>().ToList();

    /// <summary>#n as `list` prints it, or null if it isn't on the board.</summary>
    internal JsonNode? Item(string n)
    {
        if (!long.TryParse(n, out var number))
            throw new Stop(2, $"jq: Invalid JSON text passed to --argjson\n");
        return Items().FirstOrDefault(item => item!["number"]!.GetValue<long>() == number);
    }

    /// <summary>The marks an old label stood for, on a card with none of its own in the record.</summary>
    internal static JsonObject MarksOf(JsonArray labels)
    {
        var marks = new JsonObject();
        foreach (var node in labels)
        {
            if (Json.Text(node) is not { } label) continue;
            if (label == "blocked")
                marks["held"] = new JsonObject { ["at"] = null, ["by"] = null };
            else if (label == "a-team:shelved")
                marks["shelved"] = new JsonObject { ["until"] = null, ["priority"] = null };
            else if (label.StartsWith("a-team:", StringComparison.Ordinal))
                marks[label["a-team:".Length..]] = true;
            else if (label.StartsWith("rank:", StringComparison.Ordinal))
            {
                var rank = label["rank:".Length..];
                marks["rank"] = rank.Length == 0 ? "" : char.ToUpperInvariant(rank[0]) + rank[1..];
            }
            else if (label.StartsWith("theme: ", StringComparison.Ordinal))
                marks["theme"] = label["theme: ".Length..];
        }
        return marks;
    }
}
