using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ATeam.Core;

/// <summary>What a command asks of GitHub, as gh gives it, and every change it makes there.</summary>
internal sealed partial class Board
{
    private const string AckFrom = "2026-09-24T00:00:00Z";
    private const string AppFrom = "2026-09-29T00:00:00Z";
    private const string Login = """if . == null then "" elif .__typename == "Bot" then "\(.login)[bot]" else .login end""";

    private string Bot => Text("app", "slug") is { Length: > 0 } slug ? $"{slug}[bot]" : "";

    private List<string> Stakeholders =>
        (_config["stakeholders"]?.AsArray().Select(Json.Text) ?? (Json.Text(_config["reviewer"]) is { } one ? [one] : []))
            .OfType<string>().ToList();

    internal bool ReviewerOn => _config["roles"]?["reviewer"]?.GetValueKind() == System.Text.Json.JsonValueKind.True;

    internal bool CustomerOn => _config["roles"]?["customer"]?.GetValueKind() == System.Text.Json.JsonValueKind.True;

    /// <summary>Says what a dry run would do, and whether it's a dry run.</summary>
    internal bool Would(string what)
    {
        if (!DryRun) return false;
        if (!Quiet) Console.Error.WriteLine($"dry-run: would {what}");
        return true;
    }

    /// <summary>Set around a step whose words only matter if it fails.</summary>
    internal bool Quiet { get; set; }

    internal void Say(string text) => Console.Out.Write($"{(DryRun ? "(dry run) " : "")}{text}\n");

    /// <summary>In a dry run, what would have been posted.</summary>
    internal void Show(string text)
    {
        if (!DryRun) return;
        foreach (var line in text.TrimEnd('\n').Split('\n'))
            Console.Error.WriteLine($"  | {line}");
    }

    internal GhResult Read(params string[] args) => _gh.Run(args);

    internal GhResult Write(string what, params string[] args) =>
        Would(what) ? new GhResult(0, "", "") : _gh.Run(args);

    internal GhResult Post(string what, string body, params string[] args) =>
        Would(what) ? new GhResult(0, "", "") : _gh.Feed(body, args);

    /// <summary>What a command under `set -e` did with a call that failed: stops there, with gh's own words.</summary>
    internal static GhResult Must(GhResult result)
    {
        if (!result.Ok) throw new Stop(result.Code, result.Err);
        if (result.Err.Length > 0) Console.Error.Write(result.Err);
        return result;
    }

    private static string FirstLine(string text) => text.Split('\n')[0];

    internal static string Refusal(GhResult result) => FirstLine(result.Err);

    /// <summary>Whether a `--jq` answering true said so.</summary>
    internal static bool SaysTrue(GhResult result) => result.Ok && result.Out.Split('\n').Contains("true");

    /// <summary>A file's text, as `$(cat file)` has it.</summary>
    internal static string FileText(string file) => File.ReadAllText(file).TrimEnd('\n');

    internal void PostComment(string n, string body) =>
        Must(Post($"comment on #{n}", body + "\n", "issue", "comment", n, "-R", _repo, "--body-file", "-"));

    internal void OwnTask(string role, string n)
    {
        if (role is not ("dev" or "reviewer")) return;
        var task = Environment.GetEnvironmentVariable("A_TEAM_RUN_TASK") ?? "";
        if (task.Length == 0 || n == task) return;
        var (read, pr) = PrFor(task);
        Console.Error.Write(read.Err);
        if (!read.Ok || pr?["number"]?.ToJsonString() != n)
            throw new Die($"this run is for #{task}: leave #{n} to a run of its own");
    }

    internal void OwnDocs(string role, string n)
    {
        if (role == "customer" && (Seek(n) is not { } it || !Cards.Marked(it, "customer")))
            throw new Die($"customer only touches its own docs PR (#{n} isn't it)");
    }

    /// <summary>`gh api`, and what it found: 0 and what it printed, 1 only on GitHub's 404, 2 where GitHub didn't answer.
    /// A failed call says nothing about what's there, so nothing but a 404 counts as missing.</summary>
    internal (int There, string Said) FileThere(params string[] args)
    {
        var read = _gh.Run(["api", .. args]);
        return read.Ok ? (0, read.Out) : (read.Err.Contains("(HTTP 404)", StringComparison.Ordinal) ? 1 : 2, read.Out);
    }

    /// <summary>Whether the first page of the user docs is on the default branch: 0 yes, 1 GitHub says not, 2 GitHub didn't answer.</summary>
    internal int DocsThere() => FileThere($"repos/{_repo}/contents/{Text("docs")}", "--silent").There;

