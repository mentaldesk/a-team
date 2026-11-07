using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ATeam.Core;

/// <summary>What's said on a card and marked on it short of moving it, a stakeholder's own time and edits, and the team's finished worktrees.</summary>
internal static partial class Commands
{
    private static Dictionary<string, Handler> Writes() => new()
    {
        ["new"] = New,
        ["attach"] = Attach,
        ["pictures"] = Pictures,
        ["checked"] = Checked,
        ["tick"] = Tick,
        ["priority"] = Priority,
        ["comment"] = Comment,
        ["review"] = Review,
        ["skip"] = Skip,
        ["recommend"] = Recommend,
        ["theme"] = Theme,
        ["document"] = Document,
        ["edit"] = Edit,
        ["vision-review"] = VisionReview,
        ["covered"] = Covered,
        ["audited"] = Audited,
        ["spent"] = Spent,
        ["clean"] = Clean,
    };

    private static readonly string[] GateNames = ["Triage", "Pitches", "Blocked", "Review", "Other"];

    /// <summary>Only <paramref name="only"/> does this: anyone else the board knows hears why not, and anyone else isn't known.</summary>
    private static void Alone(string only, string role, string refusal)
    {
        if (role == only) return;
        if (role == "you" || StatusRoute.Roles.Contains(role)) throw new Die(refusal);
        throw new Die($"unknown role '{role}' ({only})");
    }

    private static string RunTaskOr(string n) =>
        Environment.GetEnvironmentVariable("A_TEAM_RUN_TASK") is { Length: > 0 } task ? task : n;

    private static string WithMarker(string file, string role) => $"{File.ReadAllText(file)}\n\n<!-- a-team:{role} -->";

    /// <summary>Posts a comment, passing on what gh prints about it.</summary>
    private static void Commented(Board board, string n, string body) =>
        Console.Out.Write(Board.Must(board.Post($"comment on #{n}", body + "\n", "issue", "comment", n, "-R", board.Repo, "--body-file", "-")).Out);

    private static string OwnFile(Board board, params string[] path) =>
        Path.Combine([Board.State, board.Team, .. path]);

    internal static int New(Board board, string[] args)
    {
        if (args.Length != 2)
            throw new Die($"usage: board.sh {board.Team} new you <file>");
        var (role, file) = (args[0], args[1]);
        Alone("you", role, $"{role} may not open an issue as you; open it yourself and add it");
        NeedsFile(file);
        var text = File.ReadAllText(file);
        var end = text.IndexOf('\n');
        var title = end < 0 ? text : text[..end];
        if (Blank(title)) throw new Die("an issue needs a title (the file's first line)");
        var body = end < 0 ? "" : text[(end + 1)..].TrimEnd('\n');
        board.Show(title);
        board.Show(body);
        var opened = Board.Through(board.Post($"open an issue titled '{title}'", body + "\n", "issue", "create", "-R", board.Repo, "--title", title, "--body-file", "-"));
        if (!opened.Ok) throw new Die($"can't open the issue on {board.Repo}");
        var url = board.DryRun ? "0" : opened.Said;
        Console.Out.Write(url[(url.LastIndexOf('/') + 1)..] + "\n");
        return 0;
    }

    internal static int Attach(Board board, string[] args)
    {
        if (args.Length != 2)
            throw new Die($"usage: board.sh {board.Team} attach you <image>");
        var (role, file) = (args[0], args[1]);
        Alone("you", role, $"{role} may not attach an image; only a stakeholder does, from the app");
        var path = Board.ImagePath(file);
        board.KeepImage(file, path);
        Console.Out.Write(board.AttachmentLink(path) + "\n");
        return 0;
    }

    /// <summary>The Dev's own open task PR, or a refusal in the words of the command asking.</summary>
    private static JsonObject OwnTaskPr(Board board, string role, string pr, string refusal)
    {
        board.OwnTask(role, pr);
        var it = board.TaskPr(pr) ?? throw new Die($"#{pr} isn't a PR");
        if (!Board.DevTaskPr(it)) throw new Die(refusal);
        return it;
    }

