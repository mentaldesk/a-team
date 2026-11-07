using System.Globalization;
using System.Text.Json.Nodes;

namespace ATeam.Core;

/// <summary>Filing work, and the stakeholders' gates: what approves, declines, shelves, accepts and closes it.</summary>
internal static partial class Commands
{
    private static Dictionary<string, Handler> Gating() => new()
    {
        ["task"] = Task,
        ["fix"] = Fix,
        ["approve"] = Approve,
        ["decline"] = Decline,
        ["shelve"] = Shelve,
        ["accept"] = Accept,
        ["finish"] = Finish,
    };

    private static bool Blank(string text) => text.All(char.IsWhiteSpace);

    private static void NeedsFile(string file)
    {
        if (!File.Exists(file)) throw new Die($"no such file: {file}");
    }

    private static string Signed(string body, string role) =>
        body.Contains($"<!-- a-team:{role} -->", StringComparison.Ordinal) ? body : $"{body}\n\n<!-- a-team:{role} -->";

    private static string OpenIssue(Board board, string title, string body)
    {
        board.Show($"{title}\n{body}");
        var opened = board.Post($"open an issue titled '{title}'", body + "\n", "issue", "create", "-R", board.Repo, "--title", title, "--body-file", "-");
        if (!opened.Ok) throw new Die($"can't open the issue on {board.Repo}");
        return opened.Said;
    }

    internal static int Task(Board board, string[] args)
    {
        if (args.Length != 4)
            throw new Die($"usage: board.sh {board.Team} task lead <pitch> <title> <file>");
        var (role, pitch, title, file) = (args[0], args[1], args[2], args[3]);
        CheckRole(role);
        if (role != "lead") throw new Die($"{role} may not file a task; only lead breaks down pitches");
        NeedsFile(file);
        if (Blank(title)) throw new Die("a task needs a title");
        var it = Must(board.Item(pitch), $"#{pitch} is not on the board");
        if (!Cards.Fileable(it))
            throw new Die($"lead files tasks only under an Approved or Building pitch (#{pitch} is '{Cards.Status(it)}')");
        var body = Signed(Board.FileText(file), role);
        var url = OpenIssue(board, title, body);
        var n = "new";
        JsonNode? issue = null;
        if (!board.DryRun)
        {
            n = url[(url.LastIndexOf('/') + 1)..];
            issue = Json.Parse(Board.Must(board.Read("api", $"repos/{board.Repo}/issues/{n}", "--jq", "{id, node_id}")).Out);
        }
        Board.Must(board.Write($"make #{n} a sub-issue of #{pitch}", "api", "-X", "POST", $"repos/{board.Repo}/issues/{pitch}/sub_issues",
            "-F", $"sub_issue_id={issue?["id"]?.ToJsonString() ?? "null"}"));
        board.AddToBoard(n, Json.Text(issue?["node_id"]) ?? "null");
        board.SetStatus(n, StatusRoute.None, "Ready", "lead");
        board.Log(role, n, $"filed as Ready under #{pitch}");
        board.Say($"#{n}: filed as Ready under #{pitch}");
        return 0;
    }

    internal static int Fix(Board board, string[] args)
    {
        if (args.Length != 3)
            throw new Die($"usage: board.sh {board.Team} fix lead <title> <file>");
        var (role, title, file) = (args[0], args[1], args[2]);
        CheckRole(role);
        if (role != "lead") throw new Die($"{role} may not file a fix; only lead files one, when the default branch is red");
        NeedsFile(file);
        if (Blank(title)) throw new Die("a fix needs a title");
        var open = board.AllCards().Where(card => Cards.Fix(card) && !Json.Truthy(card["closed"]) && Cards.Status(card) != "Done")
            .Select(card => $"#{Cards.N(card)}").ToList();
        if (open.Count > 0)
            throw new Die($"{string.Join(", ", open)} is already the fix for the default branch: one at a time");
        var main = board.MainRed() ?? throw new Die("the default branch isn't red: its latest completed CI passed, is still running, or there's none");
        var url = OpenIssue(board, title, Signed(Board.FileText(file), role));
        var n = "new";
        var node = "";
        if (!board.DryRun)
        {
            n = url[(url.LastIndexOf('/') + 1)..];
            node = Board.Must(board.Read("api", $"repos/{board.Repo}/issues/{n}", "--jq", ".node_id")).Said;
        }
        board.AddToBoard(n, node);
        board.Mark(n, "fix", "true");
        board.SetStatus(n, StatusRoute.None, "Ready", "lead");
        board.Log(role, n, $"filed as Ready to make {Json.Text(main["branch"])} green");
        Console.Out.Write(n + "\n");
        return 0;
    }

