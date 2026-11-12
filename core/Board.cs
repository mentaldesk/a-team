using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

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
    private const string RankField = "Lead rank";
    private const string ThemeField = "Theme";
    private const string Lagging = """
        (mirror IS NULL OR mirrored_status IS NOT status OR mirrored_priority IS NOT COALESCE(priority, 'none')
          OR COALESCE(mirrored_rank, '') IS NOT COALESCE(json_extract(marks, '$.rank'), '')
          OR COALESCE(mirrored_theme, '') IS NOT COALESCE(json_extract(marks, '$.theme'), ''))
        """;
    private const string IssueFields = """
        ... on Issue { id number title url state stateReason repository { nameWithOwner } labels(first: 20) { nodes { name } }
                         author { __typename login }
                         parent { number }
                         issueDependenciesSummary { blockedBy }
                         issueFieldValues(first: 20) { nodes { ... on IssueFieldSingleSelectValue {
                           name field { ... on IssueFieldSingleSelect { name } } } } } }
        """;
    private const string ContentFields = $$"""
        __typename
          {{IssueFields}}
          ... on PullRequest { id number title url state repository { nameWithOwner } labels(first: 20) { nodes { name } }
                               author { login } }
        """;
    private const string StatusValue = "fieldValueByName(name: $field) { ... on ProjectV2ItemFieldSingleSelectValue { name updatedAt } }";

    private readonly string _team;
    private readonly JsonObject _config;
    private readonly string _repo;
    private readonly string _owner;
    private readonly string _number;
    private readonly string _field;
    private readonly string _priority;
    private readonly string _kind;
    private readonly string _mirror;
    private readonly int _readEvery;
    private readonly Dictionary<string, string> _reverseMap = [];
    private readonly Lazy<Record> _opened;
    private readonly Gh _gh;
    private List<string>? _seen;
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
        _owner = Text("project", "owner");
        _number = Text("project", "number");
        _field = OrElse(Text("project", "statusField"), "Status");
        _priority = OrElse(Text("priorityField"), "Priority");
        _kind = OrElse(Text("project", "ownerType"), "organization");
        var mirrored = config["mirror"]?.GetValueKind() != System.Text.Json.JsonValueKind.False;
        _mirror = _number.Length > 0 && mirrored ? $"{_owner}/{_number}" : "";
        _readEvery = int.TryParse(Environment.GetEnvironmentVariable("A_TEAM_BOARD_READ_EVERY"), out var every) ? every : 900;
        foreach (var (shown, ours) in config["project"]?["statusMap"] as JsonObject ?? [])
            if (Json.Text(ours) is { } name)
                _reverseMap[name] = shown;
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
        foreach (var column in new[] { "marks", "mirrored_rank", "mirrored_theme" })
            if (!columns.Contains($"board.{column}"))
                AddColumn("board", column);
        foreach (var column in new[] { "repaired", "issues_read" })
            if (!columns.Contains($"boards.{column}"))
                AddColumn("boards", column);
        var was = _record.Scalar($"SELECT COALESCE((SELECT project FROM boards WHERE team = {TeamQ}), '-');");
        if (was == "-")
        {
            if (_mirror.Length > 0)
            {
                var nodes = MirrorNodes(shortEnds: true) ?? throw new Stop(1, _mirrorError);
                Reconcile(nodes, adopt: true);
                _seen = nodes.Select(node => Json.Text(node?["content"]?["id"])).OfType<string>().ToList();
            }
            var read = _mirror.Length > 0 ? Reconciler.Q(Now()) : "NULL";
            _record.Exec($"INSERT OR IGNORE INTO boards (team, project, read, repaired) VALUES ({TeamQ}, {Reconciler.Q(_mirror)}, {read}, {Reconciler.Q(Now())});");
        }
        else if (was != _mirror)
        {
            _record.Exec($"""
                UPDATE board SET mirror = NULL, mirrored_status = NULL, mirrored_priority = NULL, mirrored_rank = NULL,
                  mirrored_theme = NULL WHERE team = {TeamQ};
                UPDATE boards SET project = {Reconciler.Q(_mirror)}, read = NULL, failed = NULL, retry = NULL WHERE team = {TeamQ};
                """);
        }
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
    /// what the mirror's false set-backs undid before a status there stopped being a move.</summary>
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
            if (Json.Truthy(marks["upstream"]) && old.Contains("a-team:upstream") && UpstreamRefs(n) is { } refs)
                marks["upstream"] = refs;
            if (Json.Truthy(marks["shelved"]) && old.Contains("a-team:shelved") && Shelved(n) is { } shelf)
                marks["shelved"] = shelf;
            _record.Exec($"UPDATE board SET marks = {Reconciler.Q(Json.Compact(marks))} WHERE team = {TeamQ} AND item = {n};");
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

    /// <summary>Once a command, reads the open items, and every so often the whole mirror and the repo's open issues, then
    /// catches the mirror up. Where GitHub can't be read, the record stands as it is. <paramref name="cached"/> only moves the
    /// board over and carries any old labels into it.</summary>
    internal void Sync(bool cached = false)
    {
        if (_synced) return;
        _synced = true;
        BoardReady();
        AdoptLabels();
        if (cached) return;
        var seen = _seen ?? [];
        if (_mirror.Length > 0 && _seen is null && Stale("read") && MirrorNodes(shortEnds: false) is { } nodes)
        {
            Reconcile(nodes, adopt: false);
            _record.Exec($"UPDATE boards SET read = {Reconciler.Q(Now())} WHERE team = {TeamQ};");
            seen = nodes.Select(node => Json.Text(node?["content"]?["id"])).OfType<string>().ToList();
        }
        if (Stale("issues_read") && OpenIssues() is { } open)
        {
            Arrive(open);
            _record.Exec($"UPDATE boards SET issues_read = {Reconciler.Q(Now())} WHERE team = {TeamQ};");
        }
        var ids = _record.Query($"SELECT node FROM board WHERE team = {TeamQ} AND status != 'Done' AND node != '';")
            .Select(row => row.First!).Distinct().Except(seen).Order(StringComparer.Ordinal).ToList();
        if (ids.Count > 0 && FreshNodes(ids) is { } fresh)
            Reconcile(fresh, adopt: false);
        AdoptLabels();
        MirrorPush(null);
    }

    private bool Stale(string column) =>
        _record.Scalar($"SELECT COUNT(*) FROM boards WHERE team = {TeamQ} AND ({column} IS NULL OR {column} <= strftime('%Y-%m-%dT%H:%M:%SZ', 'now', '-{_readEvery} seconds'));") != "0";

    private string _mirrorError = "";

    private GhResult Gql(string query, params string[] rest)
    {
        var args = new List<string> { "api", "graphql", "-F", $"owner={_owner}", "-F", $"number={_number}" };
        args.AddRange(rest);
        args.Add("-f");
        args.Add($"query={query}");
        return _gh.Run([.. args]);
    }

    /// <summary>Every page of the mirror, read again once if it comes back short; null if it can't be read whole.</summary>
    private List<JsonNode?>? BoardPages(string query, bool shortEnds, params string[] rest)
    {
        var said = "";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var read = Gql(query, [.. rest, "--paginate"]);
            _mirrorError = read.Err;
            if (!read.Ok) return null;
            var pages = Json.ParseMany(read.Out);
            var got = 0;
            var total = 0L;
            foreach (var page in pages)
            {
                var items = page?["data"]?[_kind]?["projectV2"]?["items"];
                got += items?["nodes"]?.AsArray().Count ?? 0;
                if (items?["totalCount"] is { } count)
                    total = Math.Max(total, count.GetValue<long>());
            }
            if (got >= total) return pages;
            said = $"{got} of {total}";
        }
        if (shortEnds)
            throw new Die($"the board read came back incomplete ({said} items), twice. Nothing was changed: end this run, and the next one reads the board again.");
        return null;
    }

    /// <summary>Every item on the mirror, or null if it can't be read whole.</summary>
    private JsonArray? MirrorNodes(bool shortEnds)
    {
        var query = $$"""
            query($owner: String!, $number: Int!, $field: String!, $endCursor: String) {
                  {{_kind}}(login: $owner) { projectV2(number: $number) {
                    items(first: 100, after: $endCursor) {
                      totalCount pageInfo { hasNextPage endCursor }
                      nodes { id {{StatusValue}} content { {{ContentFields}} } } } } } }
            """;
        if (BoardPages(query, shortEnds, "-F", $"field={_field}") is not { } pages) return null;
        var nodes = new JsonArray();
        foreach (var page in pages)
        {
            foreach (var node in page?["data"]?[_kind]?["projectV2"]?["items"]?["nodes"]?.AsArray() ?? [])
            {
                if (node?["content"] is null) continue;
                nodes.Add((JsonNode?)new JsonObject
                {
                    ["project"] = new JsonObject
                    {
                        ["id"] = node["id"]?.DeepClone(),
                        ["status"] = node["fieldValueByName"]?["name"]?.DeepClone(),
                        ["since"] = node["fieldValueByName"]?["updatedAt"]?.DeepClone(),
                    },
                    ["content"] = node["content"]!.DeepClone(),
                });
            }
        }
        return nodes;
    }

    /// <summary>Every open issue on the repo, read again once if it comes back short; null if it can't be read whole.</summary>
    internal JsonArray? OpenIssues()
    {
        var query = $$"""
            query RepoIssues($owner: String!, $name: String!, $endCursor: String) {
                  repository(owner: $owner, name: $name) { issues(states: OPEN, first: 100, after: $endCursor) {
                    totalCount pageInfo { hasNextPage endCursor }
                    nodes { __typename {{IssueFields}} } } } }
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
                if (page?["data"]?["repository"]?["issues"] is not { } connection) return null;
                foreach (var node in connection["nodes"]?.AsArray() ?? [])
                    issues.Add(node?.DeepClone());
                if (connection["totalCount"] is { } count)
                    total = Math.Max(total, count.GetValue<long>());
            }
            if (total >= 0 && issues.Count >= total) return issues;
        }
        return null;
    }

    /// <summary>Those issues and PRs as GitHub has them now; `project` is null for one that isn't on the mirror.</summary>
    private JsonArray? FreshNodes(IReadOnlyList<string> ids)
    {
        var items = $"projectItems(first: 20) {{ nodes {{ id project {{ number owner {{ ... on Organization {{ login }} ... on User {{ login }} }} }}\n    {StatusValue} }} }}";
        items = $"... on Issue {{ {items} }} ... on PullRequest {{ {items} }}";
        var result = new JsonArray();
        for (var at = 0; at < ids.Count; at += 100)
        {
            var list = new JsonArray();
            foreach (var id in ids.Skip(at).Take(100))
                list.Add((JsonNode?)id);
            var chunk = Json.Compact(list);
            var page = _mirror.Length > 0
                ? _gh.Run("api", "graphql", "-F", $"field={_field}", "-f", $"query=query BoardRefresh($field: String!) {{ nodes(ids: {chunk}) {{ {ContentFields}\n        {items} }} }}")
                : _gh.Run("api", "graphql", "-f", $"query=query BoardRefresh {{ nodes(ids: {chunk}) {{ {ContentFields} }} }}");
            if (!page.Ok || Json.TryParse(page.Out) is not { } read) return null;
            foreach (var node in read["data"]?["nodes"]?.AsArray() ?? [])
            {
                if (node is null) continue;
                JsonObject? project = null;
                foreach (var candidate in node["projectItems"]?["nodes"]?.AsArray() ?? [])
                {
                    if (_number.Length == 0 || candidate?["project"]?["number"]?.ToJsonString() != _number) continue;
                    if (!string.Equals(Json.Text(candidate["project"]?["owner"]?["login"]), _owner, StringComparison.OrdinalIgnoreCase)) continue;
                    project = new JsonObject
                    {
                        ["id"] = candidate["id"]?.DeepClone(),
                        ["status"] = candidate["fieldValueByName"]?["name"]?.DeepClone(),
                        ["since"] = candidate["fieldValueByName"]?["updatedAt"]?.DeepClone(),
                    };
                    break;
                }
                var content = node.DeepClone().AsObject();
                content.Remove("projectItems");
                result.Add((JsonNode?)new JsonObject { ["project"] = project, ["content"] = content });
            }
        }
        return result;
    }

    private Dictionary<string, Known> Had()
    {
        var had = new Dictionary<string, Known>();
        foreach (var row in _record.Query($"""
            SELECT item, status, priority, since, mirror, mirrored_status, mirrored_priority,
              json_extract(content, '$.shut') AS shut FROM board WHERE team = {TeamQ};
            """))
        {
            had[row["item"]!] = new Known(row["status"]!, row["priority"], row["since"]!, row["mirrored_status"], row["mirrored_priority"], row["shut"] == "1");
        }
        return had;
    }

    /// <summary>Brings each open issue the board hasn't got onto it as an Idea, and one closed on it back as it was.</summary>
    private void Arrive(JsonArray issues)
    {
        var plan = Reconciler.Arrivals(issues, Had(), _team, _repo, _priority, Now());
        _record.Exec($"BEGIN; {plan.Sql} COMMIT;");
        foreach (var n in plan.Arrived)
            Log("board", n, "None → Idea · opened on GitHub");
        if (plan.Reopened.Count > 0 && FreshNodes(plan.Reopened) is { } fresh)
            Reconcile(fresh, adopt: false);
    }

    private void Reconcile(JsonArray nodes, bool adopt)
    {
        var plan = Reconciler.Plan(nodes, Had(), _team, _repo, _priority, _mirror, _reverseMap, Now(), adopt);
        _record.Exec($"BEGIN; {plan.Sql} COMMIT;");
        foreach (var (n, to, from) in plan.Bounces)
            Log("board", n, $"{from} on GitHub isn't a move: put back to {to}");
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
        foreach (var (n, to) in plan.Ranks)
        {
            _record.Exec($"UPDATE board SET priority = {Reconciler.Q(to)}, mirrored_priority = {Reconciler.Q(to)} WHERE team = {TeamQ} AND item = {n};");
            Log("you", n, $"{_priority} set to {to}");
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

    /// <summary>Moves #n on the board, then on its mirror. Who may do that is the route's to say, whatever the command.</summary>
    internal void SetStatus(string n, string from, string to, string who)
    {
        if (!StatusRoute.Permits(who, from, to))
            throw new InvalidOperationException($"the status route has no {who}: {from} -> {to}");
        if (Would($"set #{n} to '{to}'")) return;
        _record.Exec(StatusRoute.Set(TeamQ, n, to, Now()));
        MirrorPush(n);
    }

    /// <summary>Puts an issue or PR on the board, with no status yet, and on its mirror.</summary>
    internal void AddToBoard(string n, string node)
    {
        if (Would($"add #{n} to the board")) return;
        _record.Exec(StatusRoute.Enter(TeamQ, n, Reconciler.Q(node), StatusRoute.None, Now()));
        if (FreshNodes([node]) is { } nodes)
            Reconcile(nodes, adopt: false);
    }

    /// <summary>The board's items, as the record has them.</summary>
    internal JsonArray Items(bool open = false)
    {
        Sync();
        var items = new JsonArray();
        foreach (var row in _record.Query($"""
            SELECT b.item, b.node, b.status, b.priority, b.since, b.content, b.mirror, b.marks, up.priority AS pitch_priority
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
                ["id"] = row["mirror"] ?? "",
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

    private GhResult ProjectRaw() => Gql($$"""
        query($owner: String!, $number: Int!) {
              {{_kind}}(login: $owner) { projectV2(number: $number) { id title
                fields(first: 100) { nodes {
                  ... on ProjectV2Field { id name dataType }
                  ... on ProjectV2IterationField { id name dataType }
                  ... on ProjectV2SingleSelectField { id name dataType options { id name color description } } } } } } }
        """);

    private (JsonObject? Meta, string Err) ProjectMeta()
    {
        var raw = ProjectRaw();
        return (raw.Ok ? MetaOf(raw.Out) : null, raw.Err);
    }

    /// <summary>The project, with its Status field as `field`, its text fields as `rank` and `theme`, and as `clash` the names of those two it has as some other type.</summary>
    private JsonObject? MetaOf(string raw)
    {
        var project = Json.TryParse(raw)?["data"]?[_kind]?["projectV2"];
        if (project is null) return null;
        var fields = project["fields"]?["nodes"]?.AsArray() ?? [];
        JsonNode? Named(string name) => fields.FirstOrDefault(field => Json.Text(field?["name"]) == name);
        JsonNode? AsText(string name) => Named(name) is { } field && Json.Text(field["dataType"]) == "TEXT" ? field : null;
        var clash = new JsonArray();
        foreach (var name in new[] { RankField, ThemeField })
            if (Named(name) is not null && AsText(name) is null)
                clash.Add((JsonNode?)name);
        var status = Named(_field);
        return new JsonObject
        {
            ["id"] = project["id"]?.DeepClone(),
            ["title"] = project["title"]?.DeepClone(),
            ["field"] = status?["options"] is not null ? status.DeepClone() : null,
            ["rank"] = AsText(RankField)?.DeepClone(),
            ["theme"] = AsText(ThemeField)?.DeepClone(),
            ["clash"] = clash,
        };
    }

    private GhResult PriorityRead() =>
        _gh.Run("api", "graphql", "-F", $"owner={(_owner.Length > 0 ? _owner : _repo.Split('/')[0])}", "-f", """
            query=query($owner: String!) {
                  organization(login: $owner) { issueFields(first: 50) { nodes {
                    ... on IssueFieldSingleSelect { id name options { id name } } } } } }
            """);

    private JsonNode? PriorityField()
    {
        var read = PriorityRead();
        if (!read.Ok) return null;
        try
        {
            foreach (var node in Json.Parse(read.Out)?["data"]?["organization"]?["issueFields"]?["nodes"]?.AsArray() ?? [])
                if (Json.Text(node?["name"]) == _priority)
                    return node;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Text.Json.JsonException)
        {
            return null;
        }
        return null;
    }

    private void MirrorFailed(string err, string? own)
    {
        string why;
        string? retry = null;
        if (!string.IsNullOrEmpty(own))
        {
            why = $"GitHub refused: {own}";
        }
        else if (err.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
        {
            why = "GitHub rate limit";
            var reset = _gh.Run("api", "rate_limit", "--jq", "[.resources.graphql.reset, .resources.core.reset] | max");
            if (reset.Ok && long.TryParse(reset.Said, out var at))
                retry = Iso(at);
        }
        else if (Regex.IsMatch(err, @"could not resolve host|error connecting|connection (refused|reset|closed|timed out)|internet connection|timed out|i/o timeout|handshake timeout|client\.timeout|deadline exceeded|network is|no route to host|dial tcp|no such host|: EOF|unexpected EOF|HTTP 5[0-9][0-9]", RegexOptions.IgnoreCase))
        {
            why = "GitHub unreachable";
        }
        else
        {
            var line = err.Split('\n').FirstOrDefault(text => text.Trim().Length > 0) ?? "";
            if (line.StartsWith("gh: ", StringComparison.Ordinal)) line = line[4..];
            why = line.Length > 0 ? $"GitHub refused: {line}" : "GitHub refused";
        }
        retry ??= Iso(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300);
        _record.Exec($"UPDATE boards SET failed = {Reconciler.Q(why)}, retry = {Reconciler.Q(retry)} WHERE team = {TeamQ};");
    }

    /// <summary>The mirror's item for that issue or PR, where something else put it there first.</summary>
    private string? MirrorItem(string node) =>
        FreshNodes([node]) is [{ } fresh] ? Json.Text(fresh["project"]?["id"]) : null;

    private string StatusOnMirror(string status) =>
        (_config["project"]?["statusMap"] as JsonObject)?[status] is { } mapped && Json.Truthy(mapped) ? Json.Text(mapped) ?? status : status;

    /// <summary>Catches the mirror up with the record: one card, or the ones behind it when GitHub isn't being waited for.</summary>
    internal void MirrorPush(string? only)
    {
        if (_mirror.Length == 0 || DryRun) return;
        if (only is null && _record.Scalar($"SELECT COUNT(*) FROM boards WHERE team = {TeamQ} AND retry > {Reconciler.Q(Now())};") != "0")
            return;
        var rows = _record.Query($"""
            SELECT item, node, status, COALESCE(priority, 'none') AS priority, mirror,
                  mirrored_status AS shown, mirrored_priority AS ranked, json_extract(content, '$.type') AS type,
                  COALESCE(json_extract(marks, '$.rank'), '') AS rank, COALESCE(mirrored_rank, '') AS shown_rank,
                  COALESCE(json_extract(marks, '$.theme'), '') AS theme, COALESCE(mirrored_theme, '') AS shown_theme FROM board
                WHERE team = {TeamQ} AND node != '' AND {Lagging}{(only is null ? "" : $" AND item = {only}")}
                ORDER BY status = 'Done', since DESC LIMIT 20;
            """);
        if (rows.Count == 0)
        {
            if (only is null)
                _record.Exec($"UPDATE boards SET failed = NULL, retry = NULL WHERE team = {TeamQ};");
            return;
        }
        var (meta, metaErr) = ProjectMeta();
        if (meta is null || !Json.Truthy(meta["field"]?["id"]))
        {
            MirrorFailed(metaErr, meta is null ? null : $"no single-select field '{_field}' on {_owner} project {_number}");
            return;
        }
        var projectId = Json.Text(meta["id"])!;
        var err = "";
        string? own = null;
        JsonNode? priorityField = null;
        var priorityRead = false;
        foreach (var row in rows)
        {
            var n = row["item"]!;
            var node = row["node"]!;
            var status = row["status"]!;
            var priority = row["priority"]!;
            var mirror = row["mirror"];
            var type = row["type"];
            if (mirror is null)
            {
                var added = _gh.Run("api", "graphql", "-F", $"project={projectId}", "-F", $"content={node}", "-f", """
                    query=
                            mutation($project: ID!, $content: ID!) {
                              addProjectV2ItemById(input: {projectId: $project, contentId: $content}) { item { id } } }
                    """, "--jq", ".data.addProjectV2ItemById.item.id");
                err = added.Err;
                mirror = added.Ok ? added.Said : added.Err.Contains("already exists", StringComparison.Ordinal) ? MirrorItem(node) : null;
                if (mirror is null) break;
                _record.Exec($"UPDATE board SET mirror = {Reconciler.Q(mirror)} WHERE team = {TeamQ} AND item = {n};");
            }
            if (status != (row["shown"] ?? "-"))
            {
                if (status != StatusRoute.None)
                {
                    var wanted = StatusOnMirror(status);
                    var option = meta["field"]?["options"]?.AsArray().FirstOrDefault(o => Json.Text(o?["name"]) == wanted);
                    if (option is null)
                    {
                        own = $"no '{status}' option on {_owner} project {_number}'s {_field}";
                        break;
                    }
                    var set = _gh.Run("api", "graphql", "-F", $"project={projectId}", "-F", $"item={mirror}", "-F", $"field={Json.Text(meta["field"]?["id"])}",
                        "-f", $"option={Json.Text(option["id"])}", "-f", """
                        query=
                                  mutation($project: ID!, $item: ID!, $field: ID!, $option: String!) {
                                    updateProjectV2ItemFieldValue(input: {projectId: $project, itemId: $item, fieldId: $field,
                                                                          value: {singleSelectOptionId: $option}}) { clientMutationId } }
                        """);
                    err = set.Err;
                    if (!set.Ok) break;
                }
                _record.Exec($"UPDATE board SET mirrored_status = {Reconciler.Q(status)} WHERE team = {TeamQ} AND item = {n};");
            }
            if (priority != (row["ranked"] ?? "-"))
            {
                if (type == "Issue" && !priorityRead)
                {
                    priorityField = PriorityField();
                    priorityRead = true;
                }
                if (type == "Issue" && priorityField is not null)
                {
                    GhResult result;
                    if (priority == "none")
                    {
                        result = _gh.Run("api", "graphql", "-F", $"issue={node}", "-F", $"field={Json.Text(priorityField["id"])}", "-f", """
                            query=
                                        mutation($issue: ID!, $field: ID!) {
                                          updateIssueFieldValue(input: {issueId: $issue, issueField: {fieldId: $field, delete: true}}) {
                                            clientMutationId } }
                            """);
                    }
                    else
                    {
                        var option = priorityField["options"]?.AsArray().FirstOrDefault(o => Json.Text(o?["name"]) == priority);
                        if (option is null)
                        {
                            own = $"no '{priority}' option on {_owner}'s {_priority} field";
                            break;
                        }
                        result = _gh.Run("api", "graphql", "-F", $"issue={node}", "-F", $"field={Json.Text(priorityField["id"])}", "-F", $"option={Json.Text(option["id"])}", "-f", """
                            query=
                                        mutation($issue: ID!, $field: ID!, $option: ID!) {
                                          updateIssueFieldValue(input: {issueId: $issue, issueField: {fieldId: $field,
                                                                        singleSelectOptionId: $option}}) { clientMutationId } }
                            """);
                    }
                    err = result.Err;
                    if (!result.Ok) break;
                }
                _record.Exec($"UPDATE board SET mirrored_priority = {Reconciler.Q(priority)} WHERE team = {TeamQ} AND item = {n};");
            }
            var stop = false;
            foreach (var key in new[] { "rank", "theme" })
            {
                var value = row[key]!;
                if (value == row[$"shown_{key}"]) continue;
                var field = Json.Text(meta[key]?["id"]);
                if (field is null)
                {
                    if (value.Length == 0)
                    {
                        _record.Exec($"UPDATE board SET mirrored_{key} = '' WHERE team = {TeamQ} AND item = {n};");
                        continue;
                    }
                    var name = TextFieldName(key);
                    if (meta["clash"]!.AsArray().Any(clash => Json.Text(clash) == name))
                    {
                        own = FieldClash(name);
                        stop = true;
                        break;
                    }
                    var created = AddTextField(projectId, name);
                    err = created.Err;
                    if (!created.Ok)
                    {
                        stop = true;
                        break;
                    }
                    field = created.Said;
                    if (field.Length == 0 || field == "null")
                    {
                        own = $"couldn't add a {key} field to {_owner} project {_number}";
                        stop = true;
                        break;
                    }
                    meta[key] = new JsonObject { ["id"] = field };
                }
                var written = value.Length > 0
                    ? _gh.Run("api", "graphql", "-F", $"project={projectId}", "-F", $"item={mirror}", "-F", $"field={field}", "-f", $"text={value}", "-f", """
                        query=
                                  mutation($project: ID!, $item: ID!, $field: ID!, $text: String!) {
                                    updateProjectV2ItemFieldValue(input: {projectId: $project, itemId: $item, fieldId: $field,
                                                                          value: {text: $text}}) { clientMutationId } }
                        """)
                    : _gh.Run("api", "graphql", "-F", $"project={projectId}", "-F", $"item={mirror}", "-F", $"field={field}", "-f", """
                        query=
                                  mutation($project: ID!, $item: ID!, $field: ID!) {
                                    clearProjectV2ItemFieldValue(input: {projectId: $project, itemId: $item, fieldId: $field}) {
                                      clientMutationId } }
                        """);
                err = written.Err;
                if (!written.Ok)
                {
                    stop = true;
                    break;
                }
                _record.Exec($"UPDATE board SET mirrored_{key} = {Reconciler.Q(value)} WHERE team = {TeamQ} AND item = {n};");
            }
            if (stop || err.Length > 0 || own is not null) break;
            err = "";
        }
        if (err.Length > 0 || own is not null)
            MirrorFailed(err, own);
        else if (only is null || _record.Scalar($"SELECT COUNT(*) FROM board WHERE team = {TeamQ} AND node != '' AND {Lagging};") == "0")
            _record.Exec($"UPDATE boards SET failed = NULL, retry = NULL WHERE team = {TeamQ};");
    }
}
