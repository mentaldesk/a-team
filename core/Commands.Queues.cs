using System.Globalization;
using System.Text.Json.Nodes;

namespace ATeam.Core;

/// <summary>What wakes a role and what's waiting on whom: `triggers`, `lead-next`, `waiting`, `overview` and `sync`.</summary>
internal static partial class Commands
{
    private static Dictionary<string, Handler> Queuing() => new()
    {
        ["triggers"] = Triggers,
        ["lead-next"] = LeadNext,
        ["waiting"] = Waiting,
        ["overview"] = Overview,
        ["sync"] = SyncNow,
    };

    /// <summary>What's been found to wake a role.</summary>
    private sealed class Wake(Board board, List<JsonNode> all, bool sweep)
    {
        private readonly HashSet<long> _gaveUp = all.Where(Cards.GaveUp).Select(Board.Number).ToHashSet();

        internal Board Board => board;

        internal List<JsonNode> All => all;

        internal bool Sweep => sweep;

        internal List<string> Reasons { get; } = [];

        internal JsonArray Tasks { get; } = [];

        internal List<string> Chores { get; } = [];

        internal List<long> Items { get; } = [];

        internal List<JsonObject> Recent { get; set; } = [];

        internal string Ready { get; set; } = "";

        internal int ReadyCount { get; set; }

        internal string Card { get; set; } = "";

        internal bool Creative { get; set; }

        /// <summary>A reason the Dev has to start a run on task #n.</summary>
        internal void Task(long n, string title, string why)
        {
            if (_gaveUp.Contains(n)) return;
            Reasons.Add(why);
            var task = Tasks.FirstOrDefault(one => Json.Long(one?["number"]) == n);
            if (task is not null)
                task["reasons"]!.AsArray().Add((JsonNode?)why);
            else
                Tasks.Add((JsonNode?)new JsonObject { ["number"] = n, ["title"] = title, ["reasons"] = new JsonArray((JsonNode?)why) });
        }

        internal void Item(string reason, long n)
        {
            if (_gaveUp.Contains(n)) return;
            Reasons.Add(reason);
            Items.Add(n);
        }
    }

    private static long Long(string n) => long.Parse(n, CultureInfo.InvariantCulture);