    internal static int Pictures(Board board, string[] args)
    {
        if (args.Length < 4 || args.Length % 2 != 0)
            throw new Die($"usage: board.sh {board.Team} pictures dev <pr> <image> <caption> [<image> <caption>...]");
        var (role, pr) = (args[0], args[1]);
        Alone("dev", role, $"{role} may not put pictures on a PR; only dev does, on its own task PR");
        var shots = new List<(string File, string Path, string Caption)>();
        for (var at = 2; at < args.Length; at += 2)
        {
            var (file, caption) = (args[at], args[at + 1]);
            if (Blank(caption)) throw new Die($"{Board.FileName(file)} needs a caption: the acceptance criterion it shows");
            if (caption.Contains('\n')) throw new Die($"a caption is one line: '{caption}'");
            shots.Add((file, Board.ImagePath(file), caption));
        }
        var it = OwnTaskPr(board, role, pr, $"dev puts pictures only on its own open task PRs (#{pr} isn't one)");
        var section = new StringBuilder("## What it looks like");
        foreach (var (file, path, caption) in shots)
        {
            board.KeepImage(file, path);
            section.Append($"\n\n{caption}\n\n![{caption.Replace("[", "").Replace("]", "")}]({board.AttachmentLink(path)})");
        }
        var body = Board.WithSection(Json.Text(it["body"]) ?? "", section.ToString());
        board.Show(body);
        Board.Must(board.Post($"put {shots.Count} picture(s) on #{pr}", body + "\n", "pr", "edit", pr, "-R", board.Repo, "--body-file", "-"));
        board.Log(role, RunTaskOr(pr), $"put {shots.Count} picture(s) on PR #{pr}");
        board.Say($"#{pr}: {shots.Count} picture(s) under What it looks like");
        return 0;
    }

    internal static int Checked(Board board, string[] args)
    {
        if (args.Length < 3)
            throw new Die($"usage: board.sh {board.Team} checked dev <pr> <criterion>=screen|test ...");
        var (role, pr) = (args[0], args[1]);
        Alone("dev", role, $"{role} may not say what the team checked; only dev does, on its own task PR");
        var it = OwnTaskPr(board, role, pr, $"dev says what it checked only on its own open task PRs (#{pr} isn't one)");
        var n = Board.FirstClosed(it) ?? throw new Die($"#{pr} doesn't close an issue");
        var issue = board.IssueBody(n);
        var said = (Json.Text(it["body"]) ?? "").TrimEnd('\n');
        var items = Board.CriteriaItems(issue);
        var pictured = Board.Pictured(said);
        var section = new StringBuilder("## Checked by the team\n");
        var lines = new List<int>();
        var (screens, tests) = (0, 0);
        foreach (var each in args[2..])
        {
            var named = Regex.Match(each, @"^([1-9][0-9]*)=(screen|test)\z");
            if (!named.Success) throw new Die($"'{each}' isn't <criterion>=screen or <criterion>=test");
            var (index, how) = (named.Groups[1].Value, named.Groups[2].Value);
            var one = Board.Nth(items, index) ?? throw new Die($"#{n} has no criterion {index}");
            if (how == "screen")
            {
                if (!pictured.Contains(Board.Norm(one.Text)))
                    throw new Die($"criterion {index} is screen, but no picture on #{pr} is captioned with it: {one.Text}");
                screens++;
            }
            else
            {
                tests++;
            }
            section.Append($"\n- `{how}` {one.Text}");
            lines.Add(one.Line);
        }
        var recorded = Board.RecordedChecks(said);
        var dropped = items.Where(item => recorded.Contains(Board.Norm(item.Text)) && !lines.Contains(item.Line)).Select(item => item.Line).ToList();
        var ticked = Board.Boxed(Board.Boxed(issue, lines, "x"), dropped, " ");
        var body = Board.WithSection(said, section.ToString());
        board.Show(body);
        Board.Must(board.Post($"tick what the team checked on #{n}", ticked + "\n", "issue", "edit", n, "-R", board.Repo, "--body-file", "-"));
        Board.Must(board.Post($"say what the team checked on #{pr}", body + "\n", "pr", "edit", pr, "-R", board.Repo, "--body-file", "-"));
        board.Log(role, RunTaskOr(n), $"checked {screens + tests} of {items.Count}: {screens} on screen, {tests} by test");
        board.Say($"#{n}: {screens + tests} of {items.Count} checked by the team");
        return 0;
    }

