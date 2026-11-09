using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ATeam.Core;

/// <summary>What ties a card to another, here or in another repo, what holds it back, and the decision register.</summary>
internal static partial class Commands
{
    private const string UpstreamRef = @"^([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)#([0-9]+)\z";
    private const int UpdateSettles = 600;
    private const int LastLineShown = 200;
    private static readonly char[] Spaces = [' ', '\t', '\n', '\v', '\f', '\r'];

    private static Dictionary<string, Handler> Linking() => new()
    {
        ["link"] = Link,
        ["depends"] = Depends,
        ["undepend"] = Undepend,
        ["free"] = Free,
        ["give-up"] = GiveUp,
        ["unlink"] = Unlink,
        ["unblock"] = Unblock,
        ["hold"] = Hold,
        ["secret"] = Secret,
        ["gave"] = Gave,
        ["release"] = Release,
        ["update"] = Update,
        ["decide"] = Decide,
        ["decision"] = Decision,
        ["replace"] = Replace,
        ["reconfirm"] = Reconfirm,
        ["seeded"] = Seeded,
    };

    /// <summary>A value as `jq -r` prints it.</summary>
    private static string Raw(JsonNode? node) => Json.Text(node) ?? node?.ToJsonString() ?? "null";

    private static bool Digits(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);

    private static bool LabelledPitch(JsonNode? issue) =>
        issue?["labels"]?.AsArray().Any(label => Json.Text(label) == "pitch") == true;

    /// <summary>The child is an idea rather than a task, by its labels or its board status.</summary>
    private static bool Idea(JsonNode? issue, string status) =>
        LabelledPitch(issue) || status is "Idea" or "Exploring" or "Pitched" or "Approved" or "Building";

    internal static int Link(Board board, string[] args)
    {
        if (args.Length != 2)
            throw new Die($"usage: board.sh {board.Team} link <parent> <child>");
        var (parent, n) = (args[0], args[1]);
        var child = Json.Parse(Board.Must(board.Read("api", $"repos/{board.Repo}/issues/{n}", "--jq", "{id, labels: [.labels[].name]}")).Out);
        var status = board.Item(n) is { } card ? Cards.Status(card) : "";
        if (status == "Idea" && !LabelledPitch(child))
            throw new Die($"#{n} is in Idea: if it's an idea, say \"Follow-up from #{parent}\" in its body instead of linking it; if it's a task, add it to Ready first");
        if (Idea(child, status))
            throw new Die($"#{n} is an idea, not a task: say \"Follow-up from #{parent}\" in its body instead of linking it");
        Board.Must(board.Write($"make #{n} a sub-issue of #{parent}", "api", "-X", "POST", $"repos/{board.Repo}/issues/{parent}/sub_issues", "-F", $"sub_issue_id={Raw(child?["id"])}"));
        board.Log("lead", n, $"made a sub-issue of #{parent}");
        board.Say($"#{n} is now a sub-issue of #{parent}");
        return 0;
    }

    /// <summary>Why a dependency changed, on the blocked task, with the role's marker, so a blocked task answers "why?" by itself.</summary>
    private static void DependNote(Board board, string role, string task, string text)
    {
        var body = $"{text}\n\n<!-- a-team:{role} -->";
        board.Show(body);
        Console.Out.Write(Board.Must(board.Post($"comment on #{task}", body + "\n", "issue", "comment", task, "-R", board.Repo, "--body-file", "-")).Out);
    }

    private static string WaitMarker(string kind, string repo, string n, string until = "", string package = "") =>
        $"<!-- a-team:{kind} repo={repo} n={n}{(until.Length > 0 ? $" until={until}" : "")}{(package.Length > 0 ? $" package={package}" : "")} -->";

    private static void CheckDependant(string role)
    {
        CheckRole(role);
        if (role is "customer" or "reviewer") throw new Die($"{role} may not change what a task waits on");
    }

    internal static int Depends(Board board, string[] args)
    {
        var upstream = args.Length >= 3 ? Regex.Match(args[2], UpstreamRef) : Match.Empty;
        if (args.Length != 4 && !(args.Length is 5 or 6 && upstream.Success))
            throw new Die($"usage: board.sh {board.Team} depends <role> <task> <prerequisite> \"<why>\", or <role> <task> <owner/repo#n> <closed|released [<package>]> \"<why>\"");
        var (role, task, prereq) = (args[0], args[1], args[2]);
        CheckDependant(role);
        board.OwnTask(role, task);
        if (upstream.Success)
        {
            DependsUpstream(board, args, upstream.Groups[1].Value, upstream.Groups[2].Value);
            return 0;
        }
        var id = board.Read("api", $"repos/{board.Repo}/issues/{prereq}", "--jq", ".id");
        Console.Error.Write(id.Err);
        Board.Must(board.Write($"block #{task} on #{prereq}", "api", "-X", "POST", $"repos/{board.Repo}/issues/{task}/dependencies/blocked_by", "-F", $"issue_id={id.Said}"));
        DependNote(board, role, task, $"Blocked by #{prereq}: {args[3]}");
        board.Log(role, task, $"blocked by #{prereq}");
        board.Say($"#{task} is now blocked by #{prereq}, and said why on #{task}");
        return 0;
    }