    private static int? WipLimit(Board board, string key) =>
        int.TryParse(board.Text("wip", key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit) ? limit : null;

    private static JsonArray Words(IEnumerable<string> words) => Json.ArrayOf(words.Select(word => (JsonNode?)word));

    internal static int Triggers(Board board, string[] args)
    {
        var usage = $"usage: board.sh {board.Team} triggers <role> [--sweep]";
        var role = "";
        var sweep = false;
        foreach (var arg in args)
        {
            if (arg == "--sweep")
            {
                sweep = true;
                continue;
            }
            if (role.Length > 0) throw new Die(usage);
            role = arg;
        }
        if (role.Length == 0) throw new Die(usage);
        CheckRole(role);
        var wake = new Wake(board, board.AllCards(), sweep);
        if (role is "lead" or "dev")
        {
            var (recent, code) = board.RecentComments();
            if (code != 0) throw new Stop(code, "");
            wake.Recent = recent;
        }
        switch (role)
        {
            case "dev": WakeDev(wake); break;
            case "reviewer": WakeReviewer(wake); break;
            case "customer": WakeCustomer(wake); break;
            default: WakeLead(wake); break;
        }
        return SayPretty(Woken(wake, role));
    }

    private static JsonObject Woken(Wake wake, string role)
    {
        var said = new JsonObject { ["reasons"] = Words(wake.Reasons), ["creative"] = wake.Creative };
        if (role is "dev" or "reviewer")
        {
            said["tasks"] = wake.Tasks;
            said["ready"] = wake.Ready.Length == 0 ? null : Long(wake.Ready);
            said["readyCount"] = wake.ReadyCount;
            said["chores"] = Words(wake.Chores);
            return said;
        }
        var items = new JsonArray();
        foreach (var n in wake.Items)
            items.Add((JsonNode?)n);
        said["items"] = items;
        said["card"] = wake.Items.Count == 1 ? wake.Items[0] : wake.Card.Length > 0 ? Long(wake.Card) : null;
        return said;
    }

    private static void WakeDev(Wake wake)
    {
        var all = wake.All;
        var fixReady = all.Any(card => Cards.Startable(card) && Cards.Fix(card));
        var main = new Lazy<JsonObject?>(wake.Board.MainRed);
        foreach (var card in all.Where(card => Cards.Marked(card, "dev") && Cards.Status(card) is "In progress" or "In review" && !Cards.GaveUp(card)))
            WakeOnTask(wake, card, fixReady, main);
        WakeOnHeld(wake);
        WakeOnUpstream(wake);
        WakeOnMerged(wake);
        WakeOnReady(wake);
        wake.Reasons.AddRange(wake.Chores);
    }

    private static void WakeOnTask(Wake wake, JsonNode card, bool fixReady, Lazy<JsonObject?> main)
    {
        var board = wake.Board;
        var n = Cards.N(card);
        var title = Board.Raw(card["title"]);
        var number = Long(n);
        var what = "";
        JsonObject? pr;
        if (Cards.Dependabot(card))
        {
            if (fixReady) return;
            pr = board.BumpView(n);
            what = $"Dependabot's PR #{n}";
        }
        else
        {
            var (read, found) = board.PrFor(n);
            Board.Must(read);
            pr = found;
        }
        var numbers = new List<string> { n };
        if (pr is not null)
        {
            var p = Board.Raw(pr["number"]);
            if (p != n) numbers.Add(p);
            wake.Recent.AddRange(board.PrReviews(p));
            WakeOnPr(wake, card, pr, p, what.Length > 0 ? what : $"PR #{p}", main);
        }
        else if (Cards.Status(card) == "In progress")
        {
            wake.Task(number, title, $"#{n} is In progress but has no PR: an earlier run didn't finish");
        }
        foreach (var x in numbers)
        {
            var at = board.FeedbackAt(wake.Recent, "dev", x, wake.Sweep);
            if (at.Length > 0) wake.Task(number, title, $"stakeholder feedback on #{x} ({at})");
        }
    }

    private static void WakeOnPr(Wake wake, JsonNode card, JsonObject pr, string p, string what, Lazy<JsonObject?> main)
    {
        var board = wake.Board;
        var number = Long(Cards.N(card));
        var title = Board.Raw(card["title"]);
        var checks = board.HeadChecks(p);
        var verdict = Json.Text(checks["verdict"])!;
        if (verdict == "fail" && !Cards.Fix(card))
            verdict = Board.AgainstMain(checks, main.Value);
        var running = checks["pending"]!.AsArray().Count == 0 ? "" : ", other checks still running";
        if (verdict == "fail")
            wake.Task(number, title, $"CI failed on {what} at {board.ShortHead(p)}{running}");
        if (verdict == "pass" && pr["isDraft"]?.GetValueKind() == System.Text.Json.JsonValueKind.True)
        {
            switch (Json.Text(pr["review"]))
            {
                case "off": wake.Task(number, title, $"PR #{p} is green but still a draft"); break;
                case "posted": wake.Task(number, title, $"PR #{p} has its review: act on it"); break;
                case "answered": wake.Task(number, title, $"PR #{p} has its review answered and is green: mark it ready"); break;
            }
        }
        if (Json.Text(pr["mergeable"]) == "CONFLICTING")
            wake.Task(number, title, $"PR #{p} conflicts with its base at {board.ShortHead(p)}: merge the base branch into it and resolve");
    }

    private static void WakeOnHeld(Wake wake)
    {
        var held = wake.All.Where(Cards.Held).ToList();
        if (held.Count == 0) return;
        var said = HeldTalk(wake.Board, held);
        foreach (var question in wake.Board.Questions(held, said, Board.GatedBlocked(held)))
        {
            var unread = Json.Text(question["unread"]) ?? "";
            if (unread.Length > 0)
                wake.Task(Json.Long(question["number"]), Board.Tsv(Board.Raw(question["title"])), $"stakeholder answered the Dev's question on #{Board.Raw(question["number"])} ({unread})");
        }
    }

    private static List<JsonObject> HeldTalk(Board board, List<JsonNode> held)
    {
        var (page, code, _) = board.GatedTalk(held.Select(Board.Number));
        if (page is null) throw new Stop(code, "");
        return Board.GatedComments(page);
    }

    private static void WakeOnUpstream(Wake wake)
    {
        if (wake.Board.Text("app", "slug").Length == 0) return;
        foreach (var upstream in wake.Board.ContributesTo)
        {
            foreach (var (n, why) in wake.Board.UpstreamWakes(upstream, wake.All))
                wake.Task(n, Board.Raw(wake.All.FirstOrDefault(card => Board.Number(card) == n)?["title"]), Board.Tsv(why));
        }
    }

    private static void WakeOnMerged(Wake wake)
    {
        var checkout = wake.Board.Text("checkout");
        if (checkout.StartsWith('~')) checkout = Environment.GetEnvironmentVariable("HOME") + checkout[1..];
        if (!Directory.Exists(checkout)) return;
        foreach (var branch in Board.WorktreeBranches(checkout))
        {
            var merged = wake.Board.MergedFrom(branch);
            if (merged.Length > 0) wake.Chores.Add($"PR #{merged} has merged: clean up its worktree");
        }
    }

    private static void WakeOnReady(Wake wake)
    {
        var used = wake.All.Count(Cards.Working);
        var limit = WipLimit(wake.Board, "worktrees");
        var fastTrack = (limit ?? 0) + 1;
        var startable = wake.All.Where(Cards.Startable).ToList();
        wake.ReadyCount = startable.Count;
        var fix = startable.FirstOrDefault(Cards.Fix);
        if (fix is not null && used < fastTrack)
        {
            wake.Ready = Cards.N(fix);
            wake.Reasons.Add($"Ready fix task #{wake.Ready} for the red default branch: take it first");
        }
        else if (startable.Count > 0)
        {
            if (used < limit)
            {
                wake.Ready = Cards.N(startable[0]);
                wake.Reasons.Add($"Ready task available (e.g. #{wake.Ready}) and a free worktree");
            }
            else if (used < fastTrack && Cards.ByPriority(startable).FirstOrDefault(card => Json.Text(card["priority"]) == "Urgent") is { } urgent)
            {
                wake.Ready = Cards.N(urgent);
                wake.Reasons.Add($"Ready task available (e.g. #{wake.Ready}, Urgent) and a fast-track worktree ({used} in use, {fastTrack} allowed while an Urgent task is Ready)");
            }
        }
    }

    private static void WakeReviewer(Wake wake)
    {
        var board = wake.Board;
        if (!board.ReviewerOn) return;
        foreach (var card in wake.All.Where(card => Cards.Marked(card, "dev") && !Cards.Dependabot(card) && Cards.Status(card) is "In progress" or "In review" && !Cards.GaveUp(card)))
        {
            var n = Cards.N(card);
            var (read, pr) = board.PrFor(n);
            Board.Must(read);
            if (pr is null || pr["isDraft"]?.GetValueKind() != System.Text.Json.JsonValueKind.True || Json.Text(pr["review"]) != "waiting") continue;
            var p = Board.Raw(pr["number"]);
            if (board.HeadVerdict(p) == "pass")
                wake.Task(Long(n), Board.Raw(card["title"]), $"PR #{p} is green and waiting for its review");
        }
    }

    private static void WakeCustomer(Wake wake)
    {
        var board = wake.Board;
        if (!board.CustomerOn || board.Text("docs").Length == 0) return;
        var accepted = wake.All.Where(card => Cards.Pitch(card) && Cards.Status(card) == "Done" && Json.Text(card["closed"]) == "COMPLETED").Select(Cards.N).ToList();
        board.StartCovering(accepted);
        var docs = wake.All.Where(card => Cards.Marked(card, "customer") && Cards.Status(card) is "In review" or "Pitched")
            .OrderBy(card => Cards.Status(card) != "In review").FirstOrDefault();
        var status = docs is null ? "" : Cards.Status(docs);
        var n = docs is null ? "" : Cards.N(docs);
        var kind = "";
        var there = board.DocsThere();
        if (there == 1)
        {
            board.ForgetAudit();
            if (status == "Pitched") kind = "docs proposal";
            else wake.Reasons.Add("no user docs yet: propose where they'll live");
        }
        else if (there == 0)
        {
            var covered = board.Covered;
            wake.Reasons.AddRange(accepted.Where(p => !covered.Contains(p)).Select(p => $"pitch #{p} is done: check the docs cover what it shipped"));
            if (board.AuditDue) wake.Reasons.Add("weekly docs audit: check the docs as a whole");
            if (status == "In review") kind = "docs PR";
        }
        if (kind.Length == 0) return;
        wake.Card = n;
        var recent = board.RecentComments().Comments;
        recent.AddRange(board.PrReviews(n));
        var at = board.FeedbackAt(recent, "customer", n, wake.Sweep);
        if (at.Length > 0) wake.Item($"stakeholder feedback on {kind} #{n} ({at})", Long(n));
    }

    private static void WakeLead(Wake wake)
    {
        foreach (var pitch in wake.All.Where(card => Cards.Pitch(card) && Cards.Status(card) is not ("Idea" or "Done") && !Cards.GaveUp(card)))
            WakeOnPitch(wake, pitch);
        WakeOnHolds(wake);
        WakeOnUpdates(wake);
        var due = wake.Board.VisionDue(wake.Sweep);
        if (due.Length > 0) wake.Reasons.Add(due);
        WakeOnRedMain(wake);
        WakeOnPitching(wake);
    }

    private static void WakeOnPitch(Wake wake, JsonNode pitch)
    {
        var board = wake.Board;
        var n = Cards.N(pitch);
        var status = Cards.Status(pitch);
        var had = wake.Reasons.Count;
        var at = board.FeedbackAt(wake.Recent, "lead", n, wake.Sweep);
        if (at.Length > 0) wake.Reasons.Add($"stakeholder feedback on #{n} ({at})");
        if (status == "Approved") wake.Reasons.Add($"#{n} was approved: break it down");
        if (status == "Building" && board.SubIssues(n) is { Total: > 0, Open: 0 })
            wake.Reasons.Add($"all of #{n}'s tasks are closed: check it and close it");
        if (wake.Reasons.Count != had) wake.Items.Add(Long(n));
    }

    private static void WakeOnHolds(Wake wake)
    {
        var board = wake.Board;
        var held = wake.All.Where(Cards.Held).ToList();
        if (held.Count > 0)
        {
            var mine = board.Holds(held, HeldTalk(board, held), Board.GatedBlocked(held)).Where(hold => Json.Text(hold["heldBy"]) == "lead").ToList();
            foreach (var hold in mine)
            {
                var unread = Json.Text(hold["unread"]) ?? "";
                if (unread.Length > 0)
                    wake.Item($"stakeholder answered the Lead's hold on #{Board.Raw(hold["number"])} ({unread})", Json.Long(hold["number"]));
            }
            if (!board.HoldsReviewed && mine.Count > 0)
                wake.Reasons.Add($"tasks you held before holds cleared themselves ({string.Join(", ", mine.Select(hold => $"#{Board.Raw(hold["number"])}"))}): make those waiting on another repo dependencies");
        }
        board.HoldsNowReviewed();
    }

    private static void WakeOnUpdates(Wake wake)
    {
        var slug = wake.Board.Text("app", "slug");
        foreach (var update in wake.Board.DependabotUpdates().OfType<JsonNode>())
        {
            var answered = (update["comments"] as JsonArray ?? []).Any(comment => Json.Text(comment?["author"]?["login"]) == slug
                && (Json.Text(comment?["body"]) ?? "").Contains("<!-- a-team:lead -->", StringComparison.Ordinal));
            if (!answered)
                wake.Item($"Dependabot opened PR #{Board.Raw(update["number"])} ({Board.Tsv(Board.Raw(update["title"]))}): see what the update brings", Json.Long(update["number"]));
        }
    }

    private static void WakeOnRedMain(Wake wake)
    {
        if (wake.All.Any(card => Cards.Fix(card) && card["closed"] is null && Cards.Status(card) != "Done")) return;
        if (wake.Board.MainRed() is not { } main) return;
        var since = Json.Text(main["since"]) ?? "";
        var failing = string.Join(", ", main["failing"]!.AsArray().Select(Board.Raw));
        wake.Reasons.Add($"{Json.Text(main["branch"])} is red since {(since.Length > 7 ? since[..7] : since)}: {failing} failing. File a fix");
    }

    private static void WakeOnPitching(Wake wake)
    {
        var (board, all) = (wake.Board, wake.All);
        var pitched = all.Count(card => Cards.Pitch(card) && Cards.Status(card) == "Pitched");
        var exploring = all.Count(card => Cards.Pitch(card) && Cards.Status(card) == "Exploring");
        var found = all.Count(card => Cards.Marked(card, "idea") && Cards.Status(card) == "Idea");
        var ideas = all.Count(card => Cards.Status(card) == "Idea" && Cards.IsIssue(card));
        var limit = WipLimit(board, "pitched");
        if (exploring > 0)
        {
            if (pitched < limit)
                wake.Reasons.Add("room in Pitched for a drafted pitch");
            else if (limit is not { } full)
                Console.Error.WriteLine("jq: Invalid JSON text passed to --argjson");
            else if (Cards.PitchSwap(all, full).Demote.Count > 0)
                wake.Reasons.Add("a drafted pitch out-ranks one in Pitched: swap them");
        }
        if (pitched == 0 && exploring == 0 && board.PitchableIdea(all) is { } idea)
            wake.Reasons.Add($"nothing pitched or being drafted: pitch an Idea (e.g. #{Board.Raw(idea["number"])})");
        wake.Creative = (exploring < WipLimit(board, "exploring") && ideas > 0) || found < WipLimit(board, "ideas");
    }

    internal static int LeadNext(Board board, string[] args)
    {
        var all = board.AllCards();
        var pitched = all.Count(card => Cards.Pitch(card) && Cards.Status(card) == "Pitched");
        var exploring = all.Count(card => Cards.Pitch(card) && Cards.Status(card) == "Exploring");
        var found = all.Count(card => Cards.Marked(card, "idea") && Cards.Status(card) == "Idea");
        var skipped = all.Count(card => Cards.Marked(card, "skipped") && Cards.Status(card) == "Idea");
        var ready = all.Count(Cards.Startable);
        var blocked = all.Count(card => Cards.ReadyTask(card) && Cards.Blocked(card));
        var limit = WipLimit(board, "pitched") ?? throw new Stop(2, "jq: Invalid JSON text passed to --argjson\n");
        var (promote, demote) = Cards.PitchSwap(all, limit);
        var idle = pitched + exploring == 0;
        exploring += demote.Count - promote.Count;
        var idea = board.PitchableIdea(all);
        var canPitch = exploring < WipLimit(board, "exploring") && idea is not null;
        var ideas = WipLimit(board, "ideas");
        var canDiscover = found < ideas;
        var turn = canPitch && (board.LeadTurnWas == "discover" || !canDiscover || idle) ? "pitch" : canDiscover ? "discover" : "none";
        if (turn != "none") board.LeadTurnIs(turn);
        if (turn == "pitch") board.ShowRunOn(idea!);
        var said = new JsonObject { ["promote"] = Announced(promote), ["demote"] = Announced(demote), ["turn"] = turn };
        if (turn == "pitch") said["item"] = idea;
        else if (turn == "discover") said["room"] = (ideas ?? 0) - found;
        else said["reason"] = "Exploring and the discovery queue are both full, or there is no Idea to pitch";
        var floor = double.TryParse(board.Text("wip", "readyFloor"), NumberStyles.Float, CultureInfo.InvariantCulture, out var low) ? low : 0;
        said["ready"] = ready;
        said["blocked"] = blocked;
        said["readyLow"] = ready < floor;
        said["skipped"] = skipped;
        said["unthemed"] = board.Unthemed(all);
        said["seed"] = !board.Seeded;
        return SayPretty(said);
    }

    private static JsonArray Announced(List<JsonNode> cards)
    {
        var announced = new JsonArray();
        foreach (var card in cards)
        {
            var shown = card.DeepClone().AsObject();
            shown["announce"] = !Cards.Marked(card, "displaced");
            announced.Add((JsonNode?)shown);
        }
        return announced;
    }

    internal static int Waiting(Board board, string[] args)
    {
        if (args.Length != 0)
            throw new Die($"usage: board.sh {board.Team} waiting");
        board.Sync(cached: true);
        var all = board.OpenCards();
        var stored = board.DetailForReasons(all);
        return SayPretty(board.WaitingOn(all, stored));
    }

    internal static int SyncNow(Board board, string[] args)
    {
        if (args.Length != 0)
            throw new Die($"usage: board.sh {board.Team} sync");
        board.Sync();
        board.SyncDetail();
        return 0;
    }

    internal static int Overview(Board board, string[] args)
    {
        if (args.Length != 0)
            throw new Die($"usage: board.sh {board.Team} overview");
        var last = board.LastRuns();
        board.Sync(cached: true);
        var cards = new JsonArray();
        foreach (var card in board.OpenCards())
            cards.Add((JsonNode?)OverviewOf(card, board.Team, last));
        if (board.MirrorLag() is { } lag)
            cards.Add((JsonNode?)new JsonObject { ["team"] = board.Team, ["mirror"] = lag });
        return SayPretty(cards);
    }

    private static string KindOf(JsonNode card) =>
        Cards.Pitch(card) ? "pitch"
        : Cards.Marked(card, "customer") ? "docs"
        : Cards.Marked(card, "dev") || card["parent"] is not null ? "task"
        : "yours";

    private static JsonObject OverviewOf(JsonNode card, string team, Dictionary<long, JsonNode> last)
    {
        var run = last.GetValueOrDefault(Board.Number(card));
        var shown = new JsonObject
        {
            ["number"] = card["number"]?.DeepClone(),
            ["title"] = card["title"]?.DeepClone(),
            ["url"] = card["url"]?.DeepClone(),
            ["status"] = card["status"]?.DeepClone(),
            ["priority"] = card["priority"]?.DeepClone(),
            ["since"] = card["since"]?.DeepClone(),
            ["parent"] = card["parent"]?.DeepClone(),
            ["team"] = team,
            ["blocked"] = Cards.Blocked(card),
            ["kind"] = KindOf(card),
            ["lastRun"] = run is null ? null : new JsonObject { ["role"] = run["role"]?.DeepClone(), ["log"] = run["log"]?.DeepClone(), ["started"] = run["started"]?.DeepClone() },
        };
        if (Board.Facts(card) is { Length: > 0 } facts) shown["facts"] = facts;
        return shown;
    }
}