    /// <summary>Whether the first page of the user docs is on the default branch. Refuses to go on where GitHub didn't answer.</summary>
    internal bool DocsKnown()
    {
        var docs = Text("docs");
        if (docs.Length == 0) return false;
        return DocsThere() switch
        {
            0 => true,
            1 => false,
            _ => throw new Die($"GitHub didn't answer for {docs}: try again"),
        };
    }

    /// <summary>The number of the issue #n is a sub-issue of, or nothing. GraphQL answers only for an item the record has not seen.</summary>
    internal string ParentOf(string n)
    {
        if (Item(n) is { } known)
            return known["parent"]?.ToJsonString() is { } parent && parent != "null" ? parent : "";
        var (owner, name) = RepoParts;
        return Must(_gh.Run("api", "graphql", "-F", $"owner={owner}", "-F", $"name={name}", "-F", $"n={n}",
            "--jq", ".data.repository.issueOrPullRequest.parent.number // empty", "-f", """
            query=query ParentOf($owner: String!, $name: String!, $n: Int!) { repository(owner: $owner, name: $name) {
                  issueOrPullRequest(number: $n) { ... on Issue { parent { number } } } } }
            """)).Said;
    }

    private (string Owner, string Name) RepoParts => (_repo[.._repo.IndexOf('/')], _repo[(_repo.IndexOf('/') + 1)..]);

    private static bool Said(JsonNode? comment, string bot, string role) =>
        LoginOf(comment?["author"]) == bot && (Json.Text(comment?["body"]) ?? "").Contains($"<!-- a-team:{role} -->", StringComparison.Ordinal);

    private static string LoginOf(JsonNode? author)
    {
        if (author is null) return "";
        var login = Json.Text(author["login"]) ?? "";
        return Json.Text(author["__typename"]) == "Bot" ? $"{login}[bot]" : login;
    }