    private static void DependsUpstream(Board board, string[] args, string repo, string upN)
    {
        var (role, task, prereq) = (args[0], args[1], args[2]);
        if (args.Length < 5)
            throw new Die($"say whether #{task} waits for {prereq} to close or to be released: depends {role} {task} {prereq} <closed|released> \"<why>\"");
        var until = args[3];
        var (package, why) = args.Length == 6 ? (args[4], args[5]) : ("", args[4]);
        if (until is not ("closed" or "released")) throw new Die($"#{task} waits for {prereq} to be closed or released, not '{until}'");
        if (package.Length > 0 && until != "released") throw new Die("only a wait until released names a package to upgrade");
        if (package.Length > 0 && !Regex.IsMatch(package, @"^[A-Za-z0-9_.-]+\z")) throw new Die($"'{package}' isn't a package name");
        if (string.Equals(repo, board.Repo, StringComparison.OrdinalIgnoreCase))
            throw new Die($"{prereq} is in {board.Repo} itself: depends {role} {task} {upN} \"<why>\"");
        var read = board.Read("api", $"repos/{repo}/issues/{upN}", "--jq", "{id, pr: (.pull_request != null)}");
        if (!read.Ok)
        {
            var reason = Board.Refusal(read);
            throw new Die($"can't read {prereq} ({(reason.StartsWith("gh: ", StringComparison.Ordinal) ? reason[4..] : reason)})");
        }
        var up = Json.Parse(read.Out);
        var linked = !Json.Truthy(up?["pr"])
            && board.Write($"block #{task} on {prereq}", "api", "-X", "POST", $"repos/{board.Repo}/issues/{task}/dependencies/blocked_by", "-F", $"issue_id={Raw(up?["id"])}").Ok;
        board.Mark(task, "upstream", board.Item(task) is { } it ? WaitsWith(it, prereq) : "");
        var note = $"Blocked by {prereq}, until {until}: {why}";
        if (!linked) note += $"\n\nGitHub can't link {prereq} as a dependency, so the wait is recorded here.";
        DependNote(board, role, task, $"{note}\n\n{WaitMarker("wait", repo, upN, until, package)}");
        board.Log(role, task, $"blocked by {prereq}");
        board.Say($"#{task} now waits for {prereq} to be {until}, and said why on #{task}");
    }

    private static string WaitsWith(JsonNode it, string reference)
    {
        var waits = new JsonArray();
        foreach (var wait in (it["marks"]?["upstream"] as JsonArray ?? []).Select(Json.Text).OfType<string>().Append(reference).Distinct().Order(StringComparer.Ordinal))
            waits.Add((JsonNode?)wait);
        return Json.Compact(waits);
    }

    internal static int Undepend(Board board, string[] args)
    {
        if (args.Length != 4)
            throw new Die($"usage: board.sh {board.Team} undepend <role> <task> <prerequisite> \"<why>\"");
        var (role, task, prereq, why) = (args[0], args[1], args[2], args[3]);
        CheckDependant(role);
        board.OwnTask(role, task);
        var upstream = Regex.Match(prereq, UpstreamRef);
        if (upstream.Success)
        {
            UndependUpstream(board, role, task, prereq, why, upstream.Groups[1].Value, upstream.Groups[2].Value);
            return 0;
        }
        var blocking = BlockedBy(board, task);
        if (!long.TryParse(prereq, out var number))
            throw new Stop(2, "jq: Invalid JSON text passed to --argjson\n");
        var id = FirstId(Board.Must(blocking).Out, row => Json.Long(row?["number"]) == number);
        if (id.Length == 0) throw new Die($"#{task} is not blocked by #{prereq}");
        Board.Must(board.Write($"unblock #{task} from #{prereq}", "api", "-X", "DELETE", $"repos/{board.Repo}/issues/{task}/dependencies/blocked_by/{id}"));
        DependNote(board, role, task, $"No longer blocked by #{prereq}: {why}");
        board.Log(role, task, $"no longer blocked by #{prereq}");
        board.Say($"#{task} is no longer blocked by #{prereq}, and said why on #{task}");
        return 0;
    }

    private static GhResult BlockedBy(Board board, string task) =>
        board.Read("api", $"repos/{board.Repo}/issues/{task}/dependencies/blocked_by");

    private static string FirstId(string said, Func<JsonNode?, bool> wanted)
    {
        var first = (Json.TryParse(said) as JsonArray ?? []).FirstOrDefault(wanted);
        return first?["id"] is { } id ? Raw(id) : "";
    }