    internal static int Tick(Board board, string[] args)
    {
        if (args.Length != 5)
            throw new Die($"usage: board.sh {board.Team} tick you <n> <criterion> <on|off> <its text>");
        var (role, n, index, state, text) = (args[0], args[1], args[2], args[3], args[4]);
        Alone("you", role, $"{role} may not tick a criterion; it's the stakeholders' own check");
        var mark = state switch
        {
            "on" => "x",
            "off" => " ",
            _ => throw new Die($"'{state}' isn't on or off"),
        };
        if (!Regex.IsMatch(index, @"^[1-9][0-9]*\z")) throw new Die($"'{index}' isn't a criterion's number");
        var it = Must(board.Item(n), $"#{n} is not on the board");
        if (Cards.Status(it) != "In review" || Cards.Pitch(it))
            throw new Die($"only a task In review has criteria to tick (#{n} is '{Cards.Status(it)}')");
        var issue = board.IssueBody(n);
        var one = Board.Nth(Board.CriteriaItems(issue), index);
        if (one is null || one.Text != text) throw new Die($"#{n}'s criteria have changed: open it again");
        var ticked = Board.Boxed(issue, [one.Line], mark);
        Board.Must(board.Post($"{(state == "on" ? "tick" : "untick")} criterion {index} on #{n}", ticked + "\n", "issue", "edit", n, "-R", board.Repo, "--body-file", "-"));
        var after = Board.CriteriaItems(ticked);
        var count = $"{after.Count(item => item.Ticked)} of {after.Count}";
        board.Log(role, n, state == "on" ? $"ticked {count}" : $"unticked, {count} ticked");
        board.Say($"#{n}: {count} ticked");
        return 0;
    }

    internal static int Priority(Board board, string[] args)
    {
        if (args.Length != 3)
            throw new Die($"usage: board.sh {board.Team} priority <role> <n> <value|none>");
        var (role, n, value) = (args[0], args[1], args[2]);
        var field = board.PriorityName;
        Alone("you", role, $"{role} may not set a {field}; ranking an item is the stakeholders' own gate");
        if (board.Seek(n) is null) throw new Die($"#{n} is not on the board");
        if (value != "none" && !Cards.Ranks.Contains(value))
            throw new Die($"unknown {field} '{value}' ({string.Join(" | ", Cards.Ranks)} | none)");
        board.SetPriority(n, value);
        board.MirrorPush(n);
        board.Log(role, n, value == "none" ? $"{field} cleared" : $"{field} set to {value}");
        board.Say(value == "none" ? $"#{n}: {field} cleared" : $"#{n}: {field} set to '{value}'");
        return 0;
    }

    internal static int Comment(Board board, string[] args)
    {
        if (args.Length != 3)
            throw new Die($"usage: board.sh {board.Team} comment <role> <n> <file>");
        var (role, n, file) = (args[0], args[1], args[2]);
        if (role == "reviewer")
            throw new Die($"reviewer posts only its review, with: board.sh {board.Team} review reviewer <pr> <file>");
        if (role is not ("lead" or "dev" or "customer" or "you"))
            throw new Die($"unknown role '{role}' (lead | dev | customer | you)");
        NeedsFile(file);
        board.OwnTask(role, n);
        board.OwnDocs(role, n);
        var body = role == "you" ? Board.FileText(file) : WithMarker(file, role);
        board.Show(body);
        Commented(board, n, body);
        if (role != "you") board.Ack(n);
        board.Log(role, n, "commented");
        return 0;
    }

    internal static int Review(Board board, string[] args)
    {
        if (args.Length != 3)
            throw new Die($"usage: board.sh {board.Team} review reviewer <pr> <file>");
        var (role, pr, file) = (args[0], args[1], args[2]);
        CheckRole(role);
        if (role != "reviewer") throw new Die("only reviewer reviews a task's PR");
        if (!board.ReviewerOn) throw new Die($"{board.Team} has no Reviewer (roles.reviewer in {board.Team}.json)");
        NeedsFile(file);
        if (Blank(File.ReadAllText(file))) throw new Die($"the review is empty: {file}");
        var it = OwnTaskPr(board, role, pr, $"reviewer reviews only the Dev's open task PRs (#{pr} isn't one)");
        if (board.Reviewed(it)) throw new Die($"#{pr} has its review already: a PR is reviewed once");
        if (!Json.Truthy(it["isDraft"])) throw new Die($"#{pr} is ready for review already: it went to the stakeholders unreviewed");
        if (!board.Green(pr)) throw new Die($"#{pr} isn't green: it's reviewed once CI passes");
        var body = WithMarker(file, role);
        board.Show(body);
        Console.Out.Write(Board.Must(board.Post($"comment on #{pr}", body + "\n", "pr", "comment", pr, "-R", board.Repo, "--body-file", "-")).Out);
        board.Log(role, RunTaskOr(pr), $"reviewed PR #{pr}");
        board.Say($"#{pr}: reviewed");
        return 0;
    }

