using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ATeam.Core;

/// <summary>The PRs that reach the board or move on it by themselves: Dependabot's, the Dev's on other repos, and finished tasks'.</summary>
internal static partial class Commands
{
    private static Dictionary<string, Handler> Arriving() => new()
    {
        ["bumps"] = Bumps,
        ["adopt"] = Adopt,
        ["finished"] = Finished,
    };

    private const int RefusalsBeforeAsking = 3;

    private static bool Merges(JsonNode pr, string slug) =>
        Json.Text(pr["author"]?["login"]) == "app/dependabot" && !Json.Truthy(pr["isDraft"]) && Json.Text(pr["mergeable"]) == "MERGEABLE"
        && (pr["comments"]?.AsArray().Any(comment => Json.Text(comment?["author"]?["login"]) == slug
            && (Json.Text(comment?["body"]) ?? "").Contains("<!-- a-team:lead -->", StringComparison.Ordinal)) ?? false);

    /// <summary>Counts a refusal of a green bump. Enough in a row hold it, with the Dev's marker, so it reads as any bump the
    /// Dev can't make build. Passes go on trying, so fixing the cause merges it.</summary>
    private static void RefusedBump(Board board, string pr, JsonNode? it, string node, string why)
    {
        if (it is not null && Cards.Marked(it, "held")) return;
        var file = Path.Combine(Board.State, board.Team, "bump-refusals", pr);
        var count = (File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out var counted) ? counted : 0) + 1;
        if (count < RefusalsBeforeAsking)
        {
            if (!board.DryRun)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, $"{count}\n");
            }
            return;
        }
        Board.Must(board.Post($"comment on #{pr}", $"""
            This bump is green, but GitHub won't merge it: {why}

            The dispatcher goes on trying, so changing what's stopping it merges it. Or merge it yourself, close it, or reply here and the Dev picks it up.

            <!-- a-team:dev -->

            """, "issue", "comment", pr, "-R", board.Repo, "--body-file", "-"));
        if (it is null) board.AddToBoard(pr, node);
        board.Mark(pr, "dev", "true");
        board.SetStatus(pr, it is null ? StatusRoute.None : Cards.Status(it), "Ready", StatusRoute.Dispatcher);
        board.Mark(pr, "held", Json.Compact(new JsonObject { ["at"] = Board.Now(), ["by"] = "dev" }));
        board.Log(StatusRoute.Dispatcher, pr, "held");
        if (!board.DryRun && File.Exists(file)) File.Delete(file);
        board.Say($"PR #{pr}: GitHub keeps refusing to merge it, asked the stakeholder");
    }

    internal static int Bumps(Board board, string[] args)
    {
        if (args.Length != 0)
            throw new Die($"usage: board.sh {board.Team} bumps");
        var listed = Board.Must(board.Read("pr", "list", "-R", board.Repo, "--author", "app/dependabot", "--state", "open",
            "--json", "id,number,title,author,isDraft,mergeable,comments,headRefOid"));
        var prs = Json.Parse(listed.Out)?.AsArray() ?? [];
        if (prs.Count == 0) return 0;
        var all = board.AllCards();
        var slug = board.Text("app", "slug");
        JsonObject? main = null;
        var mainRead = false;
        var stuck = 0;
        foreach (var row in prs.OfType<JsonNode>())
        {
            var p = row["number"]!.ToJsonString();
            var title = Json.Text(row["title"]);
            var sha = Json.Text(row["headRefOid"]) ?? "";
            var checks = board.PrChecks(sha);
            var verdict = Json.Text(checks["verdict"]);
            if (verdict == "fail")
            {
                if (!mainRead)
                {
                    main = board.MainRed();
                    mainRead = true;
                }
                verdict = Board.AgainstMain(checks, main);
            }
            var it = all.FirstOrDefault(card => Cards.N(card) == p);
            if (verdict == "pass" && Merges(row, slug))
            {
                string? why = null;
                board.Quiet = true;
                try
                {
                    MergePr(board, p, sha);
                }
                catch (Die refusal)
                {
                    why = $"board.sh: {refusal.Message}";
                }
                catch (Stop stopped)
                {
                    why = stopped.Message;
                }
                board.Quiet = false;
                if (why is not null)
                {
                    if (why.Contains("HTTP 409", StringComparison.Ordinal)) continue;
                    var said = why.StartsWith("board.sh: ", StringComparison.Ordinal) ? why["board.sh: ".Length..] : why;
                    Console.Error.WriteLine($"PR #{p}: {said}");
                    stuck = 1;
                    RefusedBump(board, p, it, Json.Text(row["id"]) ?? "", said);
                    continue;
                }
                var refusals = Path.Combine(Board.State, board.Team, "bump-refusals", p);
                if (!board.DryRun && File.Exists(refusals)) File.Delete(refusals);
                if (it is not null) board.SetStatus(p, Cards.Status(it), "Done", StatusRoute.Dispatcher);
                board.Log(StatusRoute.Dispatcher, p, $"merged · PR #{p}");
                board.Say($"PR #{p} ({title}): merged");
            }
            else if (verdict == "fail" && !(it is not null && (Cards.Marked(it, "held") || Cards.Status(it) == "In progress")))
            {
                string moved;
                if (it is not null)
                {
                    moved = $"{Cards.Status(it)} → In progress";
                }
                else
                {
                    board.AddToBoard(p, Json.Text(row["id"]) ?? "");
                    moved = "added as In progress";
                }
                board.Mark(p, "dev", "true");
                board.SetStatus(p, it is null ? StatusRoute.None : Cards.Status(it), "In progress", StatusRoute.Dispatcher);
                board.Log(StatusRoute.Dispatcher, p, moved);
                board.Say($"PR #{p} ({title}): CI failing, with the Dev");
            }
        }
        return stuck;
    }

    internal static int Adopt(Board board, string[] args)
    {
        if (args.Length != 0)
            throw new Die($"usage: board.sh {board.Team} adopt");
        var slug = board.Text("app", "slug");
        if (slug.Length == 0) return 0;
        var listed = Board.Must(board.Read("pr", "list", "-R", board.Repo, "--author", $"app/{slug}", "--state", "open", "--json", "id,number,isDraft,body"));
        var prs = (Json.Parse(listed.Out)?.AsArray() ?? []).OfType<JsonNode>().Where(pr =>
        {
            var body = Json.Text(pr["body"]) ?? "";
            return body.Contains("<!-- a-team:dev -->", StringComparison.Ordinal)
                && Regex.Matches(body, "needed by ([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)#[0-9]+", RegexOptions.IgnoreCase)
                    .Any(match => !string.Equals(match.Groups[1].Value, board.Repo, StringComparison.OrdinalIgnoreCase));
        }).ToList();
        if (prs.Count == 0) return 0;
        var here = board.AllCards().ToDictionary(Cards.N, Cards.Status);
        foreach (var pr in prs)
        {
            var n = pr["number"]!.ToJsonString();
            var draft = Json.Truthy(pr["isDraft"]);
            if (!here.TryGetValue(n, out var status))
            {
                var to = draft ? "In progress" : "In review";
                board.AddToBoard(n, Json.Text(pr["id"]) ?? "");
                board.SetStatus(n, StatusRoute.None, to, "dev");
                board.Log("dev", n, $"added as {to}");
                board.Say($"#{n}: added as {to}");
            }
            else if (!draft && status == "In progress")
            {
                board.SetStatus(n, "In progress", "In review", "dev");
                board.Log("dev", n, "In progress → In review");
                board.Say($"#{n}: In progress → In review");
            }
        }
        return 0;
    }

    /// <summary>Moves each In progress task whose PR is out of draft, green and clear of its base to In review, but for a
    /// review still to answer. <paramref name="args"/> are the tasks to leave, which a Dev run has or a stakeholder stopped.</summary>
    internal static int Finished(Board board, string[] args)
    {
        if (!args.All(Digits))
            throw new Die($"usage: board.sh {board.Team} finished [<task>...]");
        foreach (var card in board.AllCards().Where(card => Cards.IsIssue(card) && Cards.Own(card, "dev") && Cards.Status(card) == "In progress"
            && !Cards.Marked(card, "held") && !args.Contains(Cards.N(card))))
        {
            var n = Cards.N(card);
            var (read, pr) = board.PrFor(n);
            Board.Must(read);
            if (pr is null || Json.Truthy(pr["isDraft"]) || Json.Text(pr["mergeable"]) != "MERGEABLE" || Json.Text(pr["review"]) == "posted") continue;
            var p = Board.Raw(pr["number"]);
            if (board.HeadVerdict(p) != "pass") continue;
            if (Json.Text(pr["review"]) == "waiting")
            {
                if (board.TaskPr(p) is not { } said) continue;
                if (!board.SaidUnreviewed(said)) board.PostComment(p, $"{Board.Unreviewed}\n\n{Board.DispatchMarker}");
            }
            board.SetStatus(n, "In progress", "In review", StatusRoute.Dispatcher);
            board.Log(StatusRoute.Dispatcher, n, "In progress → In review");
            board.Say($"#{n}: PR #{p} is finished, In progress → In review");
        }
        return 0;
    }
}