    internal static int Approve(Board board, string[] args)
    {
        if (args.Length is not (2 or 3))
            throw new Die($"usage: board.sh {board.Team} approve <role> <n> [<file>]");
        var (role, n) = (args[0], args[1]);
        var file = args.Length == 3 ? args[2] : "";
        switch (role)
        {
            case "you":
                if (args.Length != 2) throw new Die($"usage: board.sh {board.Team} approve you <n>");
                break;
            case "lead":
                if (args.Length != 3) throw new Die($"usage: board.sh {board.Team} approve lead <n> <file>");
                break;
            case "dev" or "customer" or "reviewer":
                throw new Die($"{role} may not approve a pitch; approving is the stakeholders' own gate, or the Lead's on their word");
            default:
                throw new Die($"unknown role '{role}' (you | lead)");
        }
        if (role == "lead")
        {
            NeedsFile(file);
            if (Blank(File.ReadAllText(file))) throw new Die($"the Lead's approve is its reply: {file} is empty");
        }
        var it = Must(board.Item(n), $"#{n} is not on the board");
        if (!Cards.IsIssue(it)) throw new Die($"#{n} is not an issue, so it is not a pitch to approve");
        if (!Cards.Pitch(it)) throw new Die($"#{n} is not a pitch (no 'pitch' label)");
        var status = Cards.Status(it);
        if (status != "Pitched") throw new Die($"only a Pitched pitch can be approved (#{n} is in '{status}')");
        JsonObject? word = null;
        if (role == "lead")
        {
            word = board.UnansweredFeedback("lead", n).OrderBy(c => Json.Text(c["at"]), StringComparer.Ordinal).LastOrDefault()
                ?? throw new Die($"lead may approve #{n} only on a stakeholder's word (no unanswered stakeholder feedback on #{n})");
            var pitch = Board.Must(board.Read("api", $"repos/{board.Repo}/issues/{n}", "--jq", """.body // "" """)).Out;
            if (NeedsAnswer.In(pitch).Length > 0)
                throw new Die($"#{n} still has questions under Needs your answer: fold the stakeholder's answers into the pitch first, or ask them");
            var body = $"{Board.FileText(file)}\n\n<!-- a-team:lead -->";
            board.Show(body);
            board.PostComment(n, body);
            board.Ack(n);
        }
        board.SetStatus(Cards.N(it), "Pitched", "Approved", role);
        board.Log(role, n, "Pitched → Approved");
        if (word is not null)
        {
            var at = DateTimeOffset.Parse(Json.Text(word["at"])!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            board.Log(role, n, $"approved on {Json.Text(word["author"])}'s word of {LocalDate(at, "dd MMM").TrimStart('0')} {LocalDate(at, "HH:mm")}");
        }
        board.Say($"#{n}: Pitched -> Approved");
        return 0;
    }

    internal static int Decline(Board board, string[] args)
    {
        if (args.Length != 3)
            throw new Die($"usage: board.sh {board.Team} decline <role> <n> <file>");
        var (role, n, file) = (args[0], args[1], args[2]);
        if (StatusRoute.Roles.Contains(role))
            throw new Die($"{role} may not decline an item; declining is the stakeholders' own gate");
        if (role != "you") throw new Die($"unknown role '{role}' (you)");
        NeedsFile(file);
        var reason = File.ReadAllText(file);
        if (Blank(reason)) throw new Die($"a decline needs a reason, so whoever reads #{n} later knows why");
        var it = Must(board.Item(n), $"#{n} is not on the board");
        if (!Cards.IsIssue(it)) throw new Die($"#{n} is not an issue, so it is not an Idea or a pitch to decline");
        var status = Cards.Status(it);
        if (status == "Pitched" && !Cards.Pitch(it)) throw new Die($"#{n} is not a pitch (no 'pitch' label)");
        if (status is not ("Idea" or "Pitched"))
            throw new Die($"only an Idea or a Pitched pitch can be declined (#{n} is in '{status}')");
        board.Show(reason);
        Board.Must(board.Post($"comment on #{n}", reason, "issue", "comment", n, "-R", board.Repo, "--body-file", "-"));
        board.Log(role, n, "commented");
        var closed = board.Write($"close #{n} as not planned", "issue", "close", n, "-R", board.Repo, "--reason", "not planned");
        if (!closed.Ok) throw new Die($"can't close #{n} ({Board.Refusal(closed)})");
        board.SetStatus(Cards.N(it), status, "Done", role);
        board.Log(role, n, "declined");
        board.Say($"#{n}: declined");
        return 0;
    }

    internal static int Shelve(Board board, string[] args)
    {
        if (args.Length != 4)
            throw new Die($"usage: board.sh {board.Team} shelve <role> <n> <weeks> <file>");
        var (role, n, weeks, file) = (args[0], args[1], args[2], args[3]);
        if (role is "dev" or "customer" or "reviewer")
            throw new Die($"{role} may not shelve a pitch; Not now is the stakeholders' own, or the Lead's when they ask");
        if (role is not ("you" or "lead")) throw new Die($"unknown role '{role}' (you | lead)");
        if (!weeks.All(char.IsAsciiDigit) || weeks.Length == 0 || !int.TryParse(weeks, out var count) || count < 1)
            throw new Die($"weeks is a whole number from 1 ('{weeks}' isn't)");
        NeedsFile(file);
        var reason = Board.FileText(file);
        if (role != "you" && Blank(reason)) throw new Die($"the Lead's shelve is its reply: {file} is empty");
        var it = Must(board.Item(n), $"#{n} is not on the board");
        if (!Cards.IsIssue(it)) throw new Die($"#{n} is not an issue, so it is not a pitch to shelve");
        if (!Cards.Pitch(it)) throw new Die($"#{n} is not a pitch (no 'pitch' label)");
        var status = Cards.Status(it);
        if (status != "Pitched") throw new Die($"only a Pitched pitch can be shelved (#{n} is in '{status}')");
        if (role == "lead" && board.UnansweredFeedback("lead", n).Count == 0)
            throw new Die($"lead may shelve #{n} only when a stakeholder has asked (no unanswered stakeholder feedback on #{n})");
        var until = Libc.Local(DateTimeOffset.UtcNow.AddDays(count * 7));
        var back = until.ToString("dd MMM", CultureInfo.InvariantCulture).TrimStart('0');
        var body = $"{(Blank(reason) ? "" : $"{reason}\n\n")}Not now: back on {back}.{(role == "you" ? "" : $"\n\n<!-- a-team:{role} -->")}";
        board.Show(body);
        var posted = Board.Must(board.Post($"comment on #{n}", body + "\n", "api", "-X", "POST", $"repos/{board.Repo}/issues/{n}/comments", "-F", "body=@-", "--jq", ".node_id")).Said;
        if (role == "you")
        {
            if (posted.Length > 0) board.React(posted, $"your Not now on #{n}");
        }
        else
        {
            board.Ack(n);
        }
        board.Mark(n, "shelved", Json.Compact(new JsonObject { ["until"] = until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["priority"] = it["priority"]?.DeepClone() }));
        board.SetStatus(Cards.N(it), "Pitched", "Idea", role);
        board.Log(role, n, "Pitched → Idea");
        board.Log(role, n, $"shelved until {back}");
        board.Say($"#{n}: back on {back}");
        return 0;
    }

    /// <summary>Squash-merges a PR and deletes its branch. With a sha, GitHub refuses it (HTTP 409) if that's no longer the PR's head.</summary>
    private static void MergePr(Board board, string pr, string? sha = null)
    {
        var head = Json.Parse(Board.Must(board.Read("api", $"repos/{board.Repo}/pulls/{pr}", "--jq", """{ref: .head.ref, repo: (.head.repo.full_name // ""), draft: (.draft // false)}""")).Out);
        if (Json.Truthy(head?["draft"]))
        {
            var ready = board.Write($"mark PR #{pr} ready for review", "pr", "ready", pr, "-R", board.Repo);
            if (!ready.Ok) throw new Die($"can't mark PR #{pr} ready to merge it ({Board.Refusal(ready)})");
        }
        var merge = new List<string> { "api", "-X", "PUT", $"repos/{board.Repo}/pulls/{pr}/merge", "-f", "merge_method=squash" };
        if (!string.IsNullOrEmpty(sha))
        {
            merge.Add("-f");
            merge.Add($"sha={sha}");
        }
        var merged = board.Write($"squash-merge PR #{pr}", [.. merge]);
        if (!merged.Ok) throw new Die($"can't merge PR #{pr} ({Board.Refusal(merged)})");
        if (Json.Text(head?["repo"]) == board.Repo)
        {
            var reference = Json.Text(head?["ref"]);
            board.Write($"delete branch {reference}", "api", "-X", "DELETE", $"repos/{board.Repo}/git/refs/heads/{reference}");
        }
    }

    /// <summary>Closes a pitch once all its tasks are closed. <paramref name="built"/> also needs it to have some or, with none,
    /// the merged PRs that built it, which it gives back to name.</summary>
    private static string ClosePitch(Board board, string n, bool built, IReadOnlyList<string> prs)
    {
        var subs = Json.ParseMany(Board.Must(board.Read("api", "--paginate", $"repos/{board.Repo}/issues/{n}/sub_issues")).Out)
            .SelectMany(page => page?.AsArray().ToList() ?? []).ToList();
        var builtBy = "";
        if (built && subs.Count == 0)
        {
            if (prs.Count == 0) throw new Die($"#{n} has no tasks: name the merged PRs that built it");
            foreach (var named in prs)
            {
                var pr = named.StartsWith('#') ? named[1..] : named;
                var merged = "";
                if (pr.Length > 0 && pr.All(char.IsAsciiDigit))
                {
                    var read = board.Read("api", $"repos/{board.Repo}/pulls/{pr}", "--jq", ".merged");
                    merged = read.Said;
                    if (!read.Ok && !read.Err.Contains("HTTP 404", StringComparison.Ordinal))
                        throw new Die($"can't tell whether #{pr} is merged ({Board.Refusal(read)})");
                }
                if (merged != "true") throw new Die($"#{pr} isn't merged, so it can't have built #{n}");
                builtBy += $"{(builtBy.Length > 0 ? ", " : "")}#{pr}";
            }
        }
        var open = subs.Count(sub => Json.Text(sub?["state"]) == "open");
        if (open > 0) throw new Die($"#{n} has {open} open task{(open == 1 ? "" : "s")}");
        var closed = board.Write($"close #{n} as completed", "issue", "close", n, "-R", board.Repo, "--reason", "completed");
        if (!closed.Ok) throw new Die($"can't close #{n} ({Board.Refusal(closed)})");
        return builtBy;
    }

    internal static int Accept(Board board, string[] args)
    {
        if (args.Length != 2)
            throw new Die($"usage: board.sh {board.Team} accept <role> <n>");
        var (role, n) = (args[0], args[1]);
        if (StatusRoute.Roles.Contains(role))
            throw new Die($"{role} may not accept a task; accepting is the stakeholders' own gate");
        if (role != "you") throw new Die($"unknown role '{role}' (you)");
        var it = Must(board.Item(n), $"#{n} is not on the board");
        var status = Cards.Status(it);
        var number = Cards.N(it);
        void Merged(string said)
        {
            MergePr(board, n);
            board.SetStatus(number, status, "Done", role);
            board.Log(role, n, $"accepted · PR #{n} merged");
            board.Say(said);
        }
        if ((Cards.IsPr(it) && Cards.Pitch(it)) || (status == "Pitched" && Cards.Marked(it, "customer")))
        {
            if (status != "Pitched") throw new Die($"only a document pitch in Pitched can be accepted (#{n} is in '{status}')");
            Merged($"#{n}: merged document pitch #{n}");
            return 0;
        }
        if (Cards.IsPr(it) && Cards.Marked(it, "customer"))
        {
            if (status != "In review") throw new Die($"only a docs PR In review, or a docs proposal in Pitched, can be accepted (#{n} is in '{status}')");
            Merged($"#{n}: merged docs PR #{n}");
            return 0;
        }
        if (Cards.IsPr(it))
        {
            if (status != "In review") throw new Die($"only a PR In review can be accepted (#{n} is in '{status}')");
            Merged($"#{n}: merged PR #{n}");
            return 0;
        }
        if (Cards.Pitch(it))
        {
            if (status != "In review") throw new Die($"only a pitch In review can be accepted (#{n} is in '{status}')");
            ClosePitch(board, n, built: false, []);
            board.Log(role, n, "accepted · closed");
            board.Say($"#{n}: closed as done");
            return 0;
        }
        if (status != "In review") throw new Die($"only a task In review can be accepted (#{n} is in '{status}')");
        var (read, pr) = board.PrFor(n);
        Board.Must(read);
        var merging = pr?["number"]?.ToJsonString() ?? throw new Die($"#{n} has no open PR to merge");
        MergePr(board, merging);
        board.Log(role, n, $"accepted · PR #{merging} merged");
        board.Say($"#{n}: merged PR #{merging}");
        return 0;
    }

    internal static int Finish(Board board, string[] args)
    {
        if (args.Length < 3)
            throw new Die($"usage: board.sh {board.Team} finish <role> <n> <file> [<pr>...]");
        var (role, n, file) = (args[0], args[1], args[2]);
        CheckRole(role);
        if (role != "lead") throw new Die("only lead may close a pitch as done");
        NeedsFile(file);
        var it = Must(board.Item(n), $"#{n} is not on the board");
        if (!Cards.Pitch(it)) throw new Die($"#{n} is not a pitch (no 'pitch' label)");
        var status = Cards.Status(it);
        if (status != "Building") throw new Die($"only a pitch in Building can be closed as done (#{n} is in '{status}')");
        var builtBy = ClosePitch(board, n, built: true, args[3..]);
        var body = $"{File.ReadAllText(file)}\n\n<!-- a-team:{role} -->";
        board.Show(body);
        Console.Out.Write(Board.Must(board.Post($"comment on #{n}", body + "\n", "issue", "comment", n, "-R", board.Repo, "--body-file", "-")).Out);
        board.Ack(n);
        board.Log(role, n, $"closed as done{(builtBy.Length > 0 ? $" · built by {builtBy}" : "")}");
        board.Say($"#{n}: closed as done{(builtBy.Length > 0 ? $" (built by {builtBy})" : "")}");
        return 0;
    }
}