    internal static int Skip(Board board, string[] args)
    {
        if (args.Length != 3)
            throw new Die($"usage: board.sh {board.Team} skip <role> <n> <file>");
        var (role, n, file) = (args[0], args[1], args[2]);
        CheckRole(role);
        if (role != "lead") throw new Die("only lead may skip an Idea");
        NeedsFile(file);
        var it = Must(board.Item(n), $"#{n} is not on the board, so it is not an Idea to skip");
        var status = Cards.Status(it);
        if (status != "Idea") throw new Die($"skip is only for Ideas (#{n} is in '{status}')");
        var body = WithMarker(file, role);
        board.Show(body);
        Commented(board, n, body);
        board.Ack(n);
        board.Mark(n, "skipped", "true");
        board.Log(role, n, "skipped");
        board.Say($"#{n}: skipped; a stakeholder's comment there puts it back");
        return 0;
    }

    internal static int Recommend(Board board, string[] args)
    {
        if (args.Length != 4)
            throw new Die($"usage: board.sh {board.Team} recommend lead <n> <urgent|high|medium|low> <file>");
        var (role, n, rank, file) = (args[0], args[1], args[2], args[3]);
        var level = rank.ToLowerInvariant().TrimEnd('\n');
        Alone("lead", role, $"{role} may not recommend a rank; recommending is the Lead's, and ranking is the stakeholders'");
        if (!" urgent high medium low ".Contains($" {level} ", StringComparison.Ordinal))
            throw new Die($"unknown rank '{rank}' (urgent | high | medium | low)");
        NeedsFile(file);
        var said = File.ReadAllText(file).Split('\n').Where(line => !Blank(line)).ToList();
        if (said.Count != 1) throw new Die($"the case for #{n} is one line: {file} has {Math.Max(said.Count, 1)}");
        var it = Must(board.Item(n), $"#{n} is not on the board, so it is not an Idea to recommend");
        if (!Cards.IsIssue(it) || Cards.Status(it) != "Idea")
            throw new Die($"recommend is only for Ideas (#{n} is in '{Cards.Status(it)}')");
        var name = char.ToUpperInvariant(level[0]) + level[1..];
        board.Mark(n, "rank", Json.Compact((JsonNode?)name));
        board.MirrorPush(n);
        var body = $"Recommended: **{name}** · {said[0]}\n\n<!-- a-team:{role} -->";
        board.Show(body);
        Commented(board, n, body);
        board.Log(role, n, $"recommended {name}");
        board.Say($"#{n}: recommended {name}");
        return 0;
    }

    internal static int Theme(Board board, string[] args)
    {
        if (args.Length != 3)
            throw new Die($"usage: board.sh {board.Team} theme lead <n> <heading|none>");
        var (role, n, heading) = (args[0], args[1], args[2]);
        Alone("lead", role, $"{role} may not set a pitch's theme; that's the Lead's");
        if (heading.Length == 0 || heading.Contains(',') || heading.Contains('\n'))
            throw new Die("a theme is its heading from the vision, on one line and with no commas");
        var it = Must(board.Item(n), $"#{n} is not on the board");
        if (!Cards.IsIssue(it) || !Cards.Pitch(it)) throw new Die($"#{n} isn't a pitch: only pitches carry a theme");
        if (heading == "none")
        {
            board.ClearMark(n, "theme");
            board.Log(role, n, "no theme");
            board.Say($"#{n}: no theme");
        }
        else
        {
            board.Mark(n, "theme", Json.Compact((JsonNode?)heading));
            board.Log(role, n, $"theme: {heading}");
            board.Say($"#{n}: theme: {heading}");
        }
        board.MirrorPush(n);
        return 0;
    }

