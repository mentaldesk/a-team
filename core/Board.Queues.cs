using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ATeam.Core;

/// <summary>What the queues ask of the record and GitHub: whose turn each card is, the detail that's read from, and what wakes a role.</summary>
internal sealed partial class Board
{
    private const string NoTalk = """{"data": {"repository": {}}}""";
    private const string AnyRole = "<!-- a-team:";
    internal const string DispatchMarker = "<!-- a-team:dispatch -->";
    private const string RunsOnHead = """
        checkSuites(first: 50) { nodes {
            checkRuns(first: 100, filterBy: {checkType: LATEST}) { nodes { name status conclusion completedAt url } } } } } } }
        """;
    private const string PrHeadRuns = $"commits(last: 1) {{ nodes {{ commit {{ {RunsOnHead}";
    private const string MainFields = $"defaultBranchRef {{ name target {{ ... on Commit {{ oid {RunsOnHead}";
    private const string CatchUpFragments = """

          fragment IssueEvents on Issue { number timelineItems(since: $since, first: 100,
              itemTypes: [ISSUE_COMMENT, CLOSED_EVENT]) {
            nodes { __typename ...Commented ...Closed } } }
          fragment PullEvents on PullRequest { number merged timelineItems(since: $since, first: 100,
              itemTypes: [ISSUE_COMMENT, CLOSED_EVENT, MERGED_EVENT]) {
            nodes { __typename ...Commented ...Closed ...Merged } } }
          fragment Who on Actor { __typename login }
          fragment Commented on IssueComment { id createdAt author { ...Who } }
          fragment Closed on ClosedEvent { id createdAt stateReason actor { ...Who }
            closer { __typename ... on PullRequest { number } } }
          fragment Merged on MergedEvent { id createdAt actor { ...Who } }
        """;

    /// <summary>A read behind what holds other work failed in the way that left the shell with two answers, and so with no Ideas in order.</summary>
    private bool _holdingLost;

    private static readonly string[] FailedConclusions = ["FAILURE", "TIMED_OUT", "CANCELLED", "ACTION_REQUIRED", "STARTUP_FAILURE"];

    /// <summary>The board's open items, as the record has them.</summary>
    internal List<JsonNode> OpenCards() => Items(open: true).OfType<JsonNode>().ToList();

    /// <summary>What `jq -r` or a string's `\(...)` makes of a value.</summary>
    internal static string Raw(JsonNode? node) => Json.Text(node) ?? (node is null ? "null" : Json.Compact(node));

    /// <summary>What @tsv does to a field, which `read -r` keeps.</summary>
    internal static string Tsv(string field) =>
        field.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");

    internal static long Number(JsonNode card) => Json.Long(card["number"]);

    private string Kept(params string[] under) => Path.Combine([State, _team, .. under]);

    private static void Forget(string file)
    {
        if (File.Exists(file)) File.Delete(file);
    }

    private static void Touch(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (File.Exists(file)) File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
        else File.Create(file).Dispose();
    }

