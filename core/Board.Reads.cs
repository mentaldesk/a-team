using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ATeam.Core;

/// <summary>What the read commands ask of the record, and of GitHub.</summary>
internal sealed partial class Board
{
    private const string AttachmentBranch = "a-team/attachments";
    private const string AttachmentTypes = "png|jpe?g|gif|webp";
    private const string SeenBy = "reactions(content: EYES) { totalCount }";
    private const string TalkedOn = $$"""
        number createdAt body author { __typename login }
                comments(last: 50) { nodes { createdAt body url author { __typename login } {{SeenBy}} } }
        """;
    private const string TalkedOnPr = $$"""
        {{TalkedOn}} url isDraft mergeable mergeStateStatus baseRefName headRefName
                      reviews(last: 50) { nodes { createdAt body state author { __typename login } {{SeenBy}}
                      comments(first: 50) { nodes { createdAt body author { __typename login } {{SeenBy}} } } } }
        """;
    private const string RegisterColumns = """
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

    /// <summary>Whether this state has a record to read at all.</summary>
    internal static bool Recorded => File.Exists(Path.Combine(State, "history.db"));

    private static string SqlNow(string modifier) => $"strftime('%Y-%m-%dT%H:%M:%SZ', 'now', '{modifier}')";

    /// <summary>To each of those whose body links an attachment adds `images`, a local copy of each, for an agent to look at.</summary>
    internal void WithImages(IReadOnlyList<JsonObject> said)
    {
        var pattern = $"https://github\\.com/{Regex.Escape(_repo)}/blob/{AttachmentBranch}/[0-9]{{4}}-[0-9]{{2}}/[0-9a-f]+\\.({AttachmentTypes})";
        var links = said.SelectMany(one => Regex.Matches(Json.Text(one["body"]) ?? "", pattern).Select(match => match.Value))
            .Distinct().Order(StringComparer.Ordinal).ToList();
        var copies = new List<(string Link, string Copy)>();
        foreach (var link in links)
            if (AttachmentCopy(link) is { } copy)
                copies.Add((link, copy));
        foreach (var one in said)
        {
            var body = Json.Text(one["body"]) ?? "";
            var images = new JsonArray();
            foreach (var (link, copy) in copies)
                if (body.Contains(link, StringComparison.Ordinal))
                    images.Add((JsonNode?)copy);
            if (images.Count > 0) one["images"] = images;
        }
    }

    /// <summary>Where an attachment's local copy is, fetched if it isn't there yet; null if GitHub won't give it.</summary>
    private string? AttachmentCopy(string link)
    {
        var folder = $"/blob/{AttachmentBranch}/";
        var path = link[(link.IndexOf(folder, StringComparison.Ordinal) + folder.Length)..];
        var temp = Environment.GetEnvironmentVariable("TMPDIR");
        var copy = $"{(string.IsNullOrEmpty(temp) ? "/tmp" : temp)}/a-team-attachments/{_repo}/{path}";
        if (File.Exists(copy) && new FileInfo(copy).Length > 0) return copy;
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        var part = $"{copy}.part";
        if (!_gh.Save(part, "api", $"repos/{_repo}/contents/{path}?ref={AttachmentBranch}", "-H", "Accept: application/vnd.github.raw"))
        {
            File.Delete(part);
            return null;
        }
        File.Move(part, copy, overwrite: true);
        return copy;
    }

    /// <summary>What's been said on #n and on the open PR that closes it, oldest first, each saying who by.</summary>
    internal JsonArray ConversationOn(string n)
    {
        var conversation = new JsonArray();
        if (!long.TryParse(n, out _)) return conversation;
        foreach (var remark in Remarks(TalkOn(n)).OrderBy(remark => Json.Text(remark["at"]), StringComparer.Ordinal))
            conversation.Add((JsonNode?)new JsonObject
            {
                ["who"] = WhoSaid(remark),
                ["at"] = remark["at"]?.DeepClone(),
                ["pr"] = remark["pr"]?.DeepClone(),
                ["url"] = remark["url"]?.DeepClone(),
                ["description"] = Json.Truthy(remark["description"]),
                ["body"] = WithoutMarkers(Json.Text(remark["body"]) ?? ""),
            });
        return conversation;
    }

    private static IEnumerable<JsonObject> Remarks(JsonObject repository)
    {
        foreach (var (_, value) in repository)
        {
            if (value is null) continue;
            foreach (var comment in value["comments"]?["nodes"]?.AsArray() ?? [])
                yield return Remark(comment!, null);
            foreach (var pr in value["closedByPullRequestsReferences"]?["nodes"]?.AsArray() ?? [])
            {
                var description = Remark(pr!, pr!["number"]);
                description["description"] = true;
                yield return description;
                foreach (var comment in pr["comments"]?["nodes"]?.AsArray() ?? [])
                    yield return Remark(comment!, pr["number"]);
            }
        }
    }

    private static string WithoutMarkers(string body) =>
        Regex.Replace(Regex.Replace(body, @"[ \t]*<!-- a-team:(lead|dev|customer|reviewer|shelved [^>]*) -->[ \t]*", ""), @"\s+$", "");

    private static JsonObject Remark(JsonNode said, JsonNode? pr) => new()
    {
        ["at"] = said["createdAt"]?.DeepClone(),
        ["author"] = LoginOf(said["author"]),
        ["body"] = Json.Text(said["body"]) ?? "",
        ["pr"] = pr?.DeepClone(),
        ["url"] = said["url"]?.DeepClone(),
    };

    private string WhoSaid(JsonObject remark)
    {
        foreach (var role in StatusRoute.Roles)
            if (TeamSaid(remark, $"<!-- a-team:{role} -->"))
                return role;
        var author = Json.Text(remark["author"]) ?? "";
        return Stakeholders.Contains(author) ? "you" : author;
    }

    /// <summary>One page holding #n's comments and its open PR, plus one more if GitHub hasn't linked that PR to it.</summary>
    private JsonObject TalkOn(string n)
    {
        var (owner, name) = RepoParts;
        var asked = $$"""
             x{{n}}: issueOrPullRequest(number: {{n}}) {
                  ... on Issue { {{TalkedOn}} subIssuesSummary { total completed }
                    closedByPullRequestsReferences(first: 1, includeClosedPrs: false) { nodes { {{TalkedOnPr}} } } }
                  ... on PullRequest { {{TalkedOnPr}} } }
            """;
        var read = _gh.Run("api", "graphql", "-F", $"owner={owner}", "-F", $"name={name}", "-f", $$"""
            query=query($owner: String!, $name: String!) { repository(owner: $owner, name: $name) {{{asked}}
                  openPrs: pullRequests(states: OPEN, first: 100) { nodes { number body } } } }
            """);
        if (!read.Ok) throw new Die($"can't read the conversation on #{n} ({FirstLine(read.Err)})");
        var repository = Json.Parse(read.Out)?["data"]?["repository"]?.AsObject() ?? [];
        var unlinked = Unlinked(repository);
        if (unlinked.Count > 0)
        {
            var more = string.Concat(unlinked.Values.Distinct().Order().Select(pr => $" p{pr}: pullRequest(number: {pr}) {{ {TalkedOnPr} }}"));
            var linked = _gh.Run("api", "graphql", "-F", $"owner={owner}", "-F", $"name={name}", "-f", $$"""
                query=query($owner: String!, $name: String!) { repository(owner: $owner, name: $name) {{{more}}} }
                """);
            if (!linked.Ok) throw new Die($"can't read the conversation on #{n} ({FirstLine(read.Err + linked.Err)})");
            var found = Json.Parse(linked.Out)?["data"]?["repository"];
            foreach (var (issue, pr) in unlinked)
            {
                var nodes = new JsonArray();
                if (found?[$"p{pr}"] is { } node) nodes.Add((JsonNode?)node.DeepClone());
                repository[$"x{issue}"]!["closedByPullRequestsReferences"]!["nodes"] = nodes;
            }
        }
        repository.Remove("openPrs");
        return repository;
    }

    /// <summary>For each issue GitHub links no PR to, the first open PR whose body says it closes it.</summary>
    private static SortedDictionary<long, long> Unlinked(JsonObject repository)
    {
        var open = repository["openPrs"]?["nodes"]?.AsArray() ?? [];
        var unlinked = new SortedDictionary<long, long>();
        foreach (var (_, value) in repository)
        {
            if (value?["closedByPullRequestsReferences"]?["nodes"] is not JsonArray { Count: 0 }) continue;
            var number = Json.Long(value["number"]);
            foreach (var pr in open)
                if (Closes(pr).Contains(number))
                    unlinked.TryAdd(number, Json.Long(pr?["number"]));
        }
        return unlinked;
    }

    /// <summary>#n's history, newest first: what happened on it, its runs, and your time on it.</summary>
    internal JsonObject HistoryOf(string n)
    {
        var events = new JsonArray();
        foreach (var row in _record.Rows($"""
            SELECT at, who, what, 0 AS run, NULL AS ended, NULL AS cost, NULL AS outcome, id
                    FROM events WHERE team = {TeamQ} AND item = {n}
                  UNION ALL SELECT started, role, 'run', 1, ended, cost, outcome, runs.id
                    FROM runs JOIN run_items ON run_items.run = runs.id WHERE team = {TeamQ} AND item = {n}
                  UNION ALL SELECT at, 'you', what, 2, NULL, seconds, NULL, rowid FROM spent WHERE team = {TeamQ} AND item = {n}
                  ORDER BY at DESC, run DESC, id DESC;
            """))
        {
            var entry = new JsonObject { ["at"] = row!["at"]?.DeepClone(), ["who"] = row["who"]?.DeepClone(), ["what"] = row["what"]?.DeepClone() };
            var kind = Json.Long(row["run"]);
            if (kind == 1)
                entry["run"] = new JsonObject { ["ended"] = row["ended"]?.DeepClone(), ["cost"] = row["cost"]?.DeepClone(), ["outcome"] = row["outcome"]?.DeepClone() };
            else if (kind == 2)
                entry["spent"] = row["cost"]?.DeepClone();
            events.Add((JsonNode?)entry);
        }
        var since = _record.Scalar($"""
            SELECT MIN(at) FROM (SELECT at FROM events WHERE team = {TeamQ}
                  UNION ALL SELECT started FROM runs WHERE team = {TeamQ});
            """);
        return new JsonObject { ["since"] = string.IsNullOrEmpty(since) ? null : since, ["events"] = events };
    }

    internal JsonArray EventsSince(string since) =>
        _record.Rows($"SELECT item, at, who, what FROM events WHERE team = {TeamQ} AND at >= {Reconciler.Q(since)}\n  ORDER BY at, id;");

    /// <summary>What the team's runs that ended since local midnight cost, its whole-dollar daily budget or 0, and whether that's reached.</summary>
    internal JsonObject BudgetToday()
    {
        var spent = Recorded
            ? double.Parse(_record.Scalar($"""
                SELECT COALESCE(SUM(cost), 0) FROM runs WHERE team = {TeamQ}
                    AND ended >= strftime('%Y-%m-%dT%H:%M:%SZ', {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}, 'unixepoch', 'localtime', 'start of day', 'utc');
                """)!, CultureInfo.InvariantCulture)
            : 0;
        var cost = Math.Round(spent * 100, MidpointRounding.AwayFromZero) / 100;
        var set = _config["dispatch"]?["budget"];
        var whole = set?.GetValueKind() == System.Text.Json.JsonValueKind.Number && Math.Floor(set.GetValue<double>()) == set.GetValue<double>();
        var budget = whole ? set!.GetValue<double>() : 0;
        return new JsonObject
        {
            ["cost"] = cost,
            ["budget"] = whole ? set!.DeepClone() : 0,
            ["reached"] = budget > 0 && cost >= budget,
        };
    }

    internal JsonNode? TrendNow() => _record.Rows($"""
        SELECT
                (SELECT MIN(at) FROM (SELECT at FROM events WHERE team = {TeamQ} UNION ALL
                  SELECT started FROM caught_up WHERE team = {TeamQ} UNION ALL SELECT at FROM queue WHERE team = {TeamQ})) AS since,
                (SELECT waiting FROM queue WHERE team = {TeamQ} AND at <= {SqlNow("-7 days")} AND at > {SqlNow("-8 days")}
                  ORDER BY at DESC LIMIT 1) AS weekAgo,
                (SELECT COUNT(DISTINCT CASE WHEN what LIKE 'accepted · PR #%' THEN what ELSE item END) FROM events
                  WHERE team = {TeamQ} AND who = 'you' AND what LIKE 'accepted · %' AND at > {SqlNow("-7 days")}) AS accepted,
                (SELECT MIN(at) FROM spent WHERE team = {TeamQ}) AS spentSince,
                (SELECT COALESCE(SUM(seconds), 0) FROM spent WHERE team = {TeamQ} AND at > {SqlNow("-7 days")}) AS spent;
        """)[0];

    /// <summary>Everything the Trends view draws, over the last fortnight or so.</summary>
    internal JsonObject TrendsOver()
    {
        var row = _record.Rows($$"""
            SELECT
                    (SELECT MIN(at) FROM (SELECT at FROM events WHERE team = {{TeamQ}} UNION ALL
                      SELECT started FROM caught_up WHERE team = {{TeamQ}} UNION ALL SELECT at FROM queue WHERE team = {{TeamQ}})) AS since,
                    (SELECT json_group_array(json_object('at', at, 'waiting', waiting)) FROM (SELECT at, waiting FROM queue
                      WHERE team = {{TeamQ}} AND at > {{SqlNow("-15 days")}} ORDER BY at)) AS queue,
                    (SELECT json_group_array(at) FROM (SELECT MAX(at) AS at FROM events
                      WHERE team = {{TeamQ}} AND who = 'you' AND what LIKE 'accepted · %' AND at > {{SqlNow("-15 days")}}
                      GROUP BY CASE WHEN what LIKE 'accepted · PR #%' THEN what ELSE item END ORDER BY at)) AS accepted,
                    (SELECT json_group_array(json_object('item', item, 'at', at, 'who', who, 'what', what)) FROM (SELECT * FROM events
                      WHERE team = {{TeamQ}} AND item IN (SELECT item FROM events WHERE team = {{TeamQ}} AND who = 'you'
                        AND what LIKE 'accepted · %' AND at > {{SqlNow("-15 days")}}) ORDER BY at, id)) AS cycles,
                    (SELECT json_group_array(json_object('item', item, 'at', at, 'who', who, 'what', what)) FROM (SELECT * FROM events
                      WHERE team = {{TeamQ}} AND at > {{SqlNow("-45 days")}} ORDER BY at, id)) AS moves,
                    (SELECT ROUND(COALESCE(SUM(cost), 0), 2) FROM runs WHERE team = {{TeamQ}} AND started > {{SqlNow("-7 days")}}) AS cost,
                    (SELECT MIN(at) FROM spent WHERE team = {{TeamQ}}) AS spentSince,
                    (SELECT json_group_array(json_object('at', at, 'seconds', seconds, 'gate', gate)) FROM (SELECT at, seconds, gate
                      FROM spent WHERE team = {{TeamQ}} AND at > {{SqlNow("-15 days")}} ORDER BY at)) AS spent,
                    {{SqlNow("-15 days")}} AS fortnight,
                    (SELECT json_group_array(json_object('item', item, 'at', at, 'who', who, 'what', what)) FROM (SELECT * FROM events
                      WHERE team = {{TeamQ}} AND (who = 'you' AND (what = 'commented' OR what LIKE 'accepted · %')
                        OR what LIKE 'closed as done%' OR what LIKE '% → %' OR what LIKE 'added as %') ORDER BY at, id)) AS said,
                    (SELECT json_group_array(json_object('item', item, 'parent', json_extract(content, '$.parent'))) FROM board
                      WHERE team = {{TeamQ}} AND json_extract(content, '$.type') = 'Issue'
                        AND (json_extract(content, '$.parent') IS NOT NULL OR json_extract(marks, '$.dev'))) AS tasks;
            """)[0]!;
        var worktrees = _config["wip"]?["worktrees"];
        var waits = Trends.Waits(Trends.Events(row["moves"]), worktrees?.GetValueKind() == System.Text.Json.JsonValueKind.Number ? worktrees.GetValue<double>() : null);
        var tasks = Json.Parse(Json.Text(row["tasks"])!)!.AsArray();
        var said = Trends.Events(row["said"]);
        var fortnight = Json.Text(row["fortnight"])!;
        return new JsonObject
        {
            ["since"] = row["since"]?.DeepClone(),
            ["queue"] = Json.Parse(Json.Text(row["queue"])!),
            ["accepted"] = Json.Parse(Json.Text(row["accepted"])!),
            ["cycles"] = Trends.Cycles(Trends.Events(row["cycles"]), waits),
            ["cost"] = row["cost"]?.DeepClone(),
            ["spentSince"] = row["spentSince"]?.DeepClone(),
            ["spent"] = Json.Parse(Json.Text(row["spent"])!),
            ["touches"] = Trends.Touches(said, tasks.Select(task => Json.Long(task?["item"])).ToHashSet(), fortnight),
            ["finished"] = Trends.Finished(said, tasks, fortnight),
        };
    }

    /// <summary>This team's runs, oldest first, with what each cost and ran on.</summary>
    internal JsonArray RunsWithItems()
    {
        var runs = _record.Rows($"""
            SELECT started, role, COALESCE(cost, 0) AS cost,
                    (SELECT json_group_array(item) FROM run_items WHERE run = runs.id) AS items
                  FROM runs WHERE team = {TeamQ} ORDER BY started, id;
            """);
        foreach (var run in runs)
            run!["items"] = Json.Parse(Json.Text(run["items"])!);
        return runs;
    }

    /// <summary>The register's entries for this team and all teams matching every word, only the standing ones where <paramref name="live"/>.</summary>
    internal JsonArray DecisionsMatching(string[] words, bool live)
    {
        var match = string.Join(" ", words.Select(word => $"\"{word.Replace("\"", "\"\"")}\"*"));
        try
        {
            _record.Exec(RegisterSchema);
            return _record.Rows($"""
                SELECT {RegisterColumns} FROM decisions WHERE (team = {TeamQ} OR everyone)
                      {(live ? "AND replaced_by IS NULL AND retired IS NULL" : "")}
                      {(match.Length > 0 ? $"AND id IN (SELECT rowid FROM decisions_text WHERE decisions_text MATCH {Reconciler.Q(match)})" : "")}
                      ORDER BY confirmed DESC, id DESC;
                """);
        }
        catch (InvalidOperationException problem)
        {
            Console.Out.Write("[]\n");
            throw new Stop(1, $"Error: {problem.Message}\n");
        }
    }

    /// <summary>The CI verdict for a PR's head commit. A fix's own PR never reads `main-red`, or nothing would wake the Dev to finish it.</summary>
    internal JsonObject Ci(string pr)
    {
        var head = _gh.Run("api", $"repos/{_repo}/pulls/{pr}", "--jq", ".head.sha");
        Console.Error.Write(head.Err);
        if (!head.Ok) throw new Die($"could not read PR #{pr}");
        JsonObject checks;
        try
        {
            checks = PrChecks(head.Said);
        }
        catch (Stop stopped)
        {
            throw new Stop(1, stopped.Message);
        }
        if (Json.Text(checks["verdict"]) != "fail") return checks;
        var main = MainRed();
        if (main is not null && IsFixPr(pr)) main = null;
        checks["verdict"] = AgainstMain(checks, main);
        return checks;
    }

    private bool IsFixPr(string pr)
    {
        foreach (var card in AllCards().Where(card => Cards.Fix(card) && card["closed"] is null))
        {
            var (read, found) = PrFor(Cards.N(card));
            Console.Error.Write(read.Err);
            if (found?["number"]?.ToJsonString() == pr) return true;
        }
        return false;
    }
}

/// <summary>A task's acceptance criteria, and the team's record of them on its PR.</summary>
internal static class Checklist
{
    private static string Norm(string text)
    {
        text = Regex.Replace(Regex.Replace(text, @"[\[\]]", ""), @"\s+", " ");
        if (text.StartsWith(' ')) text = text[1..];
        return text.EndsWith(' ') ? text[..^1] : text;
    }

    /// <summary>The lines under a heading in a PR's body.</summary>
    private static string[] Section(string pr, string heading)
    {
        var lines = pr.Replace("\r", "").Split('\n');
        var at = Array.FindIndex(lines, line => Regex.Replace(line, @"\s+$", "") == heading);
        if (at < 0) return [];
        var rest = lines[(at + 1)..];
        var end = Array.FindIndex(rest, line => Regex.IsMatch(line, "^#{1,2} |^a-team try |^<!-- a-team:"));
        return end >= 0 ? rest[..end] : rest;
    }

    /// <summary>An issue body's checklist: each item's line, depth, box and text, a wrapped line joined to its item.</summary>
    private static List<JsonObject> Items(string body)
    {
        var items = new List<JsonObject>();
        var inside = false;
        var lines = body.Replace("\r", "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (Regex.IsMatch(line, @"^#+\s+[Aa]cceptance [Cc]riteria\s*$"))
                inside = true;
            else if (!inside || Regex.IsMatch(line, @"^\s*$"))
                continue;
            else if (Regex.IsMatch(line, @"^#+\s"))
                break;
            else if (Regex.IsMatch(line, @"^\s*[-*+]\s"))
                items.Add(Item(line, i));
            else if (items.Count > 0)
                items[^1]["text"] = $"{Json.Text(items[^1]["text"])} {Regex.Replace(line, @"^\s+", "")}";
        }
        return items;
    }

    private static JsonObject Item(string line, int at)
    {
        var parts = Regex.Match(line, @"^(?<lead>\s*)[-*+]\s+(\[(?<box>[ xX])\]\s*)?(?<text>.*)$");
        return new JsonObject
        {
            ["line"] = at,
            ["depth"] = parts.Groups["lead"].Length == 0 ? 0 : 1,
            ["ticked"] = parts.Groups["box"].Success && parts.Groups["box"].Value != " ",
            ["text"] = parts.Groups["text"].Value,
        };
    }

    /// <summary>The checklist with how the team checked each, and its picture.</summary>
    internal static JsonArray Read(string issue, string pr)
    {
        var how = new Dictionary<string, string>();
        foreach (var line in Section(pr, "## Checked by the team"))
            if (Regex.Match(line, "^- `(?<how>screen|test)` (?<text>.*)$") is { Success: true } checkedBy)
                how[Norm(checkedBy.Groups["text"].Value)] = checkedBy.Groups["how"].Value;
        var shots = new Dictionary<string, (string Alt, string Url)>();
        foreach (var line in Section(pr, "## What it looks like"))
            if (Regex.Match(line, @"^!\[(?<alt>[^\]]*)\]\((?<url>\S+?)\)\s*$") is { Success: true } shot)
                shots[Norm(shot.Groups["alt"].Value)] = (shot.Groups["alt"].Value, shot.Groups["url"].Value);
        var criteria = new JsonArray();
        var index = 0;
        foreach (var item in Items(issue))
        {
            var said = Norm(Json.Text(item["text"])!);
            var checkedHow = how.GetValueOrDefault(said);
            item["index"] = ++index;
            item["how"] = checkedHow;
            item["picture"] = checkedHow == "screen" && shots.TryGetValue(said, out var picture)
                ? new JsonObject { ["alt"] = picture.Alt, ["url"] = picture.Url }
                : null;
            criteria.Add((JsonNode?)item);
        }
        return criteria;
    }
}

/// <summary>One line of a team's history, as the trends read it.</summary>
internal readonly record struct Happening(long Item, string At, string Who, string What);

/// <summary>A stretch of time: which Ready tasks await an answer, which are free to start, and whether the Dev's slots are full with one In review.</summary>
internal sealed record WaitSpan(long From, long To, HashSet<string> Asked, HashSet<string> Free, bool Full);

/// <summary>How long work waits on you, read off the record's history.</summary>
internal static class Trends
{
    private sealed class Tracked
    {
        internal string? Status { get; set; }

        internal bool Dev { get; set; }

        internal bool Asked { get; set; }

        internal List<string> Deps { get; } = [];
    }

    private static bool IsTask(string? status) => status is "Ready" or "In progress" or "In review";

    private static long Seconds(string at) =>
        DateTimeOffset.ParseExact(at, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUnixTimeSeconds();

    private static bool Later(string at, string than) => string.CompareOrdinal(at, than) > 0;

    internal static List<Happening> Events(JsonNode? json) =>
        Json.Parse(Json.Text(json)!)!.AsArray()
            .Select(e => new Happening(Json.Long(e!["item"]), Json.Text(e["at"])!, Json.Text(e["who"])!, Json.Text(e["what"])!)).ToList();

    /// <summary>The Status a line of history moved its item from and to, if it moved it.</summary>
    internal static (string? From, string? To)? Move(string what)
    {
        if (what.StartsWith("added as ", StringComparison.Ordinal)) return (null, what["added as ".Length..]);
        if (what.StartsWith("moved to ", StringComparison.Ordinal) || !what.Contains(" → ", StringComparison.Ordinal)) return null;
        var parts = what.Split(" · ")[0].Split(" → ");
        return (parts[0], parts.Length > 1 ? parts[1] : null);
    }

    private static WaitSpan Span(Dictionary<string, Tracked> items, string from, string to, double? worktrees)
    {
        var held = items.Values.Where(item => item.Dev && item.Status is "In progress" or "In review" && item.Deps.Count == 0).ToList();
        HashSet<string> Ready(Func<Tracked, bool> which) =>
            items.Where(item => item.Value.Status == "Ready" && which(item.Value)).Select(item => item.Key).ToHashSet();
        return new WaitSpan(Seconds(from), Seconds(to), Ready(item => item.Asked), Ready(item => !item.Asked && item.Deps.Count == 0),
            worktrees is { } slots && held.Count >= slots && held.Any(item => item.Status == "In review"));
    }

    /// <summary>A team's events, oldest first, as spans.</summary>
    internal static List<WaitSpan> Waits(IEnumerable<Happening> events, double? worktrees)
    {
        var items = new Dictionary<string, Tracked>();
        var spans = new List<WaitSpan>();
        string? last = null;
        foreach (var e in events)
        {
            if (last is not null && Later(e.At, last))
                spans.Add(Span(items, last, e.At, worktrees));
            last = e.At;
            var n = e.Item.ToString(CultureInfo.InvariantCulture);
            if (!items.TryGetValue(n, out var item))
                items[n] = item = new Tracked();
            Follow(items, item, n, e);
        }
        return spans;
    }

    private static string Number(string digits) => long.Parse(digits, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    private static void Follow(Dictionary<string, Tracked> items, Tracked item, string n, Happening e)
    {
        if (Move(e.What) is { } move)
        {
            item.Status = move.To;
            item.Dev = move.To is "In progress" or "In review" && (item.Dev || (e.Who == "dev" && move.From == "Ready" && move.To == "In progress"));
            item.Asked = item.Asked && move.To == "Ready";
        }
        else if (Regex.IsMatch(e.What, "^blocked by #[0-9]+$"))
            item.Deps.Add(Number(e.What["blocked by #".Length..]));
        else if (Regex.IsMatch(e.What, "^blocked by [^ #]+#[0-9]+$"))
            item.Deps.Add(e.What["blocked by ".Length..]);
        else if (Regex.IsMatch(e.What, "^no longer blocked by #[0-9]+$"))
            item.Deps.RemoveAll(dep => dep == Number(e.What["no longer blocked by #".Length..]));
        else if (Regex.IsMatch(e.What, "^no longer blocked by [^ #]+#[0-9]+$"))
            item.Deps.RemoveAll(dep => dep == e.What["no longer blocked by ".Length..]);
        else if (Regex.IsMatch(e.What, "^(accepted · |closed|declined)"))
        {
            item.Status = "Done";
            item.Dev = false;
            foreach (var other in items.Values)
                other.Deps.RemoveAll(dep => dep == n);
        }
        else if (e.What == "handed back with a question")
            item.Asked = true;
        else if (e.What == "question answered")
            item.Asked = false;
    }

    /// <summary>Of one item's events, its span from first entering Ready to its last acceptance and its seconds with you; null if it never entered Ready on record.</summary>
    internal static JsonObject? Cycle(IReadOnlyList<Happening> events, IReadOnlyList<WaitSpan> waits)
    {
        var accepted = events.LastOrDefault(e => e.Who == "you" && e.What.StartsWith("accepted · ", StringComparison.Ordinal)).At;
        if (accepted is null) return null;
        var moves = events.Where(e => !Later(e.At, accepted) && Move(e.What) is not null).Select(e => (Move: Move(e.What)!.Value, e.At)).ToList();
        var first = moves.FirstOrDefault(m => IsTask(m.Move.From) || IsTask(m.Move.To));
        if (first.At is null || first.Move.To != "Ready" || IsTask(first.Move.From)) return null;
        var review = 0L;
        string? since = null;
        foreach (var m in moves.Where(m => !Later(first.At, m.At)))
        {
            if (m.Move.To == "In review")
            {
                since ??= m.At;
            }
            else if (m.Move.From == "In review" && since is not null)
            {
                review += Seconds(m.At) - Seconds(since);
                since = null;
            }
        }
        var (from, to) = (Seconds(first.At), Seconds(accepted));
        var n = events[0].Item.ToString(CultureInfo.InvariantCulture);
        var waited = waits.Where(wait => wait.To > from && wait.From < to && (wait.Asked.Contains(n) || (wait.Full && wait.Free.Contains(n))))
            .Sum(wait => Math.Min(wait.To, to) - Math.Max(wait.From, from));
        return new JsonObject
        {
            ["ready"] = first.At,
            ["accepted"] = accepted,
            ["withYou"] = review + waited + (since is null ? 0 : to - Seconds(since)),
        };
    }

    internal static JsonArray Cycles(IEnumerable<Happening> events, IReadOnlyList<WaitSpan> waits)
    {
        var cycles = new JsonArray();
        foreach (var cycle in events.GroupBy(e => e.Item).OrderBy(group => group.Key).Select(group => Cycle(group.ToList(), waits)).OfType<JsonObject>()
            .OrderBy(cycle => Json.Text(cycle["accepted"]), StringComparer.Ordinal))
            cycles.Add((JsonNode?)cycle);
        return cycles;
    }

    private static bool Said(Happening e) => e.Who == "you" && e.What == "commented";

    private static IEnumerable<long> Merged(string what)
    {
        var match = Regex.Match(what, "^accepted · PR #(?<pr>[0-9]+) ");
        return match.Success ? [long.Parse(match.Groups["pr"].Value, CultureInfo.InvariantCulture)] : [];
    }

    private static IEnumerable<Happening> On(IEnumerable<long> numbers, ILookup<long, Happening> by) => numbers.SelectMany(n => by[n]);

    /// <summary>Per task in <paramref name="work"/> accepted since then, your comments on it or its PR once it reached review.</summary>
    internal static JsonArray Touches(List<Happening> said, HashSet<long> work, string since)
    {
        var by = said.ToLookup(e => e.Item);
        var touches = new JsonArray();
        foreach (var task in said.Where(e => e.Who == "you" && e.What.StartsWith("accepted · ", StringComparison.Ordinal) && Later(e.At, since) && work.Contains(e.Item))
            .GroupBy(e => e.Item).OrderBy(group => group.Key)
            .Select(group =>
            {
                var numbers = group.SelectMany(e => Merged(e.What)).Prepend(group.Key).Distinct().Order().ToList();
                var events = On(numbers, by).ToList();
                var review = events.Where(e => Move(e.What)?.To == "In review").Select(e => e.At).Order(StringComparer.Ordinal).FirstOrDefault() ?? "";
                return new JsonObject
                {
                    ["accepted"] = group.Select(e => e.At).Order(StringComparer.Ordinal).Last(),
                    ["touches"] = events.Count(e => Said(e) && !Later(review, e.At)),
                };
            }).OrderBy(task => Json.Text(task["accepted"]), StringComparer.Ordinal))
            touches.Add((JsonNode?)task);
        return touches;
    }

    /// <summary>Per pitch finished since then, whether you approved it with nothing to say and never commented on its tasks or their PRs.</summary>
    internal static JsonArray Finished(List<Happening> said, JsonArray tasks, string since)
    {
        var by = said.ToLookup(e => e.Item);
        var under = tasks.Where(task => Json.Truthy(task?["parent"])).ToLookup(task => Json.Long(task!["parent"]), task => Json.Long(task!["item"]));
        var finished = new JsonArray();
        foreach (var pitch in said.Where(e => e.What.StartsWith("closed as done", StringComparison.Ordinal) && Later(e.At, since))
            .GroupBy(e => e.Item).OrderBy(group => group.Key)
            .Select(group => Pitch(group.Last(), by, under))
            .OrderBy(pitch => Json.Text(pitch["at"]), StringComparer.Ordinal))
            finished.Add((JsonNode?)pitch);
        return finished;
    }

    private static JsonObject Pitch(Happening closed, ILookup<long, Happening> by, ILookup<long, long> under)
    {
        var (pitched, approved, spoke) = (false, false, false);
        foreach (var e in by[closed.Item])
        {
            if (pitched && Said(e)) spoke = true;
            if (Move(e.What) is not { } move) continue;
            pitched = move.To == "Pitched";
            approved = approved || (move.From == "Pitched" && move.To == "Approved");
        }
        var own = under[closed.Item].ToList();
        var items = own.Concat(On(own, by).SelectMany(e => Merged(e.What)))
            .Concat(Regex.Matches(closed.What, "#[0-9]+").Select(match => long.Parse(match.Value[1..], CultureInfo.InvariantCulture)));
        return new JsonObject
        {
            ["at"] = closed.At,
            ["untouched"] = approved && !spoke && !On(items, by).Any(Said),
        };
    }
}