    private static long ArgJson(string n) =>
        long.TryParse(n, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ? number : throw new Stop(2, "jq: Invalid JSON text passed to --argjson\n");

    /// <summary>What `@base64d` makes of a file GitHub sends, however it wraps it.</summary>
    private static string Decoded(string content)
    {
        var text = Regex.Replace(content, @"\s", "");
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(text.PadRight(text.Length + ((4 - (text.Length % 4)) % 4), '=')));
        }
        catch (FormatException)
        {
            throw new Stop(5, $"jq: error: {text} is not valid base64 data\n");
        }
    }

    internal static int Document(Board board, string[] args)
    {
        if (args.Length != 1)
            throw new Die($"usage: board.sh {board.Team} document <n>");
        var n = args[0];
        var head = board.Read("api", $"repos/{board.Repo}/pulls/{n}", "--jq", ".head.ref");
        var listed = head.Ok ? board.Read("api", "--paginate", $"repos/{board.Repo}/pulls/{n}/files", "--jq", ".[].filename") : head;
        if (!listed.Ok) throw new Die($"can't read PR #{n} ({Board.Refusal(listed)})");
        var (reference, files) = (head.Said, listed.Said);
        if (files.Split('\n').Count(line => line.Length > 0) != 1)
        {
            var names = new JsonArray();
            foreach (var name in files.Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries))
                names.Add((JsonNode?)name);
            Console.Out.Write(Json.Pretty(new JsonObject { ["number"] = ArgJson(n), ["files"] = names }) + "\n");
            return 0;
        }
        var read = board.Read("api", $"repos/{board.Repo}/contents/{files}?ref={reference}");
        if (!read.Ok) throw new Die($"can't read {files} on {reference} ({Board.Refusal(read)})");
        var number = ArgJson(n);
        if (Json.Parse(read.Out) is not JsonObject file || Json.Text(file["content"]) is not { } content)
            throw new Stop(5, $"jq: error: {files} on {reference} has no content\n");
        Console.Out.Write(Json.Pretty(new JsonObject
        {
            ["number"] = number,
            ["files"] = new JsonArray((JsonNode?)files),
            ["path"] = files,
            ["ref"] = reference,
            ["sha"] = file["sha"]?.DeepClone(),
            ["text"] = Decoded(content),
        }) + "\n");
        return 0;
    }

    internal static int Edit(Board board, string[] args)
    {
        if (args.Length != 5)
            throw new Die($"usage: board.sh {board.Team} edit you <n> <path> <sha> <file>");
        var (role, n, path, sha, file) = (args[0], args[1], args[2], args[3], args[4]);
        if (role != "you") throw new Die($"{role} may not edit a document pitch; only the stakeholders edit one from the app");
        if (!File.Exists(file)) throw new Die($"no file {file}");
        var it = Must(board.Item(n), $"#{n} is not on the board");
        if (!Cards.IsPr(it) || Cards.Status(it) != "Pitched" || !(Cards.Pitch(it) || Cards.Marked(it, "customer")))
            throw new Die($"#{n} isn't a document pitch in Pitched");
        var reference = Board.Must(board.Read("api", $"repos/{board.Repo}/pulls/{n}", "--jq", ".head.ref")).Said;
        var put = new JsonObject
        {
            ["message"] = $"Edit {path}",
            ["content"] = Convert.ToBase64String(File.ReadAllBytes(file)),
            ["sha"] = sha,
            ["branch"] = reference,
        };
        var refused = board.Post($"commit {file} to {path} on {reference}", Json.Pretty(put) + "\n", "api", "-X", "PUT", $"repos/{board.Repo}/contents/{path}", "--input", "-");
        if (!refused.Ok)
        {
            if (Regex.IsMatch(refused.Err, "HTTP 409|does not match")) throw new Die($"{path} changed on GitHub while you were editing it");
            throw new Die($"can't commit to {path} on {reference} ({Board.Refusal(refused)})");
        }
        board.Log(role, n, $"edited {path}");
        board.Say($"#{n}: committed your edit to {path}");
        return 0;
    }

    internal static int VisionReview(Board board, string[] args)
    {
        if (args.Length != 1)
            throw new Die($"usage: board.sh {board.Team} vision-review you");
        if (args[0] != "you") throw new Die($"{args[0]} may not ask for a vision review; the team's trigger or a stakeholder does");
        if (board.Text("vision").Length == 0) throw new Die($"{board.Team}.json names no vision");
        if (!board.DryRun)
        {
            var asked = OwnFile(board, "lead", "vision-review");
            Directory.CreateDirectory(Path.GetDirectoryName(asked)!);
            if (File.Exists(asked)) File.SetLastWriteTimeUtc(asked, DateTime.UtcNow);
            else File.Create(asked).Dispose();
        }
        board.Say("vision review asked for: the Lead starts one on the next pass");
        return 0;
    }

    internal static int Covered(Board board, string[] args)
    {
        if (args.Length != 2)
            throw new Die($"usage: board.sh {board.Team} covered <role> <pitch>");
        var (role, n) = (args[0], args[1]);
        if (role != "customer") throw new Die("only customer checks the docs against a done pitch");
        if (board.Seek(n) is not { } it || !Cards.Pitch(it) || Cards.Status(it) != "Done")
            throw new Die($"#{n} isn't a done pitch");
        if (!board.DryRun)
        {
            var covered = OwnFile(board, "customer", "covered");
            Directory.CreateDirectory(Path.GetDirectoryName(covered)!);
            if (!File.Exists(covered) || !File.ReadAllLines(covered).Contains(n))
                File.AppendAllText(covered, n + "\n");
        }
        board.Log(role, n, "docs checked");
        board.Say($"#{n}: docs checked");
        return 0;
    }

    internal static int Audited(Board board, string[] args)
    {
        if (args.Length != 1)
            throw new Die($"usage: board.sh {board.Team} audited <role>");
        if (args[0] != "customer") throw new Die("only customer audits the docs");
        if (!board.CustomerOn) throw new Die($"{board.Team} has no Customer lead (roles.customer in {board.Team}.json)");
        if (!board.DryRun)
        {
            var audited = OwnFile(board, "customer", "audited");
            Directory.CreateDirectory(Path.GetDirectoryName(audited)!);
            File.WriteAllText(audited, $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}\n");
        }
        board.Say("docs audited: the next audit is in a week");
        return 0;
    }

    internal static int Spent(Board board, string[] args)
    {
        if (args.Length != 6)
            throw new Die($"usage: board.sh {board.Team} spent you <n|-> <at> <seconds> <gate> <what>");
        var (role, n, at, seconds, gate, what) = (args[0], args[1], args[2], args[3], args[4], args[5]);
        if (role != "you") throw new Die($"only the stakeholders' own time is recorded, not {role}'s");
        if (n != "-" && !Regex.IsMatch(n, @"^[0-9]+\z")) throw new Die($"#{n} isn't an item number");
        if (!Regex.IsMatch(at, @"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z\z")) throw new Die($"'{at}' isn't a UTC time");
        if (!Regex.IsMatch(seconds, @"^[0-9]+\z")) throw new Die($"'{seconds}' isn't a number of seconds");
        if (!GateNames.Contains(gate)) throw new Die($"unknown gate '{gate}'");
        board.RecordSpent(n, at, seconds, gate, what);
        return 0;
    }

    private static string AtHome(string path) =>
        path.StartsWith('~') ? (Environment.GetEnvironmentVariable("HOME") ?? "") + path[1..] : path;

    internal static int Clean(Board board, string[] args)
    {
        if (args.Length != 1)
            throw new Die($"usage: board.sh {board.Team} clean <role>");
        var role = args[0];
        if (role is not ("dev" or "customer")) throw new Die("only dev and customer clean up worktrees");
        var checkout = AtHome(board.Text("checkout"));
        if (!Directory.Exists(checkout))
            throw new Die($"no checkout for {board.Team} at {(checkout.Length > 0 ? checkout : "<unset>")}");
        var cleaned = board.CleanWorktrees(role, checkout, board.Repo);
        if (role == "dev")
        {
            var workdir = AtHome(board.Text("workdir"));
            foreach (var upstream in board.ContributesTo)
            {
                var clone = $"{workdir}/upstream/{upstream[(upstream.IndexOf('/') + 1)..]}";
                if (Directory.Exists(clone)) cleaned += board.CleanWorktrees("dev", clone, upstream);
            }
        }
        if (cleaned == 0) board.Say("No worktrees of yours to clean up.");
        return 0;
    }
}