    private static void Keep(string file, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    private static string ADayAgo() => Iso(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 86400);

    private static string LocalToday() => Libc.Local(DateTimeOffset.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The newest of those, the last of any tied, as `max_by(.at)` picks.</summary>
    private static JsonObject? LatestOf(IEnumerable<JsonObject> comments)
    {
        JsonObject? latest = null;
        foreach (var comment in comments)
            if (latest is null || string.CompareOrdinal(Json.Text(comment["at"]), Json.Text(latest["at"])) >= 0)
                latest = comment;
        return latest;
    }

    /// <summary>The oldest of those, the first of any tied, as `min_by(.at)` picks.</summary>
    private static JsonObject? EarliestOf(IEnumerable<JsonObject> comments)
    {
        JsonObject? earliest = null;
        foreach (var comment in comments)
            if (earliest is null || string.CompareOrdinal(Json.Text(comment["at"]), Json.Text(earliest["at"])) < 0)
                earliest = comment;
        return earliest;
    }

    /// <summary>" since <when>", the time alone if it was today; nothing for no time.</summary>
    internal static string Since(string at)
    {
        if (at.Length == 0) return "";
        var local = Libc.Local(DateTimeOffset.FromUnixTimeSeconds(Epoch(at)));
        var format = local.Date == Libc.Local(DateTimeOffset.UtcNow).Date ? "HH:mm" : "dd MMM HH:mm";
        return $" since {local.ToString(format, CultureInfo.InvariantCulture)}";
    }

    /// <summary>The Priority of the pitch a task follows, and whether its own Priority is that one, where it is.</summary>
    internal static void PitchRank(JsonNode card, JsonObject shown)
    {
        if (card["pitchPriority"] is { } pitch) shown["pitchPriority"] = pitch.DeepClone();
        if (Json.Truthy(card["inherited"])) shown["inherited"] = true;
    }

    /// <summary>What an item's marks say, as the app shows it under the title; "" for none. A shelve shows while it's cooling.</summary>
    internal static string Facts(JsonNode card)
    {
        if (card["marks"] is not JsonObject marks) return "";
        var facts = new List<string>();
        if (Json.Truthy(marks["rank"])) facts.Add($"Lead recommends {Raw(marks["rank"])}");
        if (Json.Truthy(marks["theme"])) facts.Add($"theme: {Raw(marks["theme"])}");
        if (Cooling(card)) facts.Add($"shelved until {ShelvedDay(card)}");
        if (Json.Truthy(marks["skipped"])) facts.Add("skipped");
        if (Json.Truthy(marks["held"])) facts.Add("held");
        if (Json.Truthy(marks["secret"])) facts.Add($"needs {Raw(marks["secret"]?["name"])}");
        if (marks["upstream"] is JsonArray { Count: > 0 } refs) facts.Add($"waiting on {string.Join(", ", refs.Select(Raw))}");
        else if (Json.Truthy(marks["upstream"])) facts.Add("waiting on another repo");
        return string.Join(" · ", facts);
    }

    private static string ShelvedDay(JsonNode card) =>
        DateTime.ParseExact(Json.Text(card["marks"]?["shelved"]?["until"])!, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .ToString("dd MMM", CultureInfo.InvariantCulture).TrimStart('0');

    /// <summary>Inside the cooldown a Not now gave it, at the Priority it had then.</summary>
    internal static bool Cooling(JsonNode card)
    {
        if (card["marks"]?["shelved"] is not JsonObject shelved) return false;
        return string.CompareOrdinal(Json.Text(shelved["until"]) ?? "", LocalToday()) > 0
            && Json.Text(shelved["priority"]) == Json.Text(card["priority"]);
    }

    /// <summary>How far behind the mirror is and why, as {behind, failed, retry}; null while it's caught up.</summary>
    internal JsonNode? MirrorLag()
    {
        if (_mirror.Length == 0) return null;
        var row = _record.Rows($"SELECT (SELECT COUNT(*) FROM board WHERE team = {TeamQ} AND node != '' AND {Lagging}) AS behind,\n      failed, retry FROM boards WHERE team = {TeamQ};").FirstOrDefault();
        return row is not null && Json.Long(row["behind"]) > 0 ? row.DeepClone() : null;
    }

    /// <summary>Each card's newest run, by the card's number.</summary>
    internal Dictionary<long, JsonNode> LastRuns()
    {
        var last = new Dictionary<long, JsonNode>();
        if (!Recorded) return last;
        foreach (var run in _record.Rows($"""
            SELECT item, role, log, started FROM (SELECT item, role, log,
                    started, ROW_NUMBER() OVER (PARTITION BY item ORDER BY started DESC, runs.id DESC) AS n
                    FROM runs JOIN run_items ON run_items.run = runs.id WHERE team = {TeamQ}) WHERE n = 1;
            """))
            last[Json.Long(run!["item"])] = run!;
        return last;
    }

    /// <summary>The role's last <paramref name="count"/> runs on #n that got as far as finishing, oldest first.</summary>
    internal List<JsonNode> FinishedRuns(string role, string n, int count)
    {
        if (!Recorded) return [];
        return _record.Rows($"""
            SELECT started, cost, log FROM runs JOIN run_items ON run_items.run = runs.id
                WHERE team = {TeamQ} AND role = {Reconciler.Q(role)} AND item = {n} AND ended IS NOT NULL
                AND outcome IS NULL ORDER BY runs.id DESC LIMIT {count};
            """).OfType<JsonNode>().Reverse().ToList();
    }

    /// <summary>Pitches with no theme and no `none` from the Lead: open ones, and done ones since runs were recorded.</summary>
    internal JsonArray Unthemed(List<JsonNode> all)
    {
        var recent = new HashSet<long>();
        var none = new HashSet<long>();
        if (Recorded)
        {
            foreach (var row in _record.Query($"SELECT DISTINCT item FROM events WHERE team = {TeamQ}\n      AND at >= (SELECT MIN(started) FROM runs WHERE team = {TeamQ});"))
                recent.Add(long.Parse(row.First!, CultureInfo.InvariantCulture));
            foreach (var row in _record.Query($"SELECT item FROM events e WHERE team = {TeamQ} AND what = 'no theme'\n      AND id = (SELECT MAX(id) FROM events WHERE team = e.team AND item = e.item AND what LIKE '%theme%');"))
                none.Add(long.Parse(row.First!, CultureInfo.InvariantCulture));
        }
        return Json.ArrayOf(all.Where(card => Cards.IsIssue(card) && Cards.Pitch(card) && card["marks"]?["theme"] is null
                && Json.Text(card["closed"]) != "NOT_PLANNED" && (Cards.Status(card) != "Done" || recent.Contains(Number(card)))
                && !none.Contains(Number(card)))
            .Select(card => card["number"]?.DeepClone()));
    }

    internal bool Seeded => Path.Exists(Kept("lead", "seeded"));

    /// <summary>The Idea the Lead should pitch next, with `actedOn` its recommendation where that put it ahead of the next pitchable Idea in its Priority band.</summary>
    internal JsonObject? PitchableIdea(List<JsonNode> all)
    {
        var ideas = Cards.ByPriority(all.Where(card => Cards.Status(card) == "Idea" && Cards.IsIssue(card)));
        if (ideas.Count > 0)
        {
            _holdingLost = false;
            var holding = Holding(all);
            if (_holdingLost)
            {
                Console.Error.WriteLine("jq: Invalid JSON text passed to --argjson");
                return null;
            }
            ideas = ideas.OrderBy(card => !holding.Contains(Number(card))).ToList();
        }
        JsonObject? chosen = null;
        foreach (var candidate in ideas.Where(Pitchable))
        {
            if (chosen is null)
            {
                chosen = candidate.DeepClone().AsObject();
                if (Json.Truthy(chosen["marks"]?["shelved"]?["until"]))
                    chosen["shelved"] = chosen["marks"]?["shelved"]?["until"]?.DeepClone();
                if (!Json.Truthy(chosen["marks"]?["rank"])) break;
                continue;
            }
            if (Json.Text(chosen["priority"]) == Json.Text(candidate["priority"])
                && Json.Text(chosen["marks"]?["rank"]) != Json.Text(candidate["marks"]?["rank"]))
                chosen["actedOn"] = chosen["marks"]?["rank"]?.DeepClone();
            break;
        }
        return chosen;
    }

    /// <summary>Whether a failed read's stdout was an error body, which reading a list of comments stops at, so its fallback stands alone.</summary>
    private static bool SaidOnlyAnError(GhResult read)
    {
        try
        {
            return Json.ParseMany(read.Out).Any(page => page is not JsonArray);
        }
        catch (System.Text.Json.JsonException)
        {
            return true;
        }
    }

    /// <summary>A stakeholder's Idea or one they prioritised, not skipped by the Lead since they last spoke, and not cooling after a Not now.
    /// One opened by anyone else waits for a Priority.</summary>
    private bool Pitchable(JsonNode idea)
    {
        var n = Cards.N(idea);
        if (Cards.Marked(idea, "skipped") && OwedAnyway("lead", n) is { Count: 0 }) return false;
        if (Cooling(idea)) return false;
        return idea["priority"] is not null || !(Cards.Marked(idea, "idea") || Opener(idea) is not null || AgentWrote(n));
    }

    private bool AgentWrote(string n)
    {
        var read = Aloud("api", $"repos/{_repo}/issues/{n}", "--jq", ".body");
        return read.Ok && Regex.IsMatch(read.Out, "<!-- a-team:(lead|dev|customer|reviewer) -->");
    }

    private static List<JsonNode?> Pages(GhResult read)
    {
        try
        {
            return Json.ParseMany(read.Out).SelectMany(page => page is JsonArray list ? list.ToList() : []).ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private static List<JsonObject> Documents(GhResult read)
    {
        try
        {
            return Json.ParseMany(read.Out).OfType<JsonObject>().ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private GhResult ReviewsRead(string n)
    {
        var (owner, name) = RepoParts;
        return Aloud("api", "graphql", "-F", $"owner={owner}", "-F", $"name={name}", "-F", $"number={n}", "-f", """
            query=
                query($owner: String!, $name: String!, $number: Int!) {
                  repository(owner: $owner, name: $name) { pullRequest(number: $number) {
                    reviews(last: 50) { nodes { id url state body submittedAt author { __typename login }
                      reactions(content: EYES) { totalCount } } } } } }
            """, "--jq", $$"""
            .data.repository.pullRequest.reviews.nodes[]
                      | select((.body // "") != "" or .state == "CHANGES_REQUESTED")
                      | {kind: "review", state, author: (.author | {{Login}}), at: .submittedAt,
                         body: (.body // ""), url, id, eyes: .reactions.totalCount}
            """);
    }

    /// <summary>All that can be read of what's been said on #n: a read GitHub refuses leaves out what it would have said, and the rest are still asked.</summary>
    private List<JsonObject> CommentsAnyway(string n)
    {
        var read = Aloud("api", $"repos/{_repo}/issues/{n}");
        var issue = Json.TryParse(read.Out);
        var all = new List<JsonObject>();
        if (issue is JsonObject) all.Add(Comment("body", issue));
        all.AddRange(Pages(Aloud("api", "--paginate", $"repos/{_repo}/issues/{n}/comments")).Select(c => Comment("comment", c)));
        if (issue is JsonObject && Json.Truthy(issue["pull_request"]))
        {
            all.AddRange(Documents(ReviewsRead(n)));
            foreach (var line in Pages(Aloud("api", "--paginate", $"repos/{_repo}/pulls/{n}/comments")))
            {
                var comment = Comment("line", line);
                comment["path"] = line?["path"]?.DeepClone();
                comment["line"] = line?["line"]?.DeepClone();
                all.Add(comment);
            }
        }
        return all.OrderBy(comment => Json.Text(comment["at"]), StringComparer.Ordinal).ToList();
    }

    /// <summary>What `unanswered_feedback` gave a read it was never stopped by: the stakeholders' unanswered comments on #n, with their attachments fetched.</summary>
    private List<JsonObject>? OwedAnyway(string role, string n)
    {
        var said = CommentsAnyway(n);
        if (Unreadable(said, $"<!-- a-team:{role} -->")) return null;
        var left = Unanswered(role, said);
        WithImages(left);
        return left;
    }

    /// <summary>Whether jq stopped on those comments, as an error body among them made it, so that nothing was owed on them.</summary>
    private bool Unreadable(List<JsonObject> said, string marker)
    {
        var stopped = said.FirstOrDefault(c => !TeamSaid(c, marker) && string.CompareOrdinal(Json.Text(c["at"]), AckFrom) < 0
            && c["body"]?.GetValueKind() != System.Text.Json.JsonValueKind.String);
        if (stopped is null) return false;
        var body = stopped["body"];
        var shown = body is null ? "null (null)" : $"{body.GetValueKind().ToString().ToLowerInvariant()} ({Json.Compact(body)})";
        Console.Error.WriteLine($"jq: error (at <stdin>:0): {shown} and string (\"{marker}\") cannot have their containment checked");
        return true;
    }

    /// <summary>The conversation and line comments across the whole repo from the last day, and the exit code of the second read, which is the one that stops a command.</summary>
    internal (List<JsonObject> Comments, int Code) RecentComments()
    {
        var since = ADayAgo();
        var issues = Aloud("api", "--paginate", $"repos/{_repo}/issues/comments?since={since}&per_page=100",
            "--jq", """.[] | {n: (.issue_url | split("/") | last | tonumber), author: .user.login, at: .created_at, body: (.body // ""), eyes: .reactions.eyes}""");
        var pulls = Aloud("api", "--paginate", $"repos/{_repo}/pulls/comments?since={since}&per_page=100",
            "--jq", """.[] | {n: (.pull_request_url | split("/") | last | tonumber), author: .user.login, at: .created_at, body: (.body // ""), eyes: .reactions.eyes}""");
        return ([.. Documents(issues), .. Documents(pulls)], pulls.Code);
    }

    /// <summary>The PR's review summaries, each saying which PR, as far as GitHub would say.</summary>
    internal List<JsonObject> PrReviews(string n)
    {
        var reviews = Documents(ReviewsRead(n));
        foreach (var review in reviews)
            review["n"] = long.Parse(n, CultureInfo.InvariantCulture);
        return reviews;
    }

    private string Awaiting(List<JsonObject> recent, string role, string n)
    {
        var number = long.Parse(n, CultureInfo.InvariantCulture);
        var theirs = recent.Where(c => Json.Long(c["n"]) == number).ToList();
        return Unreadable(theirs, $"<!-- a-team:{role} -->") ? "" : Newest(Unanswered(role, theirs)) ?? "";
    }

    /// <summary>When a stakeholder last asked that role something on #n and got no answer. The day window keeps that cheap; a sweep falls back to #n's whole history, however old.</summary>
    internal string FeedbackAt(List<JsonObject> recent, string role, string n, bool sweep)
    {
        var at = Awaiting(recent, role, n);
        return at.Length > 0 || !sweep ? at : Newest(OwedAnyway(role, n) ?? []) ?? "";
    }

    /// <summary>A Dependabot PR the Dev has, as `PrFor` reports a task's. It closes no issue, and no Reviewer reads it.</summary>
    internal JsonObject? BumpView(string n)
    {
        var view = Must(_gh.Run("pr", "view", n, "-R", _repo, "--json", "number,url,isDraft,headRefName,headRefOid,mergeable,mergeStateStatus"));
        if (Json.ParseMany(view.Out).OfType<JsonObject>().FirstOrDefault() is not { } pr) return null;
        pr["review"] = "off";
        return pr;
    }

    /// <summary>A PR's CI verdict, as a command that stops where it can't read one has it.</summary>
    internal JsonObject HeadChecks(string pr)
    {
        var head = Aloud("api", $"repos/{_repo}/pulls/{pr}", "--jq", ".head.sha");
        if (!head.Ok) throw new Die($"could not read PR #{pr}");
        return PrChecks(head.Said);
    }

    /// <summary>A PR's CI verdict on its head: nothing if its head can't be read, which it says, and otherwise what came back of its runs.</summary>
    internal string HeadVerdict(string pr)
    {
        var head = Aloud("api", $"repos/{_repo}/pulls/{pr}", "--jq", ".head.sha");
        if (!head.Ok)
        {
            Console.Error.WriteLine($"board.sh: could not read PR #{pr}");
            return "";
        }
        var runs = Aloud("api", "--paginate", $"repos/{_repo}/commits/{head.Said}/check-runs?per_page=100", "--jq", ".check_runs[]");
        return Json.Text(Checks(Documents(runs).Cast<JsonNode?>().ToList())["verdict"]) ?? "";
    }

    /// <summary>The first seven characters of the PR's head, whatever came back.</summary>
    internal string ShortHead(string pr) => Aloud("api", $"repos/{_repo}/pulls/{pr}", "--jq", ".head.sha[:7]").Said;

    /// <summary>When each of those items was held, and by whom.</summary>
    internal static JsonObject GatedBlocked(IEnumerable<JsonNode> cards)
    {
        var blocked = new JsonObject();
        foreach (var card in cards)
            if (card["marks"]?["held"] is JsonObject held && held["at"] is not null)
                blocked[Cards.N(card)] = held.DeepClone();
        return blocked;
    }

    /// <summary>Every item's body, comments and open PR in one call, plus one for PRs GitHub hasn't linked; null and the exit code where GitHub won't say.</summary>
    internal (JsonNode? Page, int Code, string Err) GatedTalk(IEnumerable<long> numbers, bool checks = false, string extra = "", bool aloud = true)
    {
        var runs = checks ? PrHeadRuns : "";
        var reviewed = $"{TalkedOn} url isDraft mergeable mergeStateStatus baseRefName headRefName {runs}\n              reviews(last: 50) {{ nodes {{ createdAt body state author {{ __typename login }} {SeenBy}\n              comments(first: 50) {{ nodes {{ createdAt body author {{ __typename login }} {SeenBy} }} }} }} }}";
        var query = string.Concat(numbers.Select(n => $" x{n}: issueOrPullRequest(number: {n}) {{\n      ... on Issue {{ {TalkedOn} subIssuesSummary {{ total completed }}\n        closedByPullRequestsReferences(first: 1, includeClosedPrs: false) {{ nodes {{ {reviewed} }} }} }}\n      ... on PullRequest {{ {reviewed} }} }}"));
        if (query.Length + extra.Length == 0) return (Json.Parse(NoTalk), 0, "");
        var read = TalkRead($"{query} {extra}\n      openPrs: pullRequests(states: OPEN, first: 100) {{ nodes {{ number body }} }} }} ", aloud);
        if (!read.Ok) return (null, read.Code, read.Err);
        var page = Json.TryParse(read.Out);
        if (page?["data"]?["repository"] is not JsonObject repository) return (null, 2, read.Err);
        var unlinked = Unlinked(repository);
        var more = string.Concat(unlinked.Values.Distinct().Order().Select(pr => $" p{pr}: pullRequest(number: {pr}) {{ {reviewed} }}"));
        if (more.Length > 0)
        {
            var linked = TalkRead($"{more}}} ", aloud);
            if (!linked.Ok) return (null, linked.Code, read.Err + linked.Err);
            var found = Json.TryParse(linked.Out);
            if (found is null) return (null, 2, read.Err + linked.Err);
            foreach (var (issue, pr) in unlinked)
            {
                var nodes = new JsonArray();
                if (found["data"]?["repository"]?[$"p{pr}"] is { } node) nodes.Add((JsonNode?)node.DeepClone());
                repository[$"x{issue}"]!["closedByPullRequestsReferences"]!["nodes"] = nodes;
            }
        }
        repository.Remove("openPrs");
        return (page, 0, "");
    }

    private GhResult TalkRead(string fields, bool aloud)
    {
        var (owner, name) = RepoParts;
        var read = _gh.Run("api", "graphql", "-F", $"owner={owner}", "-F", $"name={name}", "-f",
            $"query=query($owner: String!, $name: String!) {{ repository(owner: $owner, name: $name) {{{fields}}}");
        if (aloud) Console.Error.Write(read.Err);
        return read;
    }

    private static IEnumerable<JsonNode> TalkedItems(JsonNode? talk) =>
        (talk?["data"]?["repository"] as JsonObject ?? []).Select(entry => entry.Value).OfType<JsonNode>();

    private static IEnumerable<JsonNode> ClosingPrs(JsonNode item) =>
        item["closedByPullRequestsReferences"]?["nodes"]?.AsArray().OfType<JsonNode>() ?? [];

    private static JsonObject Spoke(JsonNode said, string kind) => new()
    {
        ["at"] = said["createdAt"]?.DeepClone(),
        ["author"] = LoginOf(said["author"]),
        ["body"] = Json.Text(said["body"]) ?? "",
        ["eyes"] = said["reactions"]?["totalCount"]?.DeepClone() ?? 0,
        ["kind"] = kind,
    };

    private static IEnumerable<JsonObject> AllSaidOn(JsonNode item)
    {
        yield return Spoke(item, "body");
        foreach (var comment in item["comments"]?["nodes"]?.AsArray().OfType<JsonNode>() ?? [])
            yield return Spoke(comment, "comment");
        foreach (var review in item["reviews"]?["nodes"]?.AsArray().OfType<JsonNode>() ?? [])
        {
            if (!string.IsNullOrEmpty(Json.Text(review["body"])) || Json.Text(review["state"]) == "CHANGES_REQUESTED")
                yield return Spoke(review, "review");
            foreach (var line in review["comments"]?["nodes"]?.AsArray().OfType<JsonNode>() ?? [])
                yield return Spoke(line, "line");
        }
    }

    /// <summary>Everything said on those items, as {at, author, body, eyes, kind, n}.</summary>
    internal static List<JsonObject> GatedComments(JsonNode? talk)
    {
        var said = new List<JsonObject>();
        foreach (var item in TalkedItems(talk))
        {
            foreach (var one in AllSaidOn(item).Concat(ClosingPrs(item).SelectMany(AllSaidOn)))
            {
                one["n"] = item["number"]?.DeepClone();
                said.Add(one);
            }
        }
        return said;
    }

    private static List<JsonNode?> LoweredRuns(JsonNode? commit)
    {
        var runs = new List<JsonNode?>();
        foreach (var suite in commit?["checkSuites"]?["nodes"]?.AsArray().OfType<JsonNode>() ?? [])
            foreach (var run in suite["checkRuns"]?["nodes"]?.AsArray().OfType<JsonNode>() ?? [])
                runs.Add((JsonNode?)new JsonObject
                {
                    ["name"] = run["name"]?.DeepClone(),
                    ["status"] = Json.Text(run["status"])?.ToLowerInvariant(),
                    ["conclusion"] = (Json.Text(run["conclusion"]) ?? "").ToLowerInvariant(),
                    ["completed_at"] = run["completedAt"]?.DeepClone(),
                    ["html_url"] = run["url"]?.DeepClone(),
                });
        return runs;
    }

    /// <summary>The open PR that closes each of those items, or that is the item. A fix's PR is never `main-red`.</summary>
    internal static List<JsonObject> GatedPrs(JsonNode? talk, JsonObject? main, IReadOnlySet<long> fixes)
    {
        var prs = new List<JsonObject>();
        foreach (var item in TalkedItems(talk))
        {
            var n = Json.Long(item["number"]);
            var own = item is JsonObject itself && itself.ContainsKey("isDraft") ? [item] : ClosingPrs(item);
            foreach (var pr in own)
            {
                var checks = Checks(LoweredRuns(pr["commits"]?["nodes"] is JsonArray { Count: > 0 } commits ? commits[0]?["commit"] : null));
                var verdict = fixes.Contains(n) ? Json.Text(checks["verdict"])! : AgainstMain(checks, main);
                prs.Add(new JsonObject
                {
                    ["n"] = n,
                    ["pr"] = pr["number"]?.DeepClone(),
                    ["prUrl"] = pr["url"]?.DeepClone(),
                    ["draft"] = pr["isDraft"]?.DeepClone(),
                    ["self"] = Json.Long(pr["number"]) == n,
                    ["conflicting"] = Json.Text(pr["mergeable"]) == "CONFLICTING",
                    ["behind"] = Json.Text(pr["mergeStateStatus"]) == "BEHIND",
                    ["resolving"] = Json.Text(pr["mergeable"]) == "UNKNOWN",
                    ["base"] = pr["baseRefName"]?.DeepClone(),
                    ["head"] = pr["headRefName"]?.DeepClone(),
                    ["checks"] = verdict,
                    ["failedAt"] = checks["failing"]!.AsArray().Select(run => Json.Text(run?["at"])).OfType<string>().Max(StringComparer.Ordinal) ?? "",
                });
            }
        }
        return prs;
    }

    private (string Trouble, string At, string? Turn)? WrongWith(JsonObject? pr, JsonNode item, string role, bool reviewed)
    {
        if (pr is null) return null;
        var checks = Json.Text(pr["checks"]);
        var draft = Json.Truthy(pr["draft"]);
        var onto = Raw(pr["base"]);
        if (checks == "fail") return ("CI failing", Json.Text(pr["failedAt"]) ?? "", null);
        if (checks == "main-red") return ($"{onto} is red", "", null);
        if (Json.Truthy(pr["conflicting"])) return ($"conflicts with {onto}", "", null);
        if (Json.Truthy(pr["behind"]) && !draft) return ($"behind {onto}", "", null);
        if (Json.Truthy(pr["resolving"])) return ("resolving mergeable status", "", null);
        if (checks == "pending") return ("CI running", "", null);
        if (draft && ReviewerOn && role == "dev" && !reviewed) return ("awaiting review", "", "reviewer");
        if (draft) return ("still a draft", "", null);
        if (Json.Truthy(item["dependency"]) && Json.Text(item["status"]) == "In progress") return ("green, about to merge", "", null);
        return null;
    }

    /// <summary>How many points a review's "Worth considering" section makes, one per top-level list item.</summary>
    internal static int Considered(string review)
    {
        var lines = review.Replace("\r", "").Split('\n');
        var at = Array.FindIndex(lines, line => Regex.IsMatch(line, @"^#{1,6}[ \t]+Worth considering[ \t]*$", RegexOptions.IgnoreCase));
        if (at < 0) return 0;
        var points = 0;
        foreach (var line in lines.Skip(at + 1))
        {
            if (Regex.IsMatch(line, @"^#{1,6}[ \t]|<!-- a-team:")) break;
            if (Regex.IsMatch(line, @"^(?:[-*+]|[0-9]+[.)])[ \t]+")) points++;
        }
        return points;
    }

    /// <summary>When the role last spoke on this thread. Before AckFrom a marker anywhere counted, and that history keeps reading as it did.</summary>
    private string RoleSpoke(string marker, List<JsonObject> comments) =>
        comments.Where(c => TeamSaid(c, marker) || (string.CompareOrdinal(Json.Text(c["at"]), AckFrom) < 0 && (Json.Text(c["body"]) ?? "").Contains(marker, StringComparison.Ordinal)))
            .Select(c => Json.Text(c["at"]) ?? "").DefaultIfEmpty("").Max(StringComparer.Ordinal)!;

    /// <summary>An item with its PR, whose move it is and why. A gate is the stakeholders' until one comments, and an unanswered comment outranks a PR's troubles.</summary>
    internal JsonObject Turn(JsonNode item, List<JsonObject> comments, List<JsonObject> prs)
    {
        var number = Json.Long(item["number"]);
        var status = Json.Text(item["status"]);
        var dependency = Json.Truthy(item["dependency"]);
        var settled = dependency && status == "In review";
        var ownRole = Json.Text(item["role"]);
        var role = settled ? "lead" : ownRole ?? (status == "Pitched" ? "lead" : "dev");
        var theirs = comments.Where(c => Json.Long(c["n"]) == number).ToList();
        var mine = prs.Where(pr => Json.Long(pr["n"]) == number).ToList();
        var pr = mine.FirstOrDefault(p => !Json.Truthy(p["self"]) || ownRole == "customer" || status == "In review" || dependency);
        var own = mine.FirstOrDefault(p => Json.Truthy(p["self"]));
        var said = RoleSpoke($"<!-- a-team:{role} -->", theirs);
        var asked = settled ? "" : Newest(Unanswered(role, theirs)) ?? "";
        var bodies = theirs.Where(c => Json.Text(c["kind"]) == "body").ToList();
        var question = status == "Pitched" && (Json.Truthy(item["pitch"]) || Json.Truthy(item["document"]))
            ? NeedsAnswer.In(Json.Text(bodies.FirstOrDefault()?["body"]) ?? "") : "";
        var review = theirs.LastOrDefault(c => Json.Text(c["kind"]) == "comment" && TeamSaid(c, "<!-- a-team:reviewer -->"));
        var wrong = WrongWith(pr, item, role, review is not null);
        var shown = item.DeepClone().AsObject();
        if (pr is not null)
        {
            shown["pr"] = pr["pr"]?.DeepClone();
            shown["prUrl"] = pr["prUrl"]?.DeepClone();
            shown["checks"] = pr["checks"]?.DeepClone();
            shown["conflicting"] = pr["conflicting"]?.DeepClone();
            shown["draft"] = pr["draft"]?.DeepClone();
            shown["base"] = pr["base"]?.DeepClone();
            shown["branch"] = pr["head"]?.DeepClone();
            shown["unready"] = wrong?.Trouble ?? "";
        }
        else if (own is not null)
        {
            shown["base"] = own["base"]?.DeepClone();
            shown["branch"] = own["head"]?.DeepClone();
        }
        if (question.Length > 0) shown["question"] = question;
        if (review is not null)
        {
            shown["reviewed"] = true;
            shown["consider"] = Considered(Json.Text(review["body"]) ?? "");
        }
        if (asked.Length > 0)
        {
            shown["turn"] = role;
            shown["reason"] = $"answering your feedback{Since(asked)}";
        }
        else if (wrong is { } trouble)
        {
            shown["turn"] = trouble.Turn ?? (dependency ? "dev" : role);
            shown["trouble"] = trouble.Trouble;
            shown["reason"] = $"{trouble.Trouble}{Since(trouble.At)}";
        }
        else
        {
            var why = question.Length > 0 ? "asked you" : status == "Pitched" ? "awaiting your approval" : "awaiting your acceptance";
            shown["turn"] = "you";
            shown["reason"] = $"{why}{Since(said.Length > 0 ? said : Newest(bodies) ?? "")}";
        }
        return shown;
    }

    /// <summary>The Ready tasks the Dev handed back with a question, the Dev's turn once a stakeholder replies, and `unread` the newest reply no run has left a 👀 on.</summary>
    internal List<JsonObject> Questions(IEnumerable<JsonNode> items, List<JsonObject> comments, JsonObject blocked)
    {
        var questions = new List<JsonObject>();
        foreach (var item in items)
        {
            var number = Json.Long(item["number"]);
            if (blocked[Cards.N(item)] is not { } held || Json.Text(held["by"]) is "you" or StatusRoute.Dispatcher) continue;
            var labelled = Epoch(Json.Text(held["at"]));
            var theirs = comments.Where(c => Json.Long(c["n"]) == number).ToList();
            var asked = LatestOf(theirs.Where(c => Json.Text(c["kind"]) == "comment" && TeamSaid(c, "<!-- a-team:dev -->") && Epoch(Json.Text(c["at"])) >= labelled - AskedBefore));
            if (asked is null) continue;
            var at = Json.Text(asked["at"]);
            var replies = theirs.Where(c => Json.Text(c["kind"]) != "body" && FromStakeholder(c) && !TeamSaid(c, AnyRole)
                && string.CompareOrdinal(Json.Text(c["at"]), at) > 0).ToList();
            var replied = Newest(replies) ?? "";
            var shown = item.DeepClone().AsObject();
            shown["question"] = Regex.Replace(Json.Text(asked["body"]) ?? "", @"\s*<!-- a-team:dev -->\s*$", "");
            shown["unread"] = Newest(replies.Where(c => Eyes(c) == 0)) ?? "";
            shown["turn"] = replied.Length > 0 ? "dev" : "you";
            shown["reason"] = replied.Length > 0 ? $"reading your answer{Since(replied)}" : $"asked you{Since(at ?? "")}";
            questions.Add(shown);
        }
        return questions;
    }

    /// <summary>Every held task, and who held it: the Dev with its question, else whichever of the Lead or a stakeholder spoke first since then.</summary>
    internal List<JsonObject> Holds(List<JsonNode> items, List<JsonObject> comments, JsonObject blocked)
    {
        var asked = Questions(items, comments, blocked);
        var holds = new List<JsonObject>();
        foreach (var item in items)
        {
            var mine = asked.FirstOrDefault(question => Json.Long(question["number"]) == Json.Long(item["number"]));
            if (mine is not null)
            {
                mine.Remove("unread");
                mine["heldBy"] = "dev";
                holds.Add(mine);
            }
            else
            {
                holds.Add(HoldOn(item, comments, blocked[Cards.N(item)] as JsonObject));
            }
        }
        return holds;
    }

    private JsonObject HoldOn(JsonNode item, List<JsonObject> comments, JsonObject? held)
    {
        var number = Json.Long(item["number"]);
        var labelled = Json.Text(held?["at"]);
        var since = labelled is null ? [] : comments.Where(c => Json.Long(c["n"]) == number && Json.Text(c["kind"]) == "comment"
            && Epoch(Json.Text(c["at"])) >= Epoch(labelled) - AskedBefore).ToList();
        if (Json.Text(held?["by"]) == StatusRoute.Dispatcher)
            return GaveUpOn(item, since, held!);
        var leads = since.Where(c => TeamSaid(c, "<!-- a-team:lead -->")).ToList();
        var lead = LatestOf(leads);
        var theirs = since.Where(c => FromStakeholder(c) && !TeamSaid(c, AnyRole)).ToList();
        var shown = item.DeepClone().AsObject();
        if (lead is not null && Json.Text(held?["by"]) != "you"
            && (theirs.Count == 0 || string.CompareOrdinal(Json.Text(EarliestOf(theirs)?["at"]), Json.Text(EarliestOf(leads)?["at"])) > 0))
        {
            var at = Json.Text(lead["at"]);
            var replies = theirs.Where(c => string.CompareOrdinal(Json.Text(c["at"]), at) > 0).ToList();
            var replied = Newest(replies) ?? "";
            shown["heldBy"] = "lead";
            shown["question"] = Regex.Replace(Json.Text(lead["body"]) ?? "", @"\s*<!-- a-team:lead -->\s*$", "");
            shown["unread"] = Newest(replies.Where(c => Eyes(c) == 0)) ?? "";
            shown["turn"] = replied.Length > 0 ? "lead" : "you";
            shown["reason"] = replied.Length > 0 ? $"reading your answer{Since(replied)}" : $"held by the Lead{Since(at ?? "")}";
        }
        else if (theirs.Count > 0)
        {
            var why = EarliestOf(theirs)!;
            shown["heldBy"] = "you";
            shown["question"] = why["body"]?.DeepClone();
            shown["turn"] = "you";
            shown["reason"] = $"held by you{Since(Json.Text(why["at"]) ?? "")}";
        }
        else
        {
            shown["turn"] = "you";
            shown["reason"] = $"held, no reason given{Since(labelled ?? "")}";
        }
        return shown;
    }

    private JsonObject GaveUpOn(JsonNode item, List<JsonObject> since, JsonObject held)
    {
        var why = Json.Text(held["why"]) ?? "";
        var said = LatestOf(since.Where(c => TeamSaid(c, DispatchMarker)));
        var shown = item.DeepClone().AsObject();
        shown["heldBy"] = StatusRoute.Dispatcher;
        shown["question"] = said is null ? why : Json.Text(said["body"])!.Replace(DispatchMarker, "").Trim();
        shown["turn"] = "you";
        shown["reason"] = why;
        return shown;
    }

    /// <summary>Whether a stakeholder has said anything on #n since <paramref name="at"/>.</summary>
    internal bool RepliedSince(List<JsonObject> comments, long n, string at) =>
        comments.Any(c => Json.Long(c["n"]) == n && Json.Text(c["kind"]) != "body" && FromStakeholder(c) && !TeamSaid(c, AnyRole)
            && string.CompareOrdinal(Json.Text(c["at"]), at) > 0);

    /// <summary>The Ideas with no Priority, which never get pitched until a stakeholder gives them one. One the Lead has recommended a rank for carries it, and the case from its newest `recommend` comment.</summary>
    private List<JsonObject> UnrankedIdeas(List<JsonNode> all, List<JsonObject> said)
    {
        var unranked = new List<JsonObject>();
        foreach (var card in all.Where(card => Cards.Status(card) == "Idea" && Cards.IsIssue(card) && card["priority"] is null))
        {
            var n = Number(card);
            var cases = said.Where(c => Json.Long(c["n"]) == n && Json.Text(c["kind"]) == "comment" && TeamSaid(c, "<!-- a-team:lead -->"))
                .Select(c => Regex.Match(Json.Text(c["body"]) ?? "", "^Recommended: \\*\\*[A-Za-z]+\\*\\* · (?<case>[^\n]+)"))
                .Where(match => match.Success).Select(match => match.Groups["case"].Value).ToList();
            var shown = new JsonObject
            {
                ["number"] = card["number"]?.DeepClone(),
                ["title"] = card["title"]?.DeepClone(),
                ["status"] = card["status"]?.DeepClone(),
                ["url"] = card["url"]?.DeepClone(),
                ["team"] = _team,
                ["turn"] = "you",
                ["reason"] = (Opener(card), cases.Count > 0 ? cases[^1] : null) switch
                {
                    ({ } opener, { } lead) => $"{lead} · opened by {opener}",
                    ({ } opener, null) => $"opened by {opener}, waits for your rank",
                    (null, { } lead) => lead,
                    _ => "waiting to be ranked",
                },
            };
            if (Json.Truthy(card["marks"]?["rank"])) shown["recommendation"] = card["marks"]?["rank"]?.DeepClone();
            unranked.Add(shown);
        }
        return unranked;
    }

    /// <summary>The team on this machine that works in that repo, or nothing.</summary>
    private static string TeamOf(string repo) =>
        TeamConfigs().FirstOrDefault(team => string.Equals(Json.Text(team.Config?["repo"]), repo, StringComparison.OrdinalIgnoreCase)).Team ?? "";

    private static JsonObject Link(string? team, JsonArray refs, string what, JsonNode? shown) => new()
    {
        ["team"] = team,
        ["refs"] = refs,
        ["what"] = what,
        ["shown"] = shown,
    };

    /// <summary>For each wait not yet met, the links still to go, the one holding the task now first.</summary>
    internal JsonArray Chains(JsonArray waits, JsonNode? reach, IReadOnlySet<long> heldBumps)
    {
        var chains = new JsonArray();
        foreach (var wait in waits.OfType<JsonNode>().Where(wait => !Json.Truthy(wait["met"])))
            chains.Add((JsonNode?)Chain(wait, reach, heldBumps));
        return chains;
    }

    private JsonArray Chain(JsonNode wait, JsonNode? reach, IReadOnlySet<long> heldBumps)
    {
        var repo = Json.Text(wait["repo"])!;
        var up = TeamOf(repo);
        var team = up.Length > 0 ? up : null;
        var name = repo[(repo.LastIndexOf('/') + 1)..];
        var state = Json.Text(wait["state"]);
        var r = reach?[Json.Text(wait["ref"])!];
        if (Json.Truthy(wait["bump"]))
        {
            var bump = Json.Long(wait["bump"]!["number"]);
            return Json.ArrayOf([Link(_team, Json.ArrayOf([(JsonNode?)bump]), $"#{bump}", heldBumps.Contains(bump) ? "yours" : "merges by itself")]);
        }
        if (state is "open" or "unknown")
        {
            var refs = (r?["refs"]?.AsArray().Select(Json.Long) ?? []).Prepend(Json.Long(wait["n"])).Distinct().Order();
            var named = Json.Truthy(r?["pr"]) ? r!["pr"] : wait["n"];
            return Json.ArrayOf(new[] { Link(team, Json.ArrayOf(refs.Select(n => (JsonNode?)n)), $"{name}#{Raw(named)}", wait["shown"]?.DeepClone()) }.Concat(AfterIt(wait, team, name)));
        }
        if (state is "merged" or "closed") return Json.ArrayOf(AfterIt(wait, team, name));
        return Json.ArrayOf([Link(team, [], $"{name}#{Raw(wait["n"])}", wait["shown"]?.DeepClone())]);
    }

    private IEnumerable<JsonObject> AfterIt(JsonNode wait, string? team, string name)
    {
        if (Json.Text(wait["until"]) != "released") yield break;
        yield return Link(team, [], $"{(Json.Truthy(wait["package"]) ? Raw(wait["package"]) : name)}'s release", "yours");
        if (Json.Truthy(wait["package"]))
            yield return Link(_team, [], $"{_team}'s bump", "merges by itself");
    }

    private static string ShownAs(JsonNode wait)
    {
        var released = Json.Text(wait["until"]) == "released";
        return Json.Text(wait["state"]) switch
        {
            "open" => "open",
            "unknown" => "can't read it",
            "unmerged" => "closed without merging",
            "dropped" => "closed as not planned",
            "closed" => released ? "closed, not released" : "closed",
            "merged" => released ? "merged, not released" : "merged",
            "released" => $"released in {Json.Text(wait["release"]?["tag"])}",
            _ => "",
        };
    }

    /// <summary>For each open upstream issue, the cards that stand for it: its pitch, its open tasks and their open PRs, as {ref: {refs, pr}}.</summary>
    private JsonObject UpstreamReach(List<JsonObject> waits)
    {
        const string prs = "closedByPullRequestsReferences(first: 5) { nodes { number state } }";
        var open = waits.Where(wait => Json.Text(wait["state"]) == "open" && Json.Text(wait["kind"]) == "Issue").ToList();
        var reach = new JsonObject();
        foreach (var repo in open.Select(wait => Json.Text(wait["repo"])!).Distinct().Order(StringComparer.Ordinal))
        {
            var fields = string.Concat(open.Where(wait => Json.Text(wait["repo"]) == repo).Select(wait => wait["n"]!.ToJsonString())
                .Select(n => $" i{n}: issue(number: {n}) {{ parent {{ number }} {prs}\n        subIssues(first: 20) {{ nodes {{ number state {prs} }} }} }}"));
            var page = _gh.Run("api", "graphql", "-F", $"owner={repo[..repo.IndexOf('/')]}", "-F", $"name={repo[(repo.IndexOf('/') + 1)..]}", "-f",
                $"query=query UpstreamReach($owner: String!, $name: String!) {{\n      repository(owner: $owner, name: $name) {{{fields}}} }}");
            foreach (var (key, value) in Json.TryParse(page.Out)?["data"]?["repository"] as JsonObject ?? [])
                if (value is not null)
                    reach[$"{repo}#{key.TrimStart('i')}"] = Reached(value);
        }
        return reach;
    }

    private static List<long> OpenPrsOf(JsonNode? issue) =>
        (issue?["closedByPullRequestsReferences"]?["nodes"]?.AsArray().OfType<JsonNode>() ?? [])
            .Where(pr => Json.Text(pr["state"]) == "OPEN").Select(Number).ToList();

    private static JsonObject Reached(JsonNode issue)
    {
        var subs = issue["subIssues"]?["nodes"]?.AsArray().OfType<JsonNode>().Where(sub => Json.Text(sub["state"]) == "OPEN").ToList() ?? [];
        var open = OpenPrsOf(issue).Concat(subs.SelectMany(OpenPrsOf)).ToList();
        var parent = issue["parent"]?["number"];
        var refs = (Json.Truthy(parent) ? [Json.Long(parent)] : Array.Empty<long>()).Concat(subs.Select(Number)).Concat(open);
        return new JsonObject
        {
            ["refs"] = Json.ArrayOf(refs.Select(n => (JsonNode?)n)),
            ["pr"] = open.Count > 0 ? open[0] : null,
        };
    }

    private static bool WaitsUpstream(JsonNode card) =>
        ((Cards.ReadyTask(card) && Cards.Marked(card, "upstream"))
            || (Cards.Status(card) == "In review" && Cards.IsIssue(card) && Cards.Marked(card, "upstream")))
        && !Cards.Marked(card, "held");

    /// <summary>For each task waiting upstream, how far its waits have got. Where GitHub won't say, the last detail stands.</summary>
    private JsonObject UpstreamDetail(List<JsonNode> all, JsonObject was)
    {
        var upstream = new JsonObject();
        JsonArray? bumps = null;
        foreach (var n in all.Where(WaitsUpstream).Select(Cards.N))
        {
            var read = Aloud("api", "--paginate", $"repos/{_repo}/issues/{n}/comments");
            if (!read.Ok)
            {
                if (was["detail"]?["upstream"]?[n] is { } kept) upstream[n] = kept.DeepClone();
                continue;
            }
            var said = WaitsIn(read, Bot);
            if (said.Count == 0) continue;
            var waits = UpstreamState(said);
            foreach (var wait in waits)
                wait["shown"] = ShownAs(wait);
            if (waits.Any(wait => Json.Text(wait["state"]) == "released" && Json.Truthy(wait["package"])))
                WithBumps(waits, bumps ??= OpenBumps());
            var reach = UpstreamReach(waits);
            upstream[n] = new JsonObject { ["waits"] = Json.ArrayOf(waits), ["reach"] = reach };
        }
        return upstream;
    }

    private static List<long> TalkedOf(IEnumerable<JsonNode> cards) =>
        cards.Where(card => Cards.Status(card) is "Pitched" or "In review" || Cards.Held(card)
                || (Cards.Status(card) == "In progress" && Cards.Dependabot(card))
                || (Cards.Status(card) == "Idea" && Cards.IsIssue(card) && card["priority"] is null && card["marks"]?["rank"] is not null))
            .Select(Number).ToList();

    private static string BlockedByFields(IEnumerable<long> numbers) =>
        string.Join("\n", numbers.Select(n => $"i{n}: issue(number: {n}) {{ blockedBy(first: 10) {{ nodes {{ number state repository {{ nameWithOwner }} }} }} }}"));

    /// <summary>Of a repository page's `i&lt;n&gt;` fields, each open prerequisite in this repo with the tasks it blocks.</summary>
    private JsonObject Frees(JsonObject repository)
    {
        var pairs = new List<(long Prerequisite, long N)>();
        foreach (var (key, value) in repository)
        {
            if (!Regex.IsMatch(key, "^i[0-9]+$")) continue;
            var n = long.Parse(key[1..], CultureInfo.InvariantCulture);
            foreach (var node in value?["blockedBy"]?["nodes"]?.AsArray().OfType<JsonNode>() ?? [])
                if (Json.Text(node["state"]) == "OPEN" && string.Equals(Json.Text(node["repository"]?["nameWithOwner"]), _repo, StringComparison.OrdinalIgnoreCase))
                    pairs.Add((Number(node), n));
        }
        var frees = new JsonObject();
        foreach (var group in pairs.GroupBy(pair => pair.Prerequisite).OrderBy(group => group.Key))
            frees[group.Key.ToString(CultureInfo.InvariantCulture)] = Json.ArrayOf(group.Select(pair => (JsonNode?)pair.N));
        return frees;
    }

    /// <summary>The default branch's head off a repository page as {sha, failing} while its latest completed CI has failed, else null.</summary>
    private static JsonObject? MainOf(JsonObject repository)
    {
        var head = repository["defaultBranchRef"]?["target"];
        var checks = Checks(LoweredRuns(head));
        if (Json.Text(checks["verdict"]) != "fail") return null;
        var failing = checks["failing"]!.AsArray().Select(run => Json.Text(run?["name"])).OfType<string>().Distinct().Order(StringComparer.Ordinal);
        return new JsonObject { ["sha"] = Json.Text(head?["oid"]) ?? "", ["failing"] = Json.ArrayOf(failing.Select(name => (JsonNode?)name)) };
    }

    /// <summary>A red head with its branch and the head it was first seen red on, or null.</summary>
    private JsonObject? RedSince(JsonObject? red, string branch)
    {
        var file = Kept("red-since");
        if (red is null)
        {
            if (!DryRun) Forget(file);
            return null;
        }
        var sha = Json.Text(red["sha"]) ?? "";
        var since = File.Exists(file) ? File.ReadAllText(file).TrimEnd('\n') : "";
        if (since.Length == 0)
        {
            since = sha;
            if (!DryRun) Keep(file, since);
        }
        return new JsonObject { ["branch"] = branch, ["sha"] = sha, ["failing"] = red["failing"]?.DeepClone(), ["since"] = since };
    }

    /// <summary>Stores what `waiting` reads its reasons from, in one call and then the upstream waits; where GitHub can't be read, records that it failed.</summary>
    internal void SyncDetail()
    {
        var all = OpenCards();
        var was = Detail();
        if (TooFewRequestsLeft()) return;
        CatchUp(all);
        var ready = all.Where(card => Cards.ReadyTask(card) && Json.Long(card["blockedBy"]) > 0).Select(Number);
        var (page, _, _) = GatedTalk(TalkedOf(all), checks: true, extra: $"{MainFields} {BlockedByFields(ready)}", aloud: false);
        if (page?["data"]?["repository"] is not JsonObject repository)
        {
            _record.Exec($"INSERT INTO details (team, failed) VALUES ({TeamQ}, {Reconciler.Q(Now())})\n      ON CONFLICT (team) DO UPDATE SET failed = excluded.failed;");
            DetailSynced();
            return;
        }
        var main = RedSince(MainOf(repository), Json.Text(repository["defaultBranchRef"]?["name"]) ?? "");
        var frees = Frees(repository);
        foreach (var key in repository.Select(entry => entry.Key).Where(key => !Regex.IsMatch(key, "^x[0-9]+$")).ToList())
            repository.Remove(key);
        var detail = new JsonObject { ["talk"] = page, ["main"] = main, ["frees"] = frees, ["upstream"] = UpstreamDetail(all, was) };
        _record.Exec($"INSERT INTO details (team, detail, synced) VALUES ({TeamQ}, {Reconciler.Q(Json.Compact(detail))}, {Reconciler.Q(Now())})\n    ON CONFLICT (team) DO UPDATE SET detail = excluded.detail, synced = excluded.synced, failed = NULL;");
        DetailSynced();
    }

    private bool TooFewRequestsLeft()
    {
        var left = _gh.Run("api", "rate_limit", "--jq", "[.resources.graphql.remaining, .resources.core.remaining] | map(numbers) | min // empty");
        var floor = int.TryParse(Environment.GetEnvironmentVariable("A_TEAM_DETAIL_FLOOR"), out var set) ? set : 500;
        return left.Ok && left.Said.Length > 0 && !(long.TryParse(left.Said, out var requests) && requests >= floor);
    }

    private void DetailSynced()
    {
        if (!DryRun) Touch(Kept("detail-synced"));
    }

    /// <summary>The cards' detail as the last sync stored it, with when that was and when one has failed since, each null until there's one.</summary>
    internal JsonObject Detail()
    {
        var row = _record.Query($"SELECT detail, synced, failed FROM details WHERE team = {TeamQ};").FirstOrDefault();
        if (row is null) return new JsonObject { ["detail"] = null };
        return new JsonObject
        {
            ["detail"] = string.IsNullOrEmpty(row["detail"]) ? null : Json.Parse(row["detail"]!),
            ["synced"] = row["synced"],
            ["failed"] = row["failed"],
        };
    }

    private static bool Says(JsonNode? text) => !string.IsNullOrEmpty(Json.Text(text));

    /// <summary>Whether one of those items reached its card since the stored detail was synced.</summary>
    private static bool TalkMissing(List<JsonNode> all, JsonObject stored)
    {
        var have = stored["detail"]?["talk"]?["data"]?["repository"] as JsonObject;
        return TalkedOf(all).Any(n => have is null || !have.ContainsKey($"x{n}"));
    }

    /// <summary>The detail `waiting` reads, synced first where there's none, or an item has reached its card since. A failed sync makes the app read again, so reading only retries one that last worked.</summary>
    internal JsonObject DetailForReasons(List<JsonNode> all)
    {
        var stored = Detail();
        if (!Says(stored["synced"]))
        {
            SyncDetail();
            stored = Detail();
            if (!Says(stored["synced"]))
                throw new Die(Says(stored["failed"]) ? "can't read the cards' reasons from GitHub" : "too few GitHub API requests left this hour to read the cards' reasons");
        }
        else if (!Says(stored["failed"]) && TalkMissing(all, stored))
        {
            SyncDetail();
            stored = Detail();
        }
        return stored;
    }

    private JsonObject Gated(JsonNode card)
    {
        var pitch = Cards.Pitch(card);
        var shown = new JsonObject
        {
            ["number"] = card["number"]?.DeepClone(),
            ["title"] = card["title"]?.DeepClone(),
            ["status"] = card["status"]?.DeepClone(),
            ["url"] = card["url"]?.DeepClone(),
            ["team"] = _team,
            ["priority"] = card["priority"]?.DeepClone(),
            ["pitch"] = Cards.IsIssue(card) && pitch,
        };
        if (Cards.Marked(card, "customer")) shown["role"] = "customer";
        if (Cards.Fix(card)) shown["fix"] = true;
        if (Cards.Dependabot(card)) shown["dependency"] = true;
        if (Cards.IsPr(card) && Cards.Status(card) == "Pitched" && (pitch || Cards.Marked(card, "customer"))) shown["document"] = true;
        return shown;
    }

    private JsonObject HeldCard(JsonNode card)
    {
        var shown = new JsonObject
        {
            ["number"] = card["number"]?.DeepClone(),
            ["title"] = card["title"]?.DeepClone(),
            ["status"] = card["status"]?.DeepClone(),
            ["url"] = card["url"]?.DeepClone(),
            ["team"] = _team,
            ["priority"] = card["priority"]?.DeepClone(),
            ["pitch"] = Cards.GaveUp(card) && Cards.Pitch(card),
        };
        if (Cards.Dependabot(card))
        {
            shown["dependency"] = true;
            shown["prUrl"] = card["url"]?.DeepClone();
        }
        if (card["marks"]?["secret"] is JsonObject secret)
        {
            var named = secret.DeepClone().AsObject();
            named["repo"] = _repo;
            shown["secret"] = named;
        }
        return shown;
    }

    private (List<JsonObject> Waiting, JsonObject Reviewed) UpstreamCards(List<JsonNode> all, JsonObject stored)
    {
        var upstream = new List<JsonObject>();
        var reviewed = new JsonObject();
        var heldBumps = all.Where(card => Cards.Dependabot(card) && Cards.Marked(card, "held")).Select(Number).ToHashSet();
        foreach (var card in all.Where(WaitsUpstream))
        {
            var n = Cards.N(card);
            var entry = stored["detail"]?["upstream"]?[n];
            if (entry?["waits"] is not JsonArray waits) continue;
            var chains = Chains(waits, entry["reach"], heldBumps);
            if (Cards.Status(card) == "In review")
            {
                if (chains.Count > 0) reviewed[n] = chains;
                continue;
            }
            var line = string.Join("; ", waits.OfType<JsonNode>().Select(wait => $"{RepoName(wait)}#{Raw(wait["n"])}: {Raw(wait["shown"])}"));
            upstream.Add(new JsonObject
            {
                ["number"] = card["number"]?.DeepClone(),
                ["title"] = card["title"]?.DeepClone(),
                ["status"] = card["status"]?.DeepClone(),
                ["url"] = card["url"]?.DeepClone(),
                ["team"] = _team,
                ["priority"] = card["priority"]?.DeepClone(),
                ["pitch"] = false,
                ["turn"] = "upstream",
                ["upstream"] = line,
                ["reason"] = $"waiting on {line}",
                ["chains"] = chains,
            });
        }
        return (upstream, reviewed);
    }

    private static string RepoName(JsonNode wait)
    {
        var repo = Json.Text(wait["repo"]) ?? "";
        return repo[(repo.LastIndexOf('/') + 1)..];
    }

    private static void WithTasks(JsonObject item, JsonNode? talk)
    {
        if (!Json.Truthy(item["pitch"]) || Json.Text(item["status"]) != "In review") return;
        var number = Json.Long(item["number"]);
        foreach (var said in TalkedItems(talk))
        {
            if (said["subIssuesSummary"] is not { } summary || Json.Long(said["number"]) != number) continue;
            item["tasks"] = summary["total"]?.DeepClone();
            item["openTasks"] = Json.Long(summary["total"]) - Json.Long(summary["completed"]);
        }
    }

    private static void WithFrees(JsonObject item, JsonObject frees)
    {
        var freed = (frees[Raw(item["number"])]?.AsArray().Select(Json.Long) ?? [])
            .Concat(frees[item["pr"] is { } pr && Json.Truthy(pr) ? Raw(pr) : "0"]?.AsArray().Select(Json.Long) ?? []).Distinct().Order().ToList();
        if (freed.Count > 0) item["frees"] = Json.ArrayOf(freed.Select(n => (JsonNode?)n));
    }

    /// <summary>Everything waiting on someone, whose turn it is and why, then a red default branch and when the detail was last synced as entries with no number.</summary>
    internal JsonArray WaitingOn(List<JsonNode> all, JsonObject stored)
    {
        var detail = stored["detail"];
        var talk = detail?["talk"] ?? Json.Parse(NoTalk);
        var said = GatedComments(talk);
        var main = detail?["main"] as JsonObject;
        var prs = GatedPrs(talk, main, all.Where(Cards.Fix).Select(Number).ToHashSet());
        var (upstream, reviewed) = UpstreamCards(all, stored);
        var waiting = all.Where(card => !Cards.GaveUp(card) && (Cards.Status(card) is "Pitched" or "In review" || (Cards.Status(card) == "In progress" && Cards.Dependabot(card))))
            .Select(card => Turn(Gated(card), said, prs)).ToList();
        foreach (var item in waiting)
            WithTasks(item, talk);
        var heldCards = all.Where(Cards.Held).Select(HeldCard).Cast<JsonNode>().ToList();
        foreach (var hold in Holds(heldCards, said, GatedBlocked(all)))
        {
            hold.Remove("unread");
            if (Json.Text(hold["heldBy"]) == StatusRoute.Dispatcher && prs.FirstOrDefault(pr => Json.Long(pr["n"]) == Json.Long(hold["number"])) is { } held)
            {
                hold["pr"] = held["pr"]?.DeepClone();
                hold["prUrl"] = held["prUrl"]?.DeepClone();
                hold["base"] = held["base"]?.DeepClone();
                hold["branch"] = held["head"]?.DeepClone();
            }
            waiting.Add(hold);
        }
        waiting.AddRange(UnrankedIdeas(all, said));
        waiting.AddRange(upstream);
        var frees = detail?["frees"] as JsonObject ?? [];
        var unticked = UntickedOn(talk);
        foreach (var item in waiting)
            Finish(item, all, frees, reviewed, unticked);
        Snapshot(waiting.Count(item => Json.Text(item["turn"]) != "upstream" && !(Json.Truthy(item["dependency"]) && Json.Text(item["status"]) == "In progress")));
        var list = Json.ArrayOf(waiting);
        if (main is not null)
        {
            var red = main.DeepClone().AsObject();
            var fix = all.FirstOrDefault(card => Cards.Fix(card) && card["closed"] is null && Cards.Status(card) != "Done");
            red["fix"] = fix is null ? null : new JsonObject { ["number"] = fix["number"]?.DeepClone(), ["status"] = fix["status"]?.DeepClone() };
            list.Add((JsonNode?)new JsonObject { ["team"] = _team, ["red"] = red });
        }
        list.Add((JsonNode?)new JsonObject { ["team"] = _team, ["synced"] = stored["synced"]?.DeepClone(), ["failed"] = stored["failed"]?.DeepClone() });
        return list;
    }

    private static Dictionary<string, int> UntickedOn(JsonNode? talk)
    {
        var unticked = new Dictionary<string, int>();
        foreach (var (key, value) in talk?["data"]?["repository"] as JsonObject ?? [])
            if (key.StartsWith('x'))
                unticked[key[1..]] = Checklist.Unticked(Json.Text(value?["body"]) ?? "");
        return unticked;
    }

    private static void Finish(JsonObject item, List<JsonNode> all, JsonObject frees, JsonObject reviewed, Dictionary<string, int> unticked)
    {
        var n = Raw(item["number"]);
        WithFrees(item, frees);
        if (Json.Text(item["status"]) == "In review" && reviewed[n] is { } chains) item["chains"] = chains.DeepClone();
        var card = all.FirstOrDefault(one => Number(one) == Json.Long(item["number"]));
        if (card is not null && Facts(card) is { Length: > 0 } facts) item["facts"] = facts;
        if (card is not null) PitchRank(card, item);
        if (Json.Text(item["status"]) == "In review" && !Json.Truthy(item["pitch"]) && unticked.GetValueOrDefault(n) > 0)
            item["unticked"] = unticked[n];
    }

    /// <summary>How many items are waiting on the stakeholders now, at most once an hour.</summary>
    private void Snapshot(int count)
    {
        if (DryRun) return;
        try
        {
            _record.Exec($"INSERT INTO queue (team, at, waiting) SELECT {TeamQ}, {Reconciler.Q(Now())}, {count}\n    WHERE NOT EXISTS (SELECT 1 FROM queue WHERE team = {TeamQ} AND at > {SqlNow("-1 hour")});");
        }
        catch (InvalidOperationException)
        {
            Console.Error.WriteLine("board.sh: couldn't record how much is waiting");
        }
    }

    /// <summary>Records what people did on GitHub directly since the last catch-up, in one query. It writes events and what's been seen, and never a status.</summary>
    private void CatchUp(List<JsonNode> all)
    {
        if (DryRun) return;
        var now = Now();
        Row? state;
        try
        {
            state = _record.Query($"""
                SELECT (SELECT started FROM caught_up WHERE team = {TeamQ}) AS started,
                      (SELECT at FROM caught_up WHERE team = {TeamQ}) AS at,
                      (SELECT MIN(at) FROM events WHERE team = {TeamQ}) AS first,
                      (SELECT json_group_array(json_object('item', item, 'node', node, 'status', status))
                        FROM seen WHERE team = {TeamQ}) AS seen;
                """)[0];
        }
        catch (InvalidOperationException)
        {
            return;
        }
        var started = state["started"] ?? state["first"] ?? now;
        var since = new[] { started, Iso(Epoch(state["at"] ?? started) - 600) }.Max(StringComparer.Ordinal)!;
        var found = new JsonObject();
        if (state["at"] is not null || state["first"] is not null)
        {
            var read = Aloud("api", "graphql", "-f", $"q=repo:{_repo} updated:>={since}", "-f", $"since={since}", "-f",
                $"query=query($q: String!, $since: DateTime!) {{\n        search(query: $q, type: ISSUE, first: 100) {{ nodes {{ ...IssueEvents ...PullEvents }} }}\n        nodes(ids: {Json.Compact(ChangedSince(state["seen"], all))}) {{ ...IssueEvents ...PullEvents }} }} {CatchUpFragments}");
            if (!read.Ok)
            {
                Console.Error.WriteLine("board.sh: couldn't catch up on what happened on GitHub");
                return;
            }
            if (Json.TryParse(read.Out) is not JsonObject parsed) return;
            found = parsed;
        }
        try
        {
            _record.Exec($"BEGIN; {CaughtUpSql(found, all, started, now)} COMMIT;");
        }
        catch (InvalidOperationException)
        {
            _record.TryRollback();
            Console.Error.WriteLine("board.sh: couldn't record what happened on GitHub");
        }
    }

    /// <summary>The nodes of what's been seen that isn't where it was, for what a search by repo can't find.</summary>
    private static JsonArray ChangedSince(string? seen, List<JsonNode> all) =>
        Json.ArrayOf((Json.Parse(seen ?? "[]")?.AsArray().OfType<JsonNode>() ?? [])
            .Where(was => Json.Text(was["node"]) != "" && !all.Any(card => Number(card) == Json.Long(was["item"]) && Cards.Status(card) == Json.Text(was["status"])))
            .Select(was => was["node"]?.DeepClone()));

    private string CaughtUpSql(JsonObject found, List<JsonNode> all, string started, string now)
    {
        var sql = new StringBuilder();
        foreach (var (id, item, at, who, what) in Happenings(found, started))
        {
            var (idQ, whoQ, whatQ, atQ) = (Reconciler.Q(id), Reconciler.Q(who), Reconciler.Q(what), Reconciler.Q(at));
            var alike = what == "closed as not planned" ? $"{whatQ}, 'declined'" : whatQ;
            sql.Append($"""
                INSERT OR IGNORE INTO github (team, id, event) SELECT {TeamQ}, {idQ}, (SELECT e.id FROM events e
                   WHERE e.team = {TeamQ} AND e.item = {item} AND e.who = {whoQ}
                     AND e.what IN ({alike})
                     AND abs(julianday(e.at) - julianday({atQ})) * 1440 <= 5
                     AND NOT EXISTS (SELECT 1 FROM github g WHERE g.team = e.team AND g.event = e.id)
                   ORDER BY abs(julianday(e.at) - julianday({atQ})) LIMIT 1);
                 INSERT INTO events (team, item, at, who, what) SELECT {TeamQ}, {item}, {atQ}, {whoQ}, {whatQ}
                   WHERE EXISTS (SELECT 1 FROM github WHERE team = {TeamQ} AND id = {idQ} AND event IS NULL);
                 UPDATE github SET event = last_insert_rowid() WHERE team = {TeamQ} AND id = {idQ} AND event IS NULL;

                """);
        }
        sql.Append($"DELETE FROM seen WHERE team = {TeamQ};\n");
        foreach (var card in all.Where(card => Json.Text(card["node"]) != ""))
            sql.Append($"INSERT INTO seen (team, item, node, status) VALUES ({TeamQ}, {Cards.N(card)}, {Reconciler.Q(Json.Text(card["node"]))}, {Reconciler.Q(Cards.Status(card))});\n");
        sql.Append($"INSERT INTO caught_up (team, started, at) VALUES ({TeamQ}, {Reconciler.Q(started)}, {Reconciler.Q(now)})\n  ON CONFLICT (team) DO UPDATE SET at = excluded.at;");
        return sql.ToString();
    }

    private IEnumerable<(string? Id, long Item, string? At, string Who, string What)> Happenings(JsonObject found, string started)
    {
        var data = found["data"];
        var issues = (data?["search"]?["nodes"]?.AsArray().OfType<JsonNode>() ?? []).Concat(data?["nodes"]?.AsArray().OfType<JsonNode>() ?? [])
            .Where(node => node["number"] is not null).GroupBy(Number).OrderBy(group => group.Key).Select(group => group.First());
        foreach (var issue in issues)
        {
            var n = Number(issue);
            foreach (var happened in issue["timelineItems"]?["nodes"]?.AsArray().OfType<JsonNode>() ?? [])
            {
                var actor = happened["author"] ?? happened["actor"];
                if (actor is null || Json.Text(actor["__typename"]) == "Bot" || string.CompareOrdinal(Json.Text(happened["createdAt"]), started) < 0) continue;
                if (WhatHappened(happened, n, issue["merged"]?.GetValueKind() == System.Text.Json.JsonValueKind.True) is not { } what) continue;
                var login = Json.Text(actor["login"]) ?? "";
                yield return (Json.Text(happened["id"]), n, Json.Text(happened["createdAt"]), Stakeholders.Contains(login) ? "you" : login, what);
            }
        }
    }

    private static string? WhatHappened(JsonNode happened, long n, bool merged)
    {
        switch (Json.Text(happened["__typename"]))
        {
            case "IssueComment": return "commented";
            case "MergedEvent": return $"accepted · PR #{n} merged";
            case "ClosedEvent":
                if (merged) return null;
                if (Json.Text(happened["closer"]?["__typename"]) == "PullRequest") return $"accepted · PR #{Raw(happened["closer"]?["number"])} merged";
                return Json.Text(happened["stateReason"]) switch
                {
                    "COMPLETED" => "accepted · closed",
                    "NOT_PLANNED" => "closed as not planned",
                    "DUPLICATE" => "closed as a duplicate",
                    _ => "closed",
                };
            default: return null;
        }
    }

    /// <summary>The issue numbers the Next themes section of the vision names, in order, once each.</summary>
    internal static List<string> ThemesNamed(string vision)
    {
        var on = false;
        var named = new List<string>();
        foreach (var line in vision.Split('\n'))
        {
            if (Regex.IsMatch(line, @"^##[ \t]+Next themes[ \t]*$"))
            {
                on = true;
                continue;
            }
            if (Regex.IsMatch(line, @"^##[ \t]")) on = false;
            if (!on) continue;
            foreach (Match match in Regex.Matches(line, "(^|[^A-Za-z0-9_/&-])#(?<n>[0-9]+)"))
                named.Add(match.Groups["n"].Value);
        }
        return named.GroupBy(n => BigInteger.Parse(n, CultureInfo.InvariantCulture)).OrderBy(group => group.Key).Select(group => group.First()).ToList();
    }

    /// <summary>When a vision that last changed then is due another review, in seconds since the epoch, or null if that can't be worked out.</summary>
    internal static double? VisionDueAt(string since, string every, string unit)
    {
        if (!DateTimeOffset.TryParseExact(since, IsoFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            || !double.TryParse(every, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            return null;
        if (unit == "months")
        {
            var utc = at.UtcDateTime;
            var moved = new DateTime(utc.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(utc.Month - 1 + (int)n).AddDays(utc.Day - 1).Add(utc.TimeOfDay);
            return new DateTimeOffset(moved).ToUnixTimeSeconds();
        }
        return at.ToUnixTimeSeconds() + n * 86400 * (unit == "weeks" ? 7 : 1);
    }

    /// <summary>Why the Lead should review the vision now, or nothing while a review PR is open; the trigger is worked out only on a sweep.</summary>
    internal string VisionDue(bool sweep)
    {
        var (asked, due) = (Kept("lead", "vision-review"), Kept("lead", "vision-due"));
        var vision = Text("vision");
        var trigger = Text("visionReview", "trigger");
        if (vision.Length == 0 || (!Path.Exists(asked) && trigger is not ("every" or "after"))) return "";
        var (owner, _) = RepoParts;
        var open = Aloud("api", $"repos/{_repo}/pulls?head={owner}:a-team/vision&state=open&per_page=1", "--jq", "length");
        if (long.TryParse(open.Said, out var count) && count > 0)
        {
            if (!DryRun)
            {
                Forget(asked);
                Forget(due);
            }
            return "";
        }
        if (Path.Exists(asked)) return "vision review asked for from Commands";
        if (!sweep && File.Exists(due)) return File.ReadAllText(due).TrimEnd('\n');
        var reason = VisionTrigger(vision, trigger);
        Keep(due, reason);
        return reason;
    }

    private string VisionTrigger(string vision, string trigger)
    {
        var last = Aloud("api", $"repos/{_repo}/commits?path={vision}&per_page=1", "--jq", ".[0].commit.committer.date // empty").Said;
        if (last.Length == 0) return "";
        var (owner, _) = RepoParts;
        var closedRead = Aloud("api", $"repos/{_repo}/pulls?head={owner}:a-team/vision&state=closed&per_page=20");
        var closed = LatestClosed(Json.TryParse(closedRead.Out) as JsonArray ?? [], last);
        return trigger == "every"
            ? VisionEvery(closed, last)
            : VisionAfter(vision, closed);
    }

    private static (string Number, string At)? LatestClosed(JsonArray prs, string since)
    {
        (string Number, string At)? latest = null;
        foreach (var pr in prs.OfType<JsonNode>())
        {
            if (pr["merged_at"] is not null || string.CompareOrdinal(Json.Text(pr["closed_at"]), since) <= 0) continue;
            var at = Json.Text(pr["closed_at"])!;
            if (latest is null || string.CompareOrdinal(at, latest.Value.At) >= 0)
                latest = (Raw(pr["number"]), at);
        }
        return latest;
    }

    private string VisionEvery((string Number, string At)? closed, string last)
    {
        var every = OrElse(Text("visionReview", "every"), "30");
        var unit = OrElse(Text("visionReview", "unit"), "days");
        if (VisionDueAt(closed?.At ?? last, every, unit) is not { } due || DateTimeOffset.UtcNow.ToUnixTimeSeconds() < due) return "";
        if (every == "1" && unit.EndsWith('s')) unit = unit[..^1];
        return $"vision review due: {every} {unit} since {(closed is { } by ? $"you closed #{by.Number} without merging it" : "it last changed")}";
    }

    private string VisionAfter(string vision, (string Number, string At)? closed)
    {
        var named = ThemesNamed(Aloud("api", "-H", "Accept: application/vnd.github.raw", $"repos/{_repo}/contents/{vision}").Out);
        if (named.Count == 0) return "";
        var (owner, name) = RepoParts;
        var fields = string.Concat(named.Select(n => $"i{n}: issueOrPullRequest(number: {n}) {{ ... on Issue {{ state closedAt }} ... on PullRequest {{ state closedAt }} }} "));
        var read = Aloud("api", "graphql", "-F", $"owner={owner}", "-F", $"name={name}", "-f",
            $"query=\n            query VisionThemes($owner: String!, $name: String!) {{ repository(owner: $owner, name: $name) {{\n              {fields}\n            }} }}");
        if (Json.TryParse(read.Out)?["data"]?["repository"] is not JsonObject repository) return "";
        var themes = repository.Select(entry => entry.Value).OfType<JsonNode>().ToList();
        var since = closed?.At ?? "";
        var closes = themes.Count(theme => Json.Text(theme["state"]) != "OPEN" && string.CompareOrdinal(Json.Text(theme["closedAt"]), since) > 0);
        var after = long.TryParse(OrElse(Text("visionReview", "after"), "50"), out var percent) ? percent : 50;
        if (themes.Count == 0 || closes * 100 < after * themes.Count) return "";
        return closed is { } by
            ? $"vision review due: {closes} of the {themes.Count} issues its themes name have closed since you closed #{by.Number} without merging it"
            : $"vision review due: {closes} of the {themes.Count} issues its themes name are closed";
    }

    /// <summary>Whether the Lead has already been asked to turn the holds from before `unblock lead` into dependencies.</summary>
    internal bool HoldsReviewed => File.Exists(Kept("lead", "holds-reviewed"));

    internal void HoldsNowReviewed()
    {
        if (HoldsReviewed || DryRun) return;
        Keep(Kept("lead", "holds-reviewed"), $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}\n");
    }

    /// <summary>The Dependabot PRs open here, with what's been said on them.</summary>
    internal JsonArray DependabotUpdates() =>
        Json.Parse(Must(_gh.Run("pr", "list", "-R", _repo, "--author", "app/dependabot", "--state", "open", "--json", "number,title,comments")).Out)?.AsArray() ?? [];

    /// <summary>Tasks and how many are closed, as the sub-issues read says; nothing where it says neither.</summary>
    internal (long Total, long Open)? SubIssues(string n)
    {
        var read = Must(_gh.Run("api", $"repos/{_repo}/issues/{n}/sub_issues", "--jq", """[length, (map(select(.state == "open")) | length)] | @tsv"""));
        var fields = read.Said.Split('\t');
        return fields.Length == 2 && long.TryParse(fields[0], out var total) && long.TryParse(fields[1], out var open) ? (total, open) : null;
    }

    /// <summary>[task, why] for each reason the Dev has to look at one of its PRs on a repo the team contributes to, on the open task its body says it's needed by.</summary>
    internal List<(long N, string Why)> UpstreamWakes(string upstream, List<JsonNode> all)
    {
        var slug = Text("app", "slug");
        var prs = Aloud("pr", "list", "-R", upstream, "--author", $"app/{slug}", "--state", "open",
            "--json", "number,isDraft,headRefOid,body,createdAt,comments,reviews,statusCheckRollup");
        if (!prs.Ok || prs.Said == "[]") return [];
        var lines = Aloud("api", "--paginate", $"repos/{upstream}/pulls/comments?since={ADayAgo()}&per_page=100",
            "--jq", """.[] | {n: (.pull_request_url | split("/") | last | tonumber), author: {login: .user.login}, createdAt: .created_at}""");
        var said = lines.Ok ? Documents(lines) : [];
        var open = all.Where(card => Cards.Status(card) != "Done").Select(Number).ToHashSet();
        var wakes = new List<(long, string)>();
        foreach (var pr in Json.TryParse(prs.Out) as JsonArray ?? [])
        {
            var number = Json.Long(pr?["number"]);
            wakes.AddRange(UpstreamReasons(pr!, said.Where(line => Json.Long(line["n"]) == number).ToList(), upstream, slug, open));
        }
        return wakes;
    }

    private static string RollupVerdict(JsonNode pr)
    {
        var checks = (pr["statusCheckRollup"] as JsonArray ?? []).OfType<JsonNode>().Select(Rollup).ToList();
        if (checks.Any(check => check.Failed)) return "fail";
        return checks.Count == 0 || checks.Any(check => !check.Done) ? "pending" : "pass";
    }

    private static (bool Done, bool Failed) Rollup(JsonNode check)
    {
        if (Json.Text(check["__typename"]) == "StatusContext")
            return (Json.Text(check["state"]) is not ("PENDING" or "EXPECTED"), Json.Text(check["state"]) is "FAILURE" or "ERROR");
        return (Json.Text(check["status"]) == "COMPLETED", FailedConclusions.Contains(Json.Text(check["conclusion"])));
    }

    /// <summary>Why the Dev should look at this PR of its own on another repo, on the open task it's needed by.</summary>
    internal IEnumerable<(long N, string Why)> UpstreamReasons(JsonNode pr, List<JsonObject> lines, string upstream, string slug, IReadOnlySet<long> open)
    {
        const string dev = "<!-- a-team:dev -->";
        var body = Json.Text(pr["body"]) ?? "";
        if (!body.Contains(dev, StringComparison.Ordinal)) yield break;
        var needed = Regex.Matches(body, "needed by ([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)#([0-9]+)", RegexOptions.IgnoreCase)
            .Where(match => string.Equals(match.Groups[1].Value, _repo, StringComparison.OrdinalIgnoreCase))
            .Select(match => long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)).Where(open.Contains).Cast<long?>().FirstOrDefault();
        if (needed is not { } n) yield break;
        var theirs = $"{upstream}#{Raw(pr["number"])}";
        var comments = (pr["comments"] as JsonArray ?? []).OfType<JsonNode>().Concat(lines).ToList();
        var said = comments.Where(c => OwnBot(c, slug) && (Json.Text(c["body"]) ?? "").Contains(dev, StringComparison.Ordinal))
            .Select(c => Json.Text(c["createdAt"])).Prepend(Json.Text(pr["createdAt"])).Max(StringComparer.Ordinal);
        var asked = StakeholdersAfter(pr, comments, said);
        var verdict = RollupVerdict(pr);
        if (verdict == "fail")
        {
            var sha = Json.Text(pr["headRefOid"]);
            yield return (n, $"CI failed on {theirs} at {(sha is null ? "null" : sha.Length > 7 ? sha[..7] : sha)}");
        }
        if (verdict == "pass" && Json.Truthy(pr["isDraft"])) yield return (n, $"{theirs} is green but still a draft");
        if (asked is not null) yield return (n, $"stakeholder feedback on {theirs} ({asked})");
    }

    private static bool OwnBot(JsonNode comment, string slug)
    {
        var login = Json.Text(comment["author"]?["login"]) ?? "";
        if (login.StartsWith("app/", StringComparison.Ordinal)) login = login[4..];
        if (login.EndsWith("[bot]", StringComparison.Ordinal)) login = login[..^5];
        return login == slug;
    }

    private string? StakeholdersAfter(JsonNode pr, List<JsonNode> comments, string? said)
    {
        var at = comments.Select(c => (Author: Json.Text(c["author"]?["login"]), At: Json.Text(c["createdAt"])));
        var reviewed = (pr["reviews"] as JsonArray ?? []).OfType<JsonNode>()
            .Where(review => !string.IsNullOrEmpty(Json.Text(review["body"])) || Json.Text(review["state"]) == "CHANGES_REQUESTED")
            .Select(review => (Author: Json.Text(review["author"]?["login"]), At: Json.Text(review["submittedAt"])));
        return at.Concat(reviewed).Where(one => Stakeholders.Contains(one.Author ?? "") && string.CompareOrdinal(one.At, said) > 0)
            .Select(one => one.At).Max(StringComparer.Ordinal);
    }

    /// <summary>The branches of that checkout's worktrees, with what git says of one it can't read passed on.</summary>
    internal static List<string> WorktreeBranches(string checkout)
    {
        const string prefix = "branch refs/heads/";
        var listed = Git("-C", checkout, "worktree", "list", "--porcelain");
        Console.Error.Write(listed.Err);
        return listed.Out.Split('\n').Where(line => line.StartsWith(prefix, StringComparison.Ordinal)).Select(line => line[prefix.Length..]).ToList();
    }

    /// <summary>The number of a PR from that branch that has merged and was the team's, or nothing.</summary>
    internal string MergedFrom(string branch)
    {
        var (owner, _) = RepoParts;
        return Must(_gh.Run("api", $"repos/{_repo}/pulls?head={owner}:{branch}&state=closed&per_page=5", "--jq",
            """[.[] | select(.merged_at != null and (((.body // "") | contains("<!-- a-team:dev -->")) or .user.login == "dependabot[bot]"))][0].number // empty""")).Said;
    }

    /// <summary>Turned on, the Customer lead starts from the pitches done after that, not from every one before.</summary>
    internal void StartCovering(List<string> accepted)
    {
        var covered = Kept("customer", "covered");
        if (!File.Exists(covered)) Keep(covered, $"{string.Join("\n", accepted)}\n");
    }

    /// <summary>The done pitches the Customer lead has already checked the docs against.</summary>
    internal string[] Covered => File.Exists(Kept("customer", "covered")) ? File.ReadAllText(Kept("customer", "covered")).Split('\n') : [];

    /// <summary>The first audit after the docs page appears writes the docs it outlines.</summary>
    internal void ForgetAudit() => Forget(Kept("customer", "audited"));

    /// <summary>Whether the Customer lead's weekly audit has come round.</summary>
    internal bool AuditDue
    {
        get
        {
            var file = Kept("customer", "audited");
            var done = File.Exists(file) && long.TryParse(File.ReadAllText(file).Trim(), out var at) ? at : 0;
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() - done >= 7 * 24 * 60 * 60;
        }
    }

    /// <summary>Whose turn the Lead took last: pitching or discovering.</summary>
    internal string LeadTurnWas => File.Exists(Kept("lead-turn")) ? File.ReadAllText(Kept("lead-turn")).TrimEnd('\n') : "discover";

    internal void LeadTurnIs(string turn) => Keep(Kept("lead-turn"), $"{turn}\n");

    /// <summary>A run started across the board shows on the Idea it pitches.</summary>
    internal void ShowRunOn(JsonNode idea)
    {
        var card = Kept("lead", "card");
        if (Environment.GetEnvironmentVariable("A_TEAM_RUN_TEAM") != _team || Path.Exists(card) || DryRun) return;
        Keep(card, Json.Compact(new JsonObject { ["number"] = idea["number"]?.DeepClone() }) + "\n");
    }
}
