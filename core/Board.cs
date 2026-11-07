using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ATeam.Core;

/// <summary>This path isn't ported yet, or needs what only board.sh does: nothing is lost by running it there instead.</summary>
internal sealed class Fallback(string reason) : Exception(reason);

/// <summary>A refusal: "board.sh: " and this, on stderr, and exit 1.</summary>
internal sealed class Die(string message) : Exception(message);

/// <summary>A team's board, as one command sees it: its config, its record and GitHub.</summary>
internal sealed class Board
{
    internal const int Format = 1;
    private const string RankField = "Lead rank";
    private const string ThemeField = "Theme";
    private const string Lagging = """
        (mirror IS NULL OR mirrored_status IS NOT status OR mirrored_priority IS NOT COALESCE(priority, 'none')
          OR COALESCE(mirrored_rank, '') IS NOT COALESCE(json_extract(marks, '$.rank'), '')
          OR COALESCE(mirrored_theme, '') IS NOT COALESCE(json_extract(marks, '$.theme'), ''))
        """;
    private const string ContentFields = """
        __typename
          ... on Issue { id number title url state stateReason repository { nameWithOwner } labels(first: 20) { nodes { name } }
                         parent { number }
                         issueDependenciesSummary { blockedBy }
                         issueFieldValues(first: 20) { nodes { ... on IssueFieldSingleSelectValue {
                           name field { ... on IssueFieldSingleSelect { name } } } } } }
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
    private readonly Record _record;
    private readonly Gh _gh;
    private List<string>? _seen;
    private bool _synced;

    internal string Team => _team;

    /// <summary>Set once a command has done what can't be repeated, so a failure after it can't start the command again in board.sh.</summary>
    internal bool Committed { get; set; }

    internal Board(string team, JsonObject config, Record record, Gh gh)
    {
        _team = team;
        _config = config;
        _record = record;
        _gh = gh;
        _repo = Text("repo");
        _owner = Text("project", "owner");
        _number = Text("project", "number");
        _field = OrElse(Text("project", "statusField"), "Status");
        _priority = OrElse(Text("priorityField"), "Priority");
        _kind = OrElse(Text("project", "ownerType"), "organization");
        var mirrored = config["mirror"]?.GetValueKind() != System.Text.Json.JsonValueKind.False;
        _mirror = _number.Length > 0 && mirrored ? $"{_owner}/{_number}" : "";
        _readEvery = int.TryParse(Environment.GetEnvironmentVariable("A_TEAM_BOARD_READ_EVERY"), out var every) ? every : 900;
        foreach (var (shown, ours) in config["project"]?["statusMap"]?.AsObject() ?? [])
            if (Json.Text(ours) is { } name)
                _reverseMap[name] = shown;
    }

    private static string OrElse(string text, string other) => text.Length > 0 ? text : other;

    private string Text(params string[] path)
    {
        JsonNode? node = _config;
        foreach (var key in path)
            node = node?[key];
        return node is null || !Json.Truthy(node) ? "" : Json.Text(node) ?? node.ToJsonString();
    }

    private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Iso(long seconds) =>
        DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private string TeamQ => Reconciler.Q(_team);

    internal void Gate()
    {
        var top = _record.Scalar("SELECT format FROM formats ORDER BY format DESC LIMIT 1;");
        if (top is not null && int.Parse(top, CultureInfo.InvariantCulture) > Format)
            throw new Fallback("the board is in a newer format");
        var own = _record.Scalar("SELECT format FROM formats WHERE team = ?;", _team);
        if (own is null || int.Parse(own, CultureInfo.InvariantCulture) != Format)
            throw new Fallback("the board's format needs stamping or moving");
    }

    private void BoardReady()
    {
        var columns = _record.Query("SELECT 'board.' || name AS name FROM pragma_table_info('board') UNION ALL SELECT 'boards.' || name FROM pragma_table_info('boards');")
            .Select(row => row.First).ToHashSet();
        foreach (var column in new[] { "board.marks", "board.mirrored_rank", "board.mirrored_theme", "boards.repaired" })
            if (!columns.Contains(column))
                throw new Fallback($"the record has no {column} yet");
        var was = _record.Scalar($"SELECT COALESCE((SELECT project FROM boards WHERE team = {TeamQ}), '-');");
        if (was == "-")
        {
            if (_mirror.Length > 0)
            {
                var nodes = MirrorNodes(shortEnds: true) ?? throw new Fallback("the mirror's first read failed");
                Reconcile(nodes, adopt: true);
                RefuseLabelsOnlyTheShellAdopts();
                _seen = nodes.Select(node => Json.Text(node?["content"]?["id"])).OfType<string>().ToList();
            }
            var read = _mirror.Length > 0 ? Reconciler.Q(Now()) : "NULL";
            _record.Exec($"INSERT INTO boards (team, project, read, repaired) VALUES ({TeamQ}, {Reconciler.Q(_mirror)}, {read}, {Reconciler.Q(Now())});");
        }
        else if (was != _mirror)
        {
            _record.Exec($"""
                UPDATE board SET mirror = NULL, mirrored_status = NULL, mirrored_priority = NULL, mirrored_rank = NULL,
                  mirrored_theme = NULL WHERE team = {TeamQ};
                UPDATE boards SET project = {Reconciler.Q(_mirror)}, read = NULL, failed = NULL, retry = NULL WHERE team = {TeamQ};
                """);
        }
        if (_record.Scalar($"SELECT COUNT(*) FROM boards WHERE team = {TeamQ} AND repaired IS NULL;") != "0")
            throw new Fallback("the board's last moves need repairing");
    }

    /// <summary>Carries the old a-team labels on open items into the record, then takes them off on GitHub.</summary>
    private void AdoptLabels()
    {
        RefuseLabelsOnlyTheShellAdopts();
        foreach (var row in OpenCards())
        {
            var labels = Json.Parse(row["content"]!)?["labels"]?.AsArray() ?? [];
            var old = OldLabels(row);
            if (old.Count == 0) continue;
            var marks = Json.Parse(string.IsNullOrEmpty(row["marks"]) ? "{}" : row["marks"]!)!.AsObject();
            var label2 = new JsonArray();
            foreach (var label in old)
                label2.Add((JsonNode?)label);
            foreach (var (key, value) in MarksOf(label2))
                marks[key] = value?.DeepClone();
            var n = row["item"]!;
            _record.Exec($"UPDATE board SET marks = {Reconciler.Q(Json.Compact(marks))} WHERE team = {TeamQ} AND item = {n};");
            var gone = new HashSet<string>();
            foreach (var label in old)
                if (_gh.Run("api", "-X", "DELETE", $"repos/{_repo}/issues/{n}/labels/{Uri.EscapeDataString(label)}").Ok)
                    gone.Add(label);
            var kept = new JsonArray();
            foreach (var label in labels)
                if (Json.Text(label) is not { } name || !gone.Contains(name))
                    kept.Add(label?.DeepClone());
            _record.Exec($"UPDATE board SET content = json_set(content, '$.labels', json({Reconciler.Q(Json.Compact(kept))})) WHERE team = {TeamQ} AND item = {n};");
        }
    }

    private List<Row> OpenCards() =>
        _record.Query($"SELECT item, marks, content FROM board WHERE team = {TeamQ} AND status != 'Done' AND json_extract(content, '$.shut') IS NULL;");

    private static List<string> OldLabels(Row row) =>
        (Json.Parse(row["content"]!)?["labels"]?.AsArray() ?? []).Select(Json.Text).OfType<string>().Where(IsStateLabel).ToList();

    /// <summary>Checked before anything moves on, so board.sh finds things exactly as it would have.</summary>
    private void RefuseLabelsOnlyTheShellAdopts()
    {
        var all = OpenCards().SelectMany(OldLabels).ToList();
        if (all.Count == 0) return;
        if (all.Any(label => label is "blocked" or "a-team:shelved" or "a-team:upstream"))
            throw new Fallback("labels that carry what GitHub has to be asked for");
        if (IsRealState)
            throw new Fallback("old labels on the real state, which only the installed a-team moves");
    }

    private static bool IsRealState
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            var real = Path.Combine(string.IsNullOrEmpty(xdg) ? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", ".local/state") : xdg, "a-team");
            return Path.GetFullPath(State) == Path.GetFullPath(real);
        }
    }

    internal static string State { get; set; } = "";

    private static bool IsStateLabel(string label) =>
        label.StartsWith("a-team:", StringComparison.Ordinal) || label.StartsWith("rank:", StringComparison.Ordinal)
        || label.StartsWith("theme: ", StringComparison.Ordinal) || label == "blocked";

    internal void Sync()
    {
        if (_synced) return;
        _synced = true;
        BoardReady();
        AdoptLabels();
        var seen = _seen ?? [];
        if (_mirror.Length > 0 && _seen is null)
        {
            var stale = _record.Scalar($"SELECT COUNT(*) FROM boards WHERE team = {TeamQ} AND (read IS NULL OR read <= strftime('%Y-%m-%dT%H:%M:%SZ', 'now', '-{_readEvery} seconds'));");
            if (stale != "0" && MirrorNodes(shortEnds: false) is { } nodes)
            {
                Reconcile(nodes, adopt: false);
                _record.Exec($"UPDATE boards SET read = {Reconciler.Q(Now())} WHERE team = {TeamQ};");
                seen = nodes.Select(node => Json.Text(node?["content"]?["id"])).OfType<string>().ToList();
            }
        }
        var ids = _record.Query($"SELECT node FROM board WHERE team = {TeamQ} AND status != 'Done' AND node != '';")
            .Select(row => row.First!).Distinct().Except(seen).Order(StringComparer.Ordinal).ToList();
        if (ids.Count > 0 && FreshNodes(ids) is { } fresh)
            Reconcile(fresh, adopt: false);
        AdoptLabels();
        MirrorPush(null);
    }

    private GhResult Gql(string query, params string[] rest)
    {
        var args = new List<string> { "api", "graphql", "-F", $"owner={_owner}", "-F", $"number={_number}" };
        args.AddRange(rest);
        args.Add("-f");
        args.Add($"query={query}");
        return _gh.Run([.. args]);
    }

    /// <summary>Every item on the mirror, read again once if it comes back short; null if it can't be read whole.</summary>
    private JsonArray? MirrorNodes(bool shortEnds)
    {
        var query = $$"""
            query($owner: String!, $number: Int!, $field: String!, $endCursor: String) {
                  {{_kind}}(login: $owner) { projectV2(number: $number) {
                    items(first: 100, after: $endCursor) {
                      totalCount pageInfo { hasNextPage endCursor }
                      nodes { id {{StatusValue}} content { {{ContentFields}} } } } } } }
            """;
        var said = "";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var read = Gql(query, "-F", $"field={_field}", "--paginate");
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
            if (got < total)
            {
                said = $"{got} of {total}";
                continue;
            }
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
        if (shortEnds)
            throw new Die($"the board read came back incomplete ({said} items), twice. Nothing was changed: end this run, and the next one reads the board again.");
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
            if (!page.Ok) return null;
            foreach (var node in Json.Parse(page.Out)?["data"]?["nodes"]?.AsArray() ?? [])
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

    private void Reconcile(JsonArray nodes, bool adopt)
    {
        var had = new Dictionary<string, Known>();
        foreach (var row in _record.Query($"""
            SELECT item, status, priority, since, mirror, mirrored_status, mirrored_priority,
              json_extract(content, '$.shut') AS shut FROM board WHERE team = {TeamQ};
            """))
        {
            had[row["item"]!] = new Known(row["status"]!, row["priority"], row["since"]!, row["mirrored_status"], row["mirrored_priority"], row["shut"] == "1");
        }
        var plan = Reconciler.Plan(nodes, had, _team, _repo, _priority, _mirror, _reverseMap, Now(), adopt);
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
            _record.Exec($"UPDATE board SET status = {Reconciler.Q(to)}, since = {Reconciler.Q(Now())} WHERE team = {TeamQ} AND item = {n};");
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
        try
        {
            _record.Exec($"INSERT INTO events (team, item, at, who, what) VALUES ({TeamQ}, {Reconciler.Q(n)}, {Reconciler.Q(Now())}, {Reconciler.Q(who)}, {Reconciler.Q(what)});");
        }
        catch (InvalidOperationException)
        {
            Console.Error.WriteLine($"board.sh: couldn't record '{what}' in #{n}'s history");
        }
    }

    internal void Mark(string n, string key, string json)
    {
        _record.Exec($"UPDATE board SET marks = json_set(COALESCE(marks, '{{}}'), '$.{key}', json({Reconciler.Q(json)})) WHERE team = {TeamQ} AND item = {n};");
    }

    internal void ClearMark(string n, string key)
    {
        _record.Exec($"UPDATE board SET marks = json_remove(COALESCE(marks, '{{}}'), '$.{key}') WHERE team = {TeamQ} AND item = {n};");
    }

    internal void SetStatus(string n, string status)
    {
        _record.Exec($"UPDATE board SET status = {Reconciler.Q(status)}, since = {Reconciler.Q(Now())} WHERE team = {TeamQ} AND item = {n};");
        MirrorPush(n);
    }

    internal void AddToBoard(string n, string node)
    {
        _record.Exec($"INSERT OR IGNORE INTO board (team, item, node, status, since) VALUES ({TeamQ}, {n}, {Reconciler.Q(node)}, 'None', {Reconciler.Q(Now())});");
        if (FreshNodes([node]) is { } nodes)
            Reconcile(nodes, adopt: false);
    }

    /// <summary>The board's items, as the record has them.</summary>
    internal JsonArray Items()
    {
        Sync();
        var items = new JsonArray();
        foreach (var row in _record.Query($"""
            SELECT b.item, b.node, b.status, b.priority, b.since, b.content, b.mirror, b.marks, up.priority AS pitch_priority
              FROM board b LEFT JOIN board up ON up.team = b.team AND up.item = json_extract(b.content, '$.parent')
              WHERE b.team = {TeamQ} ORDER BY b.rowid;
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
            var priority = row["priority"] ?? (status == "Idea" || pitch ? null : row["pitch_priority"]);
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
            if (Json.Truthy(c["closed"])) item["closed"] = c["closed"]!.DeepClone();
            if (Json.Truthy(c["dependabot"])) item["dependabot"] = true;
            items.Add((JsonNode?)item);
        }
        return items;
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

    private (JsonObject? Meta, string Err) ProjectMeta()
    {
        var raw = Gql($$"""
            query($owner: String!, $number: Int!) {
                  {{_kind}}(login: $owner) { projectV2(number: $number) { id title
                    fields(first: 100) { nodes {
                      ... on ProjectV2Field { id name dataType }
                      ... on ProjectV2IterationField { id name dataType }
                      ... on ProjectV2SingleSelectField { id name dataType options { id name color description } } } } } } }
            """);
        if (!raw.Ok) return (null, raw.Err);
        var project = Json.Parse(raw.Out)?["data"]?[_kind]?["projectV2"];
        if (project is null) return (null, raw.Err);
        var fields = project["fields"]?["nodes"]?.AsArray() ?? [];
        JsonNode? Named(string name) => fields.FirstOrDefault(field => Json.Text(field?["name"]) == name);
        JsonNode? AsText(string name) => Named(name) is { } field && Json.Text(field["dataType"]) == "TEXT" ? field : null;
        var clash = new JsonArray();
        foreach (var name in new[] { RankField, ThemeField })
            if (Named(name) is not null && AsText(name) is null)
                clash.Add((JsonNode?)name);
        var status = Named(_field);
        return (new JsonObject
        {
            ["id"] = project["id"]?.DeepClone(),
            ["title"] = project["title"]?.DeepClone(),
            ["field"] = status?["options"] is not null ? status.DeepClone() : null,
            ["rank"] = AsText(RankField)?.DeepClone(),
            ["theme"] = AsText(ThemeField)?.DeepClone(),
            ["clash"] = clash,
        }, raw.Err);
    }

    private JsonNode? PriorityField()
    {
        var read = _gh.Run("api", "graphql", "-F", $"owner={(_owner.Length > 0 ? _owner : _repo.Split('/')[0])}", "-f", """
            query=query($owner: String!) {
                  organization(login: $owner) { issueFields(first: 50) { nodes {
                    ... on IssueFieldSingleSelect { id name options { id name } } } } } }
            """);
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

    private string StatusOnMirror(string status) =>
        _config["project"]?["statusMap"]?[status] is { } mapped && Json.Truthy(mapped) ? Json.Text(mapped) ?? status : status;

    /// <summary>Catches the mirror up with the record: one card, or the ones behind it when GitHub isn't being waited for.</summary>
    internal void MirrorPush(string? only)
    {
        if (_mirror.Length == 0) return;
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
                if (!added.Ok) break;
                mirror = added.Said;
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
                    var name = key == "rank" ? RankField : ThemeField;
                    if (meta["clash"]!.AsArray().Any(clash => Json.Text(clash) == name))
                    {
                        own = $"{_owner} project {_number} has a {name} field that isn't text, so a-team can't copy to it: rename that field";
                        stop = true;
                        break;
                    }
                    var created = _gh.Run("api", "graphql", "-F", $"project={projectId}", "-f", $"name={name}", "-f", """
                        query=mutation($project: ID!, $name: String!) {
                              createProjectV2Field(input: {projectId: $project, dataType: TEXT, name: $name}) {
                                projectV2Field { ... on ProjectV2Field { id } } } }
                        """, "--jq", ".data.createProjectV2Field.projectV2Field.id");
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

    internal GhResult Label(string n) => _gh.Run("issue", "edit", n, "-R", _repo, "--add-label", "pitch");

    internal GhResult LabelNew(string n) => _gh.Run("api", "-X", "POST", $"repos/{_repo}/issues/{n}/labels", "-f", "labels[]=pitch");

    internal GhResult Issue(string n, string jq) => _gh.Run("api", $"repos/{_repo}/issues/{n}", "--jq", jq);

    internal GhResult Content(string kind, string n, string jq) => _gh.Run("api", $"repos/{_repo}/{kind}/{n}", "--jq", jq);
}