    private static IEnumerable<long> Closes(JsonNode? pr)
    {
        var body = Json.Text(pr?["body"]) ?? "";
        if (!body.Contains("<!-- a-team:dev -->", StringComparison.Ordinal)) yield break;
        foreach (Match match in Regex.Matches(body, @"\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?):?[ \t]+#([0-9]+)\b", RegexOptions.IgnoreCase))
            yield return long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>The open PR that closes #n, with `review` saying where the Reviewer has got to with it.</summary>
    internal (GhResult Read, JsonObject? Pr) PrFor(string n)
    {
        const string said = "comments(last: 100) { nodes { body author { __typename login } } }";
        var (owner, name) = RepoParts;
        var read = _gh.Run("api", "graphql", "-F", $"owner={owner}", "-F", $"name={name}", "-F", $"n={n}", "-f", $$"""
            query=
                query($owner: String!, $name: String!, $n: Int!) {
                  repository(owner: $owner, name: $name) {
                    issue(number: $n) {
                      closedByPullRequestsReferences(first: 10, includeClosedPrs: false) {
                        nodes { number url isDraft headRefName mergeable mergeStateStatus {{said}} }
                      }
                    }
                    pullRequests(states: OPEN, first: 100) {
                      nodes { number url isDraft headRefName mergeable mergeStateStatus body {{said}} }
                    }
                  }
                }
            """);
        if (!long.TryParse(n, CultureInfo.InvariantCulture, out var number))
            throw new Stop(2, $"{read.Err}jq: Invalid JSON text passed to --argjson\n");
        var repository = Json.TryParse(read.Out)?["data"]?["repository"];
        var found = repository?["issue"]?["closedByPullRequestsReferences"]?["nodes"]?.AsArray().FirstOrDefault()
            ?? repository?["pullRequests"]?["nodes"]?.AsArray().FirstOrDefault(pr => Closes(pr).Contains(number));
        if (found is null) return (read, null);
        var bot = Bot;
        var comments = found["comments"]?["nodes"]?.AsArray().ToList() ?? [];
        var spoke = comments.Select(c => Said(c, bot, "reviewer") ? "review" : Said(c, bot, "dev") ? "dev" : null).OfType<string>().ToList();
        var at = spoke.IndexOf("review");
        var result = found.DeepClone().AsObject();
        result.Remove("body");
        result.Remove("comments");
        result["review"] = at >= 0 && spoke.Skip(at).Contains("dev") ? "answered"
            : comments.Any(c => Said(c, bot, "reviewer")) ? "posted"
            : ReviewerOn ? "waiting" : "off";
        return (read, result);
    }

    private static JsonObject Checks(List<JsonNode?> runs)
    {
        static bool Failed(JsonNode? run) =>
            Json.Text(run?["conclusion"]) is "failure" or "timed_out" or "cancelled" or "action_required" or "startup_failure";
        static bool Going(JsonNode? run) => Json.Text(run?["status"]) != "completed";
        var failing = new JsonArray();
        foreach (var run in runs.Where(Failed))
            failing.Add((JsonNode?)new JsonObject { ["name"] = run?["name"]?.DeepClone(), ["at"] = run?["completed_at"]?.DeepClone(), ["link"] = run?["html_url"]?.DeepClone() });
        var pending = new JsonArray();
        foreach (var run in runs.Where(Going))
            pending.Add(run?["name"]?.DeepClone());
        return new JsonObject
        {
            ["verdict"] = runs.Any(Failed) ? "fail" : runs.Count == 0 || runs.Any(Going) ? "pending" : "pass",
            ["failing"] = failing,
            ["pending"] = pending,
        };
    }

    /// <summary>The CI verdict for a commit, as its check runs give it.</summary>
    internal JsonObject PrChecks(string sha) =>
        Checks(Json.ParseMany(Must(_gh.Run("api", "--paginate", $"repos/{_repo}/commits/{sha}/check-runs?per_page=100", "--jq", ".check_runs[]")).Out));

    /// <summary>The default branch's head as {branch, sha, since, failing} while its latest completed CI there has failed, else null.</summary>
    internal JsonObject? MainRed()
    {
        var read = Aloud("api", "--paginate", $"repos/{_repo}/commits/HEAD/check-runs?per_page=100", "--jq", ".check_runs[]");
        if (!read.Ok) throw new Die("could not read the default branch's CI");
        var runs = Json.ParseMany(read.Out);
        var checks = Checks(runs);
        if (Json.Text(checks["verdict"]) != "fail") return RedSince(null, "");
        var branch = Aloud("api", $"repos/{_repo}", "--jq", ".default_branch");
        if (!branch.Ok) throw new Die($"could not read {_repo}'s default branch");
        var failing = checks["failing"]!.AsArray().Select(run => Json.Text(run?["name"])).OfType<string>().Distinct().Order(StringComparer.Ordinal);
        return RedSince(new JsonObject { ["sha"] = Json.Text(runs.FirstOrDefault()?["head_sha"]) ?? "", ["failing"] = Json.ArrayOf(failing.Select(name => (JsonNode?)name)) }, branch.Said);
    }

    /// <summary>Those checks' verdict, `main-red` where every job they fail also fails on the default branch.</summary>
    internal static string AgainstMain(JsonObject checks, JsonObject? main)
    {
        var verdict = Json.Text(checks["verdict"])!;
        if (verdict != "fail" || main is null) return verdict;
        var failing = checks["failing"]!.AsArray();
        var red = main["failing"]!.AsArray().Select(Json.Text).ToList();
        return failing.Count > 0 && failing.All(run => red.Contains(Json.Text(run?["name"]))) ? "main-red" : verdict;
    }

    private static List<JsonNode?> Paged(GhResult read) =>
        Json.ParseMany(read.Out).SelectMany(page => page?.AsArray().ToList() ?? []).ToList();

    private static JsonObject Comment(string kind, JsonNode? raw) => new()
    {
        ["kind"] = kind,
        ["author"] = raw?["user"]?["login"]?.DeepClone(),
        ["at"] = raw?["created_at"]?.DeepClone(),
        ["body"] = Json.Text(raw?["body"]) ?? "",
        ["url"] = raw?["html_url"]?.DeepClone(),
        ["id"] = raw?["node_id"]?.DeepClone(),
        ["eyes"] = raw?["reactions"]?["eyes"]?.DeepClone(),
    };

    /// <summary>Lets what a program said on stderr through, as a shell does.</summary>
    internal static GhResult Through(GhResult result)
    {
        Console.Error.Write(result.Err);
        return result;
    }

    /// <summary>A read a command goes on without, with gh's words passed on.</summary>
    private GhResult Aloud(params string[] args) => Through(_gh.Run(args));

    /// <summary>All that's been said on #n, oldest first; null where GitHub won't say.</summary>
    internal List<JsonObject>? Comments(string n)
    {
        var (all, code) = CommentsSoFar(n);
        return code == 0 ? all : null;
    }

    /// <summary>All that's been said on #n as far as GitHub would say, null for nothing, and the exit code of the read it stopped at, or 0.</summary>
    internal (List<JsonObject>? All, int Code) CommentsSoFar(string n)
    {
        var read = Aloud("api", $"repos/{_repo}/issues/{n}");
        if (!read.Ok) return (null, read.Code);
        var issue = Json.Parse(read.Out);
        var all = new List<JsonObject> { Comment("body", issue) };
        (List<JsonObject>?, int) Stopped(int code) =>
            (all.OrderBy(comment => Json.Text(comment["at"]), StringComparer.Ordinal).ToList(), code);
        var comments = Aloud("api", "--paginate", $"repos/{_repo}/issues/{n}/comments");
        if (!comments.Ok) return Stopped(comments.Code);
        all.AddRange(Paged(comments).Select(c => Comment("comment", c)));
        if (issue?["pull_request"] is not null)
        {
            var reviews = ReviewsRead(n);
            if (!reviews.Ok) return Stopped(reviews.Code);
            all.AddRange(Json.ParseMany(reviews.Out).OfType<JsonObject>());
            var lines = Aloud("api", "--paginate", $"repos/{_repo}/pulls/{n}/comments");
            if (!lines.Ok) return Stopped(lines.Code);
            foreach (var line in Paged(lines))
            {
                var comment = Comment("line", line);
                comment["path"] = line?["path"]?.DeepClone();
                comment["line"] = line?["line"]?.DeepClone();
                all.Add(comment);
            }
        }
        return Stopped(0);
    }

    /// <summary>The team wrote this, as the role whose marker is <paramref name="marker"/>.</summary>
    private bool TeamSaid(JsonNode comment, string marker)
    {
        var bot = Bot;
        var body = Json.Text(comment["body"]) ?? "";
        if (bot.Length > 0 && Json.Text(comment["author"]) == bot)
            return body.Contains(marker, StringComparison.Ordinal);
        if (string.CompareOrdinal(Json.Text(comment["at"]), AppFrom) >= 0)
            return false;
        var last = body.Replace("\r", "").Split('\n').Select(line => line.TrimEnd(' ', '\t')).LastOrDefault(line => line.Length > 0) ?? "";
        return last.StartsWith(marker, StringComparison.Ordinal);
    }

    private bool FromStakeholder(JsonNode comment) => Stakeholders.Contains(Json.Text(comment["author"]) ?? "");

    private static long Eyes(JsonNode comment) => Json.Long(comment["eyes"]);

    /// <summary>The stakeholders' comments on #n that no run has left a 👀 on.</summary>
    internal List<JsonObject> UnansweredFeedback(string role, string n)
    {
        var comments = Comments(n) ?? throw new Stop(1, $"board.sh: couldn't read what's been said on #{n}\n");
        return Unanswered(role, comments);
    }

    /// <summary>Of what's been said on a card, the stakeholders' comments no run has left a 👀 on.</summary>
    internal List<JsonObject> Unanswered(string role, List<JsonObject> comments)
    {
        var since = RoleSpoke($"<!-- a-team:{role} -->", comments);
        return comments.Where(c => Json.Text(c["kind"]) != "body" && FromStakeholder(c) && !TeamSaid(c, "<!-- a-team:") && Eyes(c) == 0
            && (string.CompareOrdinal(Json.Text(c["at"]), AckFrom) >= 0 || string.CompareOrdinal(Json.Text(c["at"]), since) > 0)).ToList();
    }

    internal void React(string subject, string what) =>
        Must(Write($"add 👀 to {what}", "api", "graphql", "-F", $"subject={subject}", "-f", """
            query=
                mutation($subject: ID!) {
                  addReaction(input: {subjectId: $subject, content: EYES}) { reaction { content } } }
            """));

    /// <summary>A 👀 on every stakeholder comment on #n this run could have seen. One that arrived mid-run is newer than the
    /// run's start, so it stays unanswered and gets a run of its own.</summary>
    internal void Ack(string n)
    {
        var started = Environment.GetEnvironmentVariable("A_TEAM_RUN_STARTED");
        if (string.IsNullOrEmpty(started)) started = Now();
        foreach (var comment in Comments(n) ?? [])
        {
            var at = Json.Text(comment["at"]);
            if (string.CompareOrdinal(at, started) < 0 && Json.Text(comment["kind"]) != "body" && FromStakeholder(comment)
                && !TeamSaid(comment, "<!-- a-team:") && Eyes(comment) == 0)
                React(Json.Text(comment["id"])!, $"your comment of {at} on #{n}");
        }
    }

    /// <summary>What task #n still waits on in other repos, by the wait markers on it.</summary>
    private JsonArray? WaitsOn(string n, string repo, string bot)
    {
        var read = _gh.Run("api", "--paginate", $"repos/{repo}/issues/{n}/comments");
        if (!read.Ok && !SaidOnlyAnError(read)) _holdingLost = true;
        return read.Ok ? WaitsIn(read, bot) : null;
    }

    private static JsonArray WaitsIn(GhResult read, string bot)
    {
        var waits = new List<JsonObject>();
        foreach (var comment in Paged(read))
        {
            if (Json.Text(comment?["user"]?["login"]) != bot) continue;
            foreach (Match match in Regex.Matches(Json.Text(comment?["body"]) ?? "",
                "<!-- a-team:(wait|unwait) repo=([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+) n=([0-9]+)(?: until=(closed|released))?(?: package=([A-Za-z0-9_.-]+))? -->"))
            {
                var reference = $"{match.Groups[2].Value}#{match.Groups[3].Value}";
                waits.RemoveAll(wait => Json.Text(wait["ref"]) == reference);
                if (match.Groups[1].Value != "wait") continue;
                waits.Add(new JsonObject
                {
                    ["ref"] = reference,
                    ["repo"] = match.Groups[2].Value,
                    ["n"] = long.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                    ["until"] = match.Groups[4].Success ? match.Groups[4].Value : null,
                    ["package"] = match.Groups[5].Success ? match.Groups[5].Value : null,
                });
            }
        }
        var array = new JsonArray();
        foreach (var wait in waits)
            array.Add((JsonNode?)wait);
        return array;
    }

    private JsonArray? UpstreamRefs(string n)
    {
        if (WaitsOn(n, _repo, Bot) is not { Count: > 0 } waits) return null;
        var refs = new JsonArray();
        foreach (var wait in waits)
            refs.Add(wait?["ref"]?.DeepClone());
        return refs;
    }

    private JsonObject? Shelved(string n)
    {
        var read = _gh.Run("api", "--paginate", $"repos/{_repo}/issues/{n}/comments");
        if (!read.Ok) return null;
        var bot = Bot;
        JsonObject? found = null;
        foreach (var comment in Paged(read))
        {
            var author = Json.Text(comment?["user"]?["login"]) ?? "";
            if (author != bot && !Stakeholders.Contains(author)) continue;
            var match = Regex.Match(Json.Text(comment?["body"]) ?? "", "<!-- a-team:shelved until=(?<until>[0-9-]+) priority=(?<priority>.+?) -->");
            if (!match.Success) continue;
            var priority = match.Groups["priority"].Value;
            found = new JsonObject { ["until"] = match.Groups["until"].Value, ["priority"] = priority == "none" ? null : priority };
        }
        return found;
    }

    /// <summary>When each of those issues was last marked `blocked` and by whom; what GitHub won't say is left out.</summary>
    private JsonObject HeldSince(List<string> numbers)
    {
        var held = new JsonObject();
        if (numbers.Count == 0) return held;
        var fields = string.Concat(numbers.Select(n => $$"""
             x{{n}}: issueOrPullRequest(number: {{n}}) { ... on Issue { timelineItems(last: 50, itemTypes: [LABELED_EVENT]) {
                  nodes { ... on LabeledEvent { createdAt label { name } actor { __typename login } } } } } }
            """));
        var (owner, name) = RepoParts;
        var read = _gh.Run("api", "graphql", "-F", $"owner={owner}", "-F", $"name={name}", "-f",
            $"query=query HeldSince($owner: String!, $name: String!) {{\n        repository(owner: $owner, name: $name) {{{fields}}} }}");
        if (!read.Ok) return held;
        try
        {
            foreach (var (key, value) in Json.Parse(read.Out)?["data"]?["repository"]?.AsObject() ?? [])
            {
                if (value is null) continue;
                var labelled = value["timelineItems"]?["nodes"]?.AsArray().Where(node => Json.Text(node?["label"]?["name"]) == "blocked")
                    .OrderBy(node => Json.Text(node?["createdAt"]), StringComparer.Ordinal).LastOrDefault();
                var login = LoginOf(labelled?["actor"]);
                held[key.TrimStart('x')] = labelled is null
                    ? new JsonObject { ["at"] = null, ["by"] = null }
                    : new JsonObject { ["at"] = labelled["createdAt"]?.DeepClone(), ["by"] = Stakeholders.Contains(login) ? "you" : "team" };
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.Text.Json.JsonException)
        {
            return new JsonObject();
        }
        return held;
    }

    /// <summary>Every team configured on this machine, with its config; one that won't read is left out.</summary>
    internal static IEnumerable<(string Team, JsonNode? Config)> TeamConfigs()
    {
        var teams = Path.Combine(ConfigDir, "teams");
        foreach (var file in (Directory.Exists(teams) ? Directory.GetFiles(teams, "*.json") : []).Order(StringComparer.Ordinal))
        {
            JsonNode? config;
            try
            {
                config = Json.Parse(File.ReadAllText(file));
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
            {
                continue;
            }
            yield return (Path.GetFileNameWithoutExtension(file), config);
        }
    }

    /// <summary>Tasks other work waits on first: named by another team's upstream wait, or by a Ready task here that's blocked, with their pitches and siblings.</summary>
    private HashSet<long> Holding(List<JsonNode> all)
    {
        var named = new List<long>();
        foreach (var (team, config) in TeamConfigs())
        {
            var repo = Json.Text(config?["repo"]) ?? "";
            var slug = Json.Text(config?["app"]?["slug"]) ?? "";
            if (repo.Length == 0 || slug.Length == 0 || string.Equals(repo, _repo, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var row in _record.Query($"""
                SELECT item FROM board WHERE team = {Reconciler.Q(team)} AND status != 'Done'
                  AND json_extract(content, '$.type') = 'Issue' AND (json_extract(marks, '$.upstream') IS NOT NULL
                    OR (marks IS NULL AND EXISTS (SELECT 1 FROM json_each(content, '$.labels') WHERE value = 'a-team:upstream')));
                """))
            {
                foreach (var wait in WaitsOn(row.First!, repo, $"{slug}[bot]") ?? [])
                    if (string.Equals(Json.Text(wait?["repo"]), _repo, StringComparison.OrdinalIgnoreCase))
                        named.Add(wait!["n"]!.GetValue<long>());
            }
        }
        named.AddRange(Prerequisites(all.Where(card => Cards.ReadyTask(card) && Json.Long(card["blockedBy"]) > 0).Select(Cards.N).ToList()));
        var holding = new HashSet<long>();
        foreach (var n in named)
        {
            holding.Add(n);
            foreach (var card in all)
            {
                var number = card["number"]!.GetValue<long>();
                var parent = card["parent"] is null ? (long?)null : Json.Long(card["parent"]);
                if (number == n && parent is { } up) holding.Add(up);
                if (parent == n) holding.Add(number);
            }
        }
        return holding;
    }

    /// <summary>What those tasks wait on in this repo.</summary>
    private List<long> Prerequisites(List<string> numbers)
    {
        if (numbers.Count == 0) return [];
        var (owner, name) = RepoParts;
        var fields = BlockedByFields(numbers.Select(n => long.Parse(n, CultureInfo.InvariantCulture)));
        var read = _gh.Run("api", "graphql", "-F", $"owner={owner}", "-F", $"name={name}", "-f",
            $"query=query Prerequisites($owner: String!, $name: String!) {{\n      repository(owner: $owner, name: $name) {{ {fields} }} }}");
        if (!read.Ok && read.Out.Trim().Length > 0) _holdingLost = true;
        if (!read.Ok) return [];
        var found = new List<long>();
        try
        {
            foreach (var (key, value) in Json.Parse(read.Out)?["data"]?["repository"]?.AsObject() ?? [])
            {
                if (!Regex.IsMatch(key, "^i[0-9]+$")) continue;
                foreach (var node in value?["blockedBy"]?["nodes"]?.AsArray() ?? [])
                    if (Json.Text(node?["state"]) == "OPEN"
                        && string.Equals(Json.Text(node?["repository"]?["nameWithOwner"]), _repo, StringComparison.OrdinalIgnoreCase))
                        found.Add(node!["number"]!.GetValue<long>());
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.Text.Json.JsonException)
        {
            return [];
        }
        return found;
    }

    /// <summary>The Ready tasks the Dev can start now, in the order it takes them: a fix for a red default branch first.</summary>
    internal List<JsonNode> Startable(List<JsonNode> all)
    {
        var list = Cards.ByPriority(all.Where(Cards.Startable));
        if (list.Count > 0)
        {
            var holding = Holding(all);
            list = list.OrderBy(card => !holding.Contains(card["number"]!.GetValue<long>())).ToList();
        }
        return list.OrderBy(card => !Cards.Fix(card)).ToList();
    }
}