    private static void UndependUpstream(Board board, string role, string task, string prereq, string why, string repo, string upN)
    {
        var waits = board.WaitsOnTask(task);
        if (!waits.Any(wait => Json.Text(wait?["ref"]) == prereq)) throw new Die($"#{task} is not waiting on {prereq}");
        var number = long.Parse(upN, CultureInfo.InvariantCulture);
        var id = FirstId(Board.Must(BlockedBy(board, task)).Out, row => Json.Long(row?["number"]) == number
            && (Json.Text(row?["repository_url"]) ?? "").EndsWith("/" + repo, StringComparison.OrdinalIgnoreCase));
        if (id.Length > 0)
            Board.Must(board.Write($"unblock #{task} from {prereq}", "api", "-X", "DELETE", $"repos/{board.Repo}/issues/{task}/dependencies/blocked_by/{id}"));
        DependNote(board, role, task, $"No longer blocked by {prereq}: {why}\n\n{WaitMarker("unwait", repo, upN)}");
        var left = new JsonArray();
        foreach (var wait in waits.Select(wait => Json.Text(wait?["ref"])).Where(reference => reference != prereq))
            left.Add((JsonNode?)wait);
        if (left.Count == 0) board.ClearMark(task, "upstream");
        else board.Mark(task, "upstream", Json.Compact(left));
        board.Log(role, task, $"no longer blocked by {prereq}");
        board.Say($"#{task} no longer waits on {prereq}, and said why on #{task}");
    }

    private static string Day(string at) =>
        LocalDate(DateTimeOffset.Parse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal), "dd MMM").TrimStart('0');

    private static string Upgrade(JsonObject wait)
    {
        var tag = Raw(wait["release"]?["tag"]);
        var version = tag.StartsWith('v') ? tag[1..] : tag;
        return Json.Truthy(wait["package"]) ? $"{Raw(wait["package"])} to {version}" : $"to {version}";
    }

    private static string WaitOver(JsonObject wait)
    {
        var (reference, state) = (Raw(wait["ref"]), Raw(wait["state"]));
        var (tag, at) = (Raw(wait["release"]?["tag"]), Raw(wait["release"]?["at"]));
        if (state == "released" && Json.Truthy(wait["after"]))
            return $"{reference} closed on {Day(Raw(wait["closed"]))}, and {tag} (released {Day(at)}) is the first release since.";
        if (state == "released") return $"{reference} is in {tag} (released {Day(at)}).";
        return $"{reference} has {(state == "merged" ? "merged" : "closed")}.";
    }

    private static string FreedNote(List<JsonObject> waits)
    {
        var versions = waits.Where(wait => Raw(wait["state"]) == "released").Select(Upgrade).ToList();
        var tail = versions.Count == 0 ? "." : $": upgrade {string.Join(" and ", versions)} first.";
        var markers = string.Concat(waits.Select(wait => WaitMarker("unwait", Raw(wait["repo"]), Raw(wait["n"]))));
        return $"{string.Join(" ", waits.Select(WaitOver))} No longer blocked{tail}\n\n{markers}";
    }

    internal static int Free(Board board, string[] args)
    {
        if (args.Length != 0)
            throw new Die($"usage: board.sh {board.Team} free");
        JsonArray? bumps = null;
        var waiting = board.Items(open: true).OfType<JsonNode>().Where(card => Cards.IsIssue(card) && Cards.Marked(card, "upstream")).Select(Cards.N).ToList();
        foreach (var n in waiting)
        {
            var read = board.WaitsOnTask(n);
            if (read.Count == 0) continue;
            var waits = board.UpstreamState(read);
            if (waits.Any(wait => Raw(wait["state"]) == "released" && Json.Truthy(wait["package"])))
            {
                bumps ??= board.OpenBumps();
                Board.WithBumps(waits, bumps);
            }
            if (!waits.All(wait => Json.Truthy(wait["met"]))) continue;
            board.ClearMark(n, "upstream");
            DependNote(board, "dev", n, FreedNote(waits));
            foreach (var wait in waits)
                board.Log("dev", n, $"no longer blocked by {Raw(wait["ref"])}");
            board.Say($"#{n}: no longer waiting on {string.Join(", ", waits.Select(wait => Raw(wait["ref"])))}");
        }
        var gaveUp = board.Items(open: true).OfType<JsonNode>().Where(Cards.GaveUp).ToList();
        if (gaveUp.Count > 0) FreeGivenUp(board, gaveUp);
        return 0;
    }

    /// <summary>Frees each item the dispatcher gave up on that a stakeholder has answered, or that's moved since.</summary>
    private static void FreeGivenUp(Board board, List<JsonNode> held)
    {
        var (page, code, _) = board.GatedTalk(held.Select(Board.Number));
        if (page is null) throw new Stop(code == 0 ? 1 : code, "");
        var said = Board.GatedComments(page);
        foreach (var it in held)
        {
            var hold = it["marks"]!["held"]!;
            var replied = board.RepliedSince(said, Board.Number(it), Raw(hold["at"]));
            if (!replied && Raw(hold["status"]) == Cards.Status(it)) continue;
            var n = Cards.N(it);
            ClearHold(board, n, it);
            board.Log(StatusRoute.Dispatcher, n, replied ? "hold answered" : "hold cleared by a move");
            board.Say($"#{n}: freed, {(replied ? "a stakeholder replied" : "it moved")}");
        }
    }

    /// <summary>What the dispatcher says on an item it stops starting runs for.</summary>
    private static string GaveUpNote(Board board, string role, string n, int runs, string why)
    {
        var finished = board.FinishedRuns(role, n, runs);
        var times = finished.Select(run => Clock(Raw(run["started"]))).ToList();
        var cost = finished.Sum(run => double.TryParse(Raw(run["cost"]), NumberStyles.Float, CultureInfo.InvariantCulture, out var spent) ? spent : 0);
        var when = times.Count == 0 ? "" : $"\nRuns: {string.Join(" · ", times)} UTC (${cost.ToString("0.00", CultureInfo.InvariantCulture)})";
        var last = string.Concat(finished.Select(run => $"\n- {Clock(Raw(run["started"]))}: \"{LastLine(Raw(run["log"]))}\""));
        var lines = last.Length == 0 ? "" : $"\n\nEach run's last line:{last}";
        return $"Gave up after {runs} runs, all started for the same thing.\n\nWhy: {why}{when}{lines}\n\nNothing will start for it until you reply here.\n\n{Board.DispatchMarker}";
    }

    private static string Clock(string at) =>
        DateTimeOffset.Parse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The last line of what a run ended by saying, cut short, and never read as a marker.</summary>
    private static string LastLine(string log)
    {
        if (!File.Exists(log)) return "";
        var said = File.ReadLines(log).Where(line => line.Contains("\"type\":\"result\"", StringComparison.Ordinal)).Select(TryResult).LastOrDefault(text => text is not null) ?? "";
        var last = said.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.Length > 0) ?? "";
        last = last.Replace("<!--", "", StringComparison.Ordinal);
        return last.Length <= LastLineShown ? last : $"{last[..LastLineShown]}…";
    }

    private static string? TryResult(string line)
    {
        try
        {
            return Json.Text(Json.Parse(line)?["result"]);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>The dispatcher's: holds #n, whatever its status, saying what its runs were for and what became of them.</summary>
    internal static int GiveUp(Board board, string[] args)
    {
        if (args.Length != 4 || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var runs))
            throw new Die($"usage: board.sh {board.Team} give-up <role> <n> <runs> <file>");
        var (role, n, file) = (args[0], args[1], args[3]);
        CheckRole(role);
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("A_TEAM_RUN_STARTED"))) throw new Die("only the dispatcher gives up on an item");
        NeedsFile(file);
        var it = Must(board.Item(n), $"#{n} is not on the board");
        if (Cards.Marked(it, "held"))
        {
            board.Say($"#{n}: already held");
            return 0;
        }
        var reasons = File.ReadAllLines(file).Where(HasText).ToList();
        var named = reasons.Where(reason => Regex.IsMatch(reason, $@"#{n}\b")).ToList();
        var why = string.Join(" · ", named.Count > 0 ? named : reasons);
        var body = GaveUpNote(board, role, n, runs, why);
        board.Show(body);
        board.PostComment(n, body);
        board.Mark(n, "held", Json.Compact(new JsonObject { ["at"] = Board.Now(), ["by"] = StatusRoute.Dispatcher, ["status"] = Cards.Status(it), ["why"] = $"gave up after {runs} runs: {why}" }));
        board.Log(StatusRoute.Dispatcher, n, $"held · gave up after {runs} runs");
        board.Say($"#{n}: held, gave up after {runs} runs");
        return 0;
    }

    private static string ParentNow(Board board, string n)
    {
        try
        {
            return board.ParentOf(n);
        }
        catch (Stop failed)
        {
            Console.Error.Write(failed.Message);
            return "";
        }
    }

    /// <summary>A sub-issue the Lead filed as a task that the board left in Idea, unlike an idea, which is marked or isn't the Lead's.</summary>
    private static bool LeftInIdea(JsonNode? card, JsonNode? issue) =>
        card is not null && Cards.Status(card) == "Idea" && !Cards.Pitch(card) && !Cards.Marked(card, "idea")
        && (Json.Text(issue?["body"]) ?? "").Contains("<!-- a-team:lead -->", StringComparison.Ordinal);

    internal static int Unlink(Board board, string[] args)
    {
        if (args.Length != 3)
            throw new Die($"usage: board.sh {board.Team} unlink <role> <parent> <child>");
        var (role, parent, child) = (args[0], args[1], args[2]);
        CheckRole(role);
        if (role != "lead") throw new Die($"{role} may not take a task off a pitch; only lead draws breakdowns");
        var card = board.Item(child);
        var from = card is null ? "" : Cards.Status(card);
        var issue = Json.Parse(Board.Must(board.Read("api", $"repos/{board.Repo}/issues/{child}", "--jq", """{id, title, body: (.body // ""), labels: [.labels[].name]}""")).Out);
        var up = PitchToLeave(board, role, parent);
        var pitchStatus = Cards.Status(up);
        var followup = Idea(issue, from);
        if (followup)
        {
            if (pitchStatus == "Done") throw new Die($"{role} may not change a Done pitch (#{parent})");
        }
        else
        {
            if (from.Length == 0) throw new Die($"#{child} is not on the board");
            if (from != "Ready") throw new Die($"{role} may only take a Ready task off a pitch (#{child} is '{from}')");
            if (pitchStatus != "Building") throw new Die($"{role} may only take a task off a pitch in Building (#{parent} is '{pitchStatus}')");
        }
        if (ParentNow(board, child) != parent) throw new Die($"#{child} is not a sub-issue of #{parent}");
        if (pitchStatus is "Approved" or "Building" && LeftInIdea(card, issue))
            throw new Die($"#{child} is a task left in Idea, not an idea: put it in Ready with: board.sh {board.Team} add lead {child} Ready");
        Board.Must(board.Write($"take #{child} off #{parent}", "api", "-X", "DELETE", $"repos/{board.Repo}/issues/{parent}/sub_issue", "-F", $"sub_issue_id={Raw(issue?["id"])}"));
        board.ForgetParent(child);
        board.Log(role, child, $"taken off #{parent}");
        if (!followup)
        {
            board.Say($"#{child} is no longer a sub-issue of #{parent}");
            return 0;
        }
        SayItGrewOut(board, role, parent, child, issue);
        board.Say($"#{child} is no longer a sub-issue of #{parent}; said so on #{parent}, and #{child} names #{parent} as where it came from");
        return 0;
    }

    private static JsonNode PitchToLeave(Board board, string role, string parent)
    {
        var refusal = $"{role} may only take a task off a pitch (#{parent} is not one)";
        if (!long.TryParse(parent, out _))
        {
            Console.Error.Write("jq: Invalid JSON text passed to --argjson\n");
            throw new Die(refusal);
        }
        return board.Item(parent) is { } up && Cards.Pitch(up) ? up : throw new Die(refusal);
    }

    private static void SayItGrewOut(Board board, string role, string parent, string child, JsonNode? issue)
    {
        var body = $"#{child} ({Raw(issue?["title"])}) grew out of this pitch and is its own item now, so it no longer holds this one up. Its body names #{parent} as where it came from.\n\n<!-- a-team:{role} -->";
        board.Show(body);
        Console.Out.Write(Board.Must(board.Post($"comment on #{parent}", body + "\n", "issue", "comment", parent, "-R", board.Repo, "--body-file", "-")).Out);
        var was = Raw(issue?["body"]);
        if (!Regex.IsMatch(was, $@"Follow-up from #{parent}\b"))
            Board.Must(board.Write($"add 'Follow-up from #{parent}' to #{child}", "issue", "edit", child, "-R", board.Repo, "--body", $"Follow-up from #{parent}\n\n{was}".TrimEnd('\n')));
    }

    /// <summary>#n is held no longer, nor asking for a secret.</summary>
    private static void ClearHold(Board board, string n, JsonNode it)
    {
        board.ClearMark(n, "held");
        if (Cards.Marked(it, "secret")) board.ClearMark(n, "secret");
    }

    internal static int Unblock(Board board, string[] args)
    {
        if (args.Length != 2)
            throw new Die($"usage: board.sh {board.Team} unblock <role> <n>");
        var (role, n) = (args[0], args[1]);
        CheckRole(role);
        if (role is not ("dev" or "lead")) throw new Die("only dev or lead may unblock a task, and only one it held");
        board.OwnTask(role, n);
        var it = Must(board.Item(n), $"#{n} is not on the board");
        if (!Cards.Held(it)) throw new Die($"#{n} isn't a held Ready task");
        var (heldBy, turn) = board.HoldOn(it);
        if ((heldBy ?? "you") == "you")
            throw new Die($"#{n} has no hold from {role}: a stakeholder is holding it, so leave it to them");
        if (heldBy != role)
            throw new Die($"#{n} has no hold from {role}: {heldBy} held it, so leave it to them");
        string what;
        if (role == "lead" && Cards.Marked(it, "upstream")) what = "hold now waits on another repo";
        else if (turn == role) what = role == "dev" ? "question answered" : "hold answered";
        else throw new Die($"no stakeholder has replied to {role}'s ask on #{n} since it was held");
        ClearHold(board, n, it);
        board.Log(role, n, what);
        board.Ack(n);
        board.Say($"#{n}: no longer blocked");
        return 0;
    }

    /// <summary>The role may hold #n, so posts the body on it, marks it held and records what happened.</summary>
    private static void HoldTask(Board board, string role, string n, string body, string what)
    {
        board.OwnTask(role, n);
        var it = Must(board.Item(n), $"#{n} is not on the board");
        var status = Cards.Status(it);
        if (role == "dev")
        {
            if (!(Cards.Marked(it, "dev") && status is "In progress" or "Ready"))
                throw new Die($"dev holds only its own task, In progress or Ready (#{n} is in '{status}')");
        }
        else if (!Cards.ReadyTask(it))
        {
            throw new Die($"only a Ready task can be held (#{n} is in '{status}')");
        }
        if (Cards.Marked(it, "held")) throw new Die($"#{n} is already held");
        var said = role == "you" ? body : $"{body}\n\n<!-- a-team:{role} -->";
        board.Show(said);
        board.PostComment(n, said);
        if (role != "you") board.Ack(n);
        board.Mark(n, "held", Json.Compact(new JsonObject { ["at"] = Board.Now(), ["by"] = role }));
        board.Log(role, n, what);
    }

    internal static int Hold(Board board, string[] args)
    {
        if (args.Length != 3)
            throw new Die($"usage: board.sh {board.Team} hold <role> <n> <file>");
        var (role, n, file) = (args[0], args[1], args[2]);
        if (role is "customer" or "reviewer") throw new Die($"{role} may not hold a task");
        if (role is not ("you" or "dev" or "lead")) throw new Die($"unknown role '{role}' (you | dev | lead)");
        NeedsFile(file);
        if (!HasText(File.ReadAllText(file))) throw new Die($"say why #{n} is held");
        HoldTask(board, role, n, Board.FileText(file), "held");
        board.Say($"#{n}: held");
        return 0;
    }

    internal static int Secret(Board board, string[] args)
    {
        if (args.Length != 5)
            throw new Die($"usage: board.sh {board.Team} secret <role> <n> <NAME> <repo|environment> <file>");
        var (role, n, name, where, file) = (args[0], args[1], args[2], args[3], args[4]);
        if (role is not ("dev" or "lead")) throw new Die($"only the Lead or the Dev asks for a secret, not {role}");
        if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*\z"))
            throw new Die($"'{name}' can't be a secret's name: letters, digits and _ only, not starting with a digit");
        if (name.StartsWith("GITHUB_", StringComparison.OrdinalIgnoreCase))
            throw new Die($"'{name}' can't be a secret's name: GitHub keeps GITHUB_ for itself");
        if (!Regex.IsMatch(where, @"^[A-Za-z0-9_.][A-Za-z0-9_. -]*\z")) throw new Die($"'{where}' can't be an environment's name");
        NeedsFile(file);
        if (!HasText(File.ReadAllText(file))) throw new Die($"say why #{n} needs {name}, and where to get it");
        var (environment, place) = where == "repo" ? ("", $"a repo secret of {board.Repo}") : (where, $"in the {where} environment of {board.Repo}");
        var why = Board.FileText(file);
        HoldTask(board, role, n, $"🔑 This needs `{name}`, {place}.\n\n{why}\n\nGive it in Work, with `k` on this task. Never put its value in a comment: this is public.", $"held · asks for {name}");
        board.Mark(n, "secret", Json.Compact(new JsonObject { ["name"] = name, ["environment"] = environment, ["by"] = role, ["why"] = why }));
        board.Say($"#{n}: held, asking for {name}");
        return 0;
    }

    internal static int Gave(Board board, string[] args)
    {
        if (args.Length != 2)
            throw new Die($"usage: board.sh {board.Team} gave you <n>");
        var (role, n) = (args[0], args[1]);
        if (role != "you") throw new Die("only a stakeholder gives a secret, from the app");
        var it = Must(board.Item(n), $"#{n} is not on the board");
        if (!Cards.Held(it) || !Cards.Marked(it, "secret")) throw new Die($"#{n} isn't asking for a secret");
        var name = Raw(it["marks"]?["secret"]?["name"]);
        ClearHold(board, n, it);
        board.Log("you", n, $"gave {name}");
        board.Say($"#{n}: back in the Dev's queue");
        return 0;
    }

    internal static int Release(Board board, string[] args)
    {
        if (args.Length != 2)
            throw new Die($"usage: board.sh {board.Team} release <role> <n>");
        var (role, n) = (args[0], args[1]);
        if (StatusRoute.Roles.Contains(role)) throw new Die($"{role} may not release a hold this way; an agent clears its own with unblock");
        if (role != "you") throw new Die($"unknown role '{role}' (you)");
        var it = Must(board.Item(n), $"#{n} is not on the board");
        if (!Cards.Held(it)) throw new Die($"#{n} isn't a held Ready task");
        ClearHold(board, n, it);
        board.Log(role, n, "hold released");
        board.Say($"#{n}: hold released");
        return 0;
    }

    private static long? UpdatedAt(string file, string pr, string sha)
    {
        if (!File.Exists(file)) return null;
        foreach (var line in File.ReadAllLines(file))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 3 && fields[0] == pr && fields[1] == sha && long.TryParse(fields[2], out var at)) return at;
        }
        return null;
    }

    /// <summary>GitHub merges in the background, so a PR reads BEHIND for a while after on the head it was updated from.</summary>
    internal static int Update(Board board, string[] args)
    {
        if (args.Length != 0)
            throw new Die($"usage: board.sh {board.Team} update");
        var slug = board.Text("app", "slug");
        if (slug.Length == 0) return 0;
        var file = Path.Combine(Board.State, board.Team, "updated");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var kept = "";
        var prs = Json.Parse(Board.Must(board.Read("pr", "list", "-R", board.Repo, "--author", $"app/{slug}", "--state", "open", "--json", "number,isDraft,mergeStateStatus,headRefOid,baseRefName")).Out)?.AsArray() ?? [];
        foreach (var pr in prs.OfType<JsonNode>().Where(pr => !Json.Truthy(pr["isDraft"]) && Json.Text(pr["mergeStateStatus"]) == "BEHIND"))
        {
            var (p, sha, baseRef) = (Raw(pr["number"]), Raw(pr["headRefOid"]), Raw(pr["baseRefName"]));
            if (UpdatedAt(file, p, sha) is { } at && now - at < UpdateSettles)
            {
                kept += $"{p} {sha} {at}\n";
                continue;
            }
            var merged = board.Write($"merge {baseRef} into PR #{p}", "api", "-X", "PUT", $"repos/{board.Repo}/pulls/{p}/update-branch", "-f", $"expected_head_sha={sha}");
            Console.Error.Write(merged.Err);
            if (merged.Ok)
            {
                kept += $"{p} {sha} {now}\n";
                board.Say($"PR #{p}: merged {baseRef} into it");
            }
            else
            {
                board.Say($"PR #{p}: couldn't merge {baseRef} into it");
            }
        }
        if (!board.DryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, kept);
        }
        return 0;
    }

    /// <summary>A decision's file: its first line with anything on it, then the rest as why.</summary>
    internal static (string Decision, string Why) DecisionIn(string text)
    {
        var lines = text.Split('\n').ToList();
        var at = lines.FindIndex(HasText);
        if (at < 0) return ("", "");
        return (lines[at].Trim(Spaces), string.Join('\n', lines.Skip(at + 1).SkipWhile(line => !HasText(line))).TrimEnd('\n'));
    }

    private static bool HasText(string line) => line.Trim(Spaces).Length > 0;

    private static Row? Entry(Board board, string id) =>
        Digits(id) ? board.DecisionEntry(id) : throw new Die($"'{id}' isn't a decision's id");

    /// <summary>The entry, refused unless it's still standing.</summary>
    private static Row Live(Board board, string id)
    {
        var entry = Entry(board, id) ?? throw new Die($"no decision {id} in {board.Team}'s register");
        if (entry["replacedBy"] is { } newer) throw new Die($"decision {id} was replaced by {newer}");
        if (entry["retired"] is not null) throw new Die($"decision {id} is retired");
        return entry;
    }

    /// <summary>The entry a change would replace. A bad id here also finds no such entry, as `$(live_decision)` said.</summary>
    private static Row Replacing(Board board, string id)
    {
        if (Digits(id)) return Live(board, id);
        Console.Error.WriteLine($"board.sh: '{id}' isn't a decision's id");
        throw new Die($"no decision {id} in {board.Team}'s register");
    }

    /// <summary>Refuses the Lead's change to an entry you changed, and a team's to an all-teams one.</summary>
    private static void Replaceable(Row entry, string who, string scope)
    {
        if (who != "you" && entry["yours"] is not null)
            throw new Die($"you changed decision {entry["id"]} in Decisions, so only your own newer word replaces it");
        if (scope != "all" && entry["scope"] != "team")
            throw new Die($"decision {entry["id"]} is for all teams, so only an all-teams entry replaces it; record this one beside it with its condition");
    }

    private static string WhatItWas(Row entry)
    {
        if (!DateTime.TryParseExact(entry["said"], Board.IsoFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var said)) return "";
        return $"what {(entry["who"] == "you" ? "you" : "the Lead")} said on #{entry["item"]} on {said.ToString("d MMM", CultureInfo.InvariantCulture)}";
    }

    private static (string Item, string Link) Cited(Board board, string where)
    {
        if (Digits(where)) return (where, $"https://github.com/{board.Repo}/issues/{where}");
        var link = Regex.Match(where, $@"^https://github\.com/{Regex.Escape(board.Repo)}/(issues|pull)/([0-9]+)([#?].*)?\z", RegexOptions.Singleline);
        if (!link.Success) throw new Die($"'{where}' isn't an issue number or a link to an issue, PR or comment in {board.Repo}");
        return (link.Groups[2].Value, where);
    }

    internal static int Decide(Board board, string[] args)
    {
        var usage = $"usage: board.sh {board.Team} decide lead <you|lead> <rule|lean> <team|all> <n|url> <file> [--said <date>] [--replaces <id>]";
        if (args.Length < 6) throw new Die(usage);
        var (role, who, kind, scope, where, file) = (args[0], args[1], args[2], args[3], args[4], args[5]);
        var said = Board.Now();
        var old = "";
        for (var at = 6; at < args.Length; at += 2)
        {
            if (args[at] is not ("--said" or "--replaces") || at + 1 == args.Length) throw new Die(usage);
            if (args[at] == "--said") said = args[at + 1];
            else old = args[at + 1];
        }
        if (role is "dev" or "customer" or "reviewer" or "you") throw new Die("only lead records decisions; you correct them from Decisions in the app");
        if (role != "lead") throw new Die($"unknown role '{role}' (lead)");
        if (who is not ("you" or "lead")) throw new Die($"who said it is 'you' or 'lead', not '{who}'");
        if (kind is not ("rule" or "lean")) throw new Die($"a decision is a 'rule' or a 'lean', not '{kind}'");
        if (scope is not ("team" or "all")) throw new Die($"a decision is for this 'team' or 'all' teams, not '{scope}'");
        var (item, link) = Cited(board, where);
        var stamp = Regex.IsMatch(said, @"^[0-9]{4}-[0-9]{2}-[0-9]{2}\z") ? $"{said}T00:00:00Z" : said;
        if (!Regex.IsMatch(stamp, @"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z\z")) throw new Die($"'{said}' isn't a date like 2026-09-26");
        NeedsFile(file);
        var (decision, why) = DecisionIn(File.ReadAllText(file));
        if (decision.Length == 0) throw new Die($"the decision is the first line of {file}, and it's empty");
        Row? replacing = null;
        if (old.Length > 0)
        {
            replacing = Replacing(board, old);
            Replaceable(replacing, who, scope);
        }
        if (board.Would($"record decision '{decision}'{(old.Length > 0 ? $", replacing decision {old}" : "")}")) return 0;
        var id = board.RecordDecision(scope == "all", decision, why, who, kind, item, link, stamp, old);
        board.Log("lead", item, $"recorded decision {id}");
        board.Say(replacing is null ? $"decision {id} recorded" : $"decision {id} recorded; it replaces decision {old}, {WhatItWas(replacing)}");
        return 0;
    }

    internal static int Replace(Board board, string[] args)
    {
        if (args.Length != 3)
            throw new Die($"usage: board.sh {board.Team} replace lead <old> <new>");
        var (role, old, newer) = (args[0], args[1], args[2]);
        if (role != "lead") throw new Die("only lead marks one decision replaced by another");
        if (old == newer) throw new Die("a decision can't replace itself");
        var replacing = Replacing(board, old);
        var by = Replacing(board, newer);
        Replaceable(replacing, by["who"]!, by["scope"]!);
        if (string.CompareOrdinal(by["said"], replacing["said"]) < 0)
            throw new Die($"decision {newer} was said before decision {old}, so the newer one is {old}: replace {newer} with {old}");
        if (board.Would($"mark decision {old} replaced by decision {newer}")) return 0;
        board.ChangeDecisions($"UPDATE decisions SET replaced_by = {newer} WHERE id = {old};");
        board.Say($"decision {old} replaced by decision {newer}");
        return 0;
    }

    internal static int Seeded(Board board, string[] args)
    {
        if (args.Length != 1)
            throw new Die($"usage: board.sh {board.Team} seeded lead");
        if (args[0] != "lead") throw new Die("only lead seeds the decision register");
        if (!board.DryRun)
        {
            var file = Path.Combine(Board.State, board.Team, "lead", "seeded");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, Board.Now() + "\n");
        }
        board.Say("decision register seeded: lead-next won't ask again");
        return 0;
    }

    internal static int Reconfirm(Board board, string[] args)
    {
        if (args.Length < 3)
            throw new Die($"usage: board.sh {board.Team} reconfirm lead <n> <id>...");
        var (role, n, ids) = (args[0], args[1], args[2..]);
        if (role != "lead") throw new Die("only lead reconfirms decisions, as it breaks down a pitch that cited them");
        if (!Digits(n)) throw new Die($"#{n} isn't an item number");
        foreach (var id in ids)
            Live(board, id);
        var named = string.Join(" ", ids);
        if (board.Would($"reconfirm decisions {named} on #{n}")) return 0;
        board.ChangeDecisions($"UPDATE decisions SET confirmed = {Reconciler.Q(Board.Now())}, confirmed_on = {n}\n      WHERE id IN ({string.Join(",", ids)});");
        board.Say($"reconfirmed decisions {named} on #{n}");
        return 0;
    }

    private static (string Set, string Done) Correction(Board board, string[] args, string id, Row entry)
    {
        var change = args[2];
        switch (change)
        {
            case "edit":
                if (args.Length != 4) throw new Die($"usage: board.sh {board.Team} decision you <id> edit <file>");
                NeedsFile(args[3]);
                var wording = File.ReadAllText(args[3]).Split('\n').Where(HasText).ToList();
                if (wording.Count != 1) throw new Die($"a decision is one line: {args[3]} has {Math.Max(wording.Count, 1)}");
                return ($"decision = {Reconciler.Q(wording[0].Trim(Spaces))}", "reworded");
            case "kind":
                if (args.Length != 3) throw new Die($"usage: board.sh {board.Team} decision you <id> kind");
                var kind = entry["kind"] == "rule" ? "lean" : "rule";
                return ($"kind = {Reconciler.Q(kind)}", $"now a {kind}");
            case "widen":
                if (args.Length != 3) throw new Die($"usage: board.sh {board.Team} decision you <id> widen");
                if (entry["scope"] != "all") return ("everyone = 1", "for all teams");
                var team = entry["team"];
                if (team != board.Team) throw new Die($"decision {id} is {team}'s; narrow it to {team} from there");
                return ("everyone = 0", $"for {board.Team} only");
            case "retire":
                if (args.Length != 3) throw new Die($"usage: board.sh {board.Team} decision you <id> retire");
                return entry["retired"] is null ? ($"retired = {Reconciler.Q(Board.Now())}", "retired") : ("retired = NULL", "back in force");
            default:
                throw new Die($"unknown change '{change}' (edit | kind | widen | retire)");
        }
    }

    internal static int Decision(Board board, string[] args)
    {
        if (args.Length < 3)
            throw new Die($"usage: board.sh {board.Team} decision you <id> <edit <file> | kind | widen | retire>");
        var (role, id) = (args[0], args[1]);
        if (StatusRoute.Roles.Contains(role)) throw new Die("only you correct the register, from Decisions in the app");
        if (role != "you") throw new Die($"unknown role '{role}' (you)");
        var entry = Entry(board, id) ?? throw new Die($"no decision {id} in {board.Team}'s register");
        var (set, done) = Correction(board, args, id, entry);
        if (board.Would($"make decision {id} {done}")) return 0;
        board.ChangeDecisions($"UPDATE decisions SET {set}, yours = {Reconciler.Q(Board.Now())} WHERE id = {id};");
        board.Say($"decision {id}: {done}");
        return 0;
    }
}
