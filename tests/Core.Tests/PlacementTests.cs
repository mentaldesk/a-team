using System.Text.Json.Nodes;
using Xunit;

namespace ATeam.Core.Tests;

/// <summary>Every open item shows in an Overseer lane, and every turn names something that will act on it, over boards
/// generated from every status, kind of item, mark and state of its PR and comments.</summary>
public class PlacementTests
{
    private const string Repo = "o/r";
    private const string Now = "2026-11-07T00:00:00Z";

    private static readonly string[] AnyStatus = [.. StatusRoute.Statuses, StatusRoute.None, "?Foo", "Triage"];

    private static Board Demo(bool reviewer) => new("demo", new JsonObject
    {
        ["repo"] = Repo,
        ["stakeholders"] = new JsonArray("reviewer"),
        ["app"] = new JsonObject { ["slug"] = "demo-app" },
        ["roles"] = new JsonObject { ["reviewer"] = reviewer, ["customer"] = true },
    }, () => throw new InvalidOperationException("no record here"), new Gh(null), dryRun: false);

    public static TheoryData<string, string> StatusesAndTypes()
    {
        var data = new TheoryData<string, string>();
        foreach (var status in AnyStatus.Where(status => status != "Done"))
            foreach (var type in new[] { "Issue", "PullRequest" })
                data.Add(status, type);
        return data;
    }

    [Theory]
    [MemberData(nameof(StatusesAndTypes))]
    public void Every_open_card_has_a_lane_and_one_whose_status_is_unknown_says_so(string status, string type)
    {
        var card = new JsonObject { ["number"] = 8, ["type"] = type, ["status"] = status, ["marks"] = new JsonObject() };
        Assert.Contains(Cards.Lane(card), Cards.Lanes);
        if (!StatusRoute.Statuses.Contains(status) && status != StatusRoute.None)
        {
            Assert.Equal("Idea", Cards.Lane(card));
            Assert.Contains($"status '{status}' isn't one a-team knows", Board.Facts(card));
        }
    }

    private static JsonObject Open(int n, string type, string repo = Repo, bool draft = false, IEnumerable<(int N, string Repo)>? closes = null, string author = "someone")
    {
        var refs = new JsonArray();
        foreach (var (number, at) in closes ?? [])
            refs.Add(new JsonObject { ["number"] = number, ["repository"] = new JsonObject { ["nameWithOwner"] = at } });
        return new JsonObject
        {
            ["__typename"] = type,
            ["id"] = $"N_{n}",
            ["number"] = n,
            ["title"] = "T",
            ["url"] = "u",
            ["state"] = "OPEN",
            ["isDraft"] = draft,
            ["repository"] = new JsonObject { ["nameWithOwner"] = repo },
            ["labels"] = new JsonObject { ["nodes"] = new JsonArray() },
            ["author"] = new JsonObject { ["__typename"] = "User", ["login"] = author },
            ["closingIssuesReferences"] = new JsonObject { ["nodes"] = refs },
        };
    }

    /// <summary>Each open item of every kind, against a board that has it at every status, or hasn't got it, shut or not.</summary>
    public static IEnumerable<object[]> Boards()
    {
        var kinds = new (string Name, Func<int, JsonObject> Make)[]
        {
            ("issue", n => Open(n, "Issue")),
            ("PR closing nothing", n => Open(n, "PullRequest")),
            ("draft PR", n => Open(n, "PullRequest", draft: true)),
            ("Dependabot's PR", n => Open(n, "PullRequest", author: "dependabot")),
            ("PR closing a card", n => Open(n, "PullRequest", closes: [(1, Repo)])),
            ("PR closing an issue not on the board", n => Open(n, "PullRequest", closes: [(2, Repo)])),
            ("PR closing an issue in another repo", n => Open(n, "PullRequest", closes: [(1, "o/other")])),
        };
        foreach (var (name, _) in kinds)
            foreach (var had in AnyStatus.Prepend(null))
                foreach (var shut in new[] { false, true })
                    if (had is not null || !shut)
                        yield return [name, had, shut];
    }

    [Theory]
    [MemberData(nameof(Boards))]
    public void Every_open_item_arrives_comes_back_or_shows_on_a_card_it_closes(string kind, string? had, bool shut)
    {
        var item = kind switch
        {
            "issue" => Open(8, "Issue"),
            "PR closing nothing" => Open(8, "PullRequest"),
            "draft PR" => Open(8, "PullRequest", draft: true),
            "Dependabot's PR" => Open(8, "PullRequest", author: "dependabot"),
            "PR closing a card" => Open(8, "PullRequest", closes: [(1, Repo)]),
            "PR closing an issue not on the board" => Open(8, "PullRequest", closes: [(2, Repo)]),
            _ => Open(8, "PullRequest", closes: [(1, "o/other")]),
        };
        var board = new Dictionary<string, Known> { ["1"] = new("In progress", false) };
        if (had is not null) board["8"] = new Known(had, shut);
        var plan = Reconciler.Arrivals([item, Open(9, "Issue", repo: "o/other")], board, "demo", Repo, Now);

        var arrived = plan.Arrived.FirstOrDefault(one => one.Number == "8").To;
        var showsOnACard = kind == "PR closing a card";
        if (arrived is not null)
            Assert.Contains(arrived, Cards.Lanes);
        else if (plan.Reopened.Contains("N_8"))
            Assert.True(shut || had == "Done");
        else
            Assert.True(showsOnACard || (had is not null && had != StatusRoute.None && had != "Done" && !shut),
                $"{kind} at {had ?? "nothing"}{(shut ? ", shut" : "")} neither arrived nor came back");
        Assert.DoesNotContain(plan.Arrived, one => one.Number == "9");
    }

    private static JsonObject Said(string kind, string time, string author, string body, long eyes = 0) => new()
    {
        ["at"] = $"2026-10-01T{time}Z",
        ["author"] = author,
        ["body"] = body,
        ["eyes"] = eyes,
        ["kind"] = kind,
        ["n"] = 8,
    };

    private static JsonObject Pr(string checks, bool draft, bool conflicting, bool behind, bool self) => new()
    {
        ["n"] = 8,
        ["pr"] = self ? 8 : 12,
        ["prUrl"] = "https://github.com/o/r/pull/12",
        ["draft"] = draft,
        ["self"] = self,
        ["conflicting"] = conflicting,
        ["behind"] = behind,
        ["resolving"] = false,
        ["base"] = "main",
        ["head"] = "task",
        ["checks"] = checks,
        ["failedAt"] = "",
    };

    private static readonly Dictionary<string, Func<List<JsonObject>>> Talk = new()
    {
        ["nothing said"] = () => [],
        ["your comment unread"] = () => [Said("comment", "10:00:00", "reviewer", "please fix")],
        ["your comment answered from another team's App"] = () =>
            [Said("comment", "10:00:00", "reviewer", "please fix"), Said("comment", "11:00:00", "other-app[bot]", "done\n<!-- a-team:dev -->")],
        ["your comment read"] = () => [Said("comment", "10:00:00", "reviewer", "please fix", eyes: 1)],
        ["a question in the body"] = () => [Said("body", "09:00:00", "demo-app[bot]", "Which one?\n\n**Your call:** pick one\n<!-- a-team:lead -->")],
    };

    /// <summary>Every card that can wait on someone, with every PR state and conversation, each settled against no trigger
    /// for its role and against one.</summary>
    public static IEnumerable<object[]> Turns()
    {
        var statuses = new[] { "Pitched", "In review", "In progress" };
        var kinds = new[] { "task", "pitch", "docs", "dependency", "yours" };
        var prs = new List<string> { "none" };
        foreach (var checks in new[] { "pass", "fail", "pending", "main-red" })
            foreach (var draft in new[] { false, true })
                foreach (var trouble in new[] { "", "conflicting", "behind" })
                    prs.Add($"{checks}{(draft ? " draft" : "")}{(trouble.Length > 0 ? " " + trouble : "")}");
        foreach (var status in statuses)
            foreach (var kind in kinds)
                foreach (var pr in prs)
                    foreach (var talk in Talk.Keys)
                        foreach (var reviewer in new[] { false, true })
                            yield return [status, kind, pr, talk, reviewer];
    }

    [Theory]
    [MemberData(nameof(Turns))]
    public void Every_turn_names_a_role_a_run_starts_for_something_that_clears_itself_or_you(string status, string kind, string pr, string talk, bool reviewer)
    {
        var item = new JsonObject
        {
            ["number"] = 8,
            ["title"] = "A card",
            ["status"] = status,
            ["url"] = "https://github.com/o/r/issues/8",
            ["team"] = "demo",
            ["pitch"] = kind == "pitch",
        };
        if (kind == "docs") item["role"] = "customer";
        if (kind == "dependency") item["dependency"] = true;
        var words = pr.Split(' ');
        List<JsonObject> prs = pr == "none" ? [] : [Pr(words[0], words.Contains("draft"), words.Contains("conflicting"), words.Contains("behind"), kind is "dependency" or "docs")];
        var board = Demo(reviewer);

        foreach (var woken in new[] { false, true })
        {
            var turned = board.Turn(item, Talk[talk](), prs);
            var named = Json.Text(turned["turn"]);
            Board.Settle(turned, Repo, (role, n) => woken && role == named && n == 8);
            var turn = Json.Text(turned["turn"]);
            Assert.True(turn == "you" || (StatusRoute.Roles.Contains(turn) && (woken || Board.ClearsItself(turned))),
                $"{status} {kind}, PR {pr}, {talk}: turn {turn} ({Json.Text(turned["reason"])}) with {(woken ? "a" : "no")} trigger for it");
            if (Json.Truthy(turned["stuck"]))
                Assert.EndsWith(", but nothing will start a run for it", Json.Text(turned["reason"]));
        }
    }

    [Fact]
    public void A_role_that_answered_from_another_teams_run_is_named_in_what_is_stuck()
    {
        var item = new JsonObject { ["number"] = 130, ["status"] = "In review", ["title"] = "T", ["url"] = "u", ["team"] = "tui" };
        var said = Talk["your comment answered from another team's App"]().Select(c => { c["n"] = 130; return c; }).ToList();
        var turned = Demo(reviewer: false).Turn(item, said, []);
        Board.Settle(turned, "mentaldesk/tui-style-guide", (_, _) => false);
        Assert.Equal("you", Json.Text(turned["turn"]));
        Assert.Equal("dev answered on tui-style-guide#130, but nothing will start a run for it", Json.Text(turned["reason"]));
    }

    [Fact]
    public void A_turn_a_trigger_covers_stays_with_its_role()
    {
        var item = new JsonObject { ["number"] = 8, ["status"] = "In review", ["title"] = "T", ["url"] = "u", ["team"] = "demo" };
        var turned = Demo(reviewer: false).Turn(item, Talk["your comment unread"](), []);
        Board.Settle(turned, Repo, (role, n) => role == "dev" && n == 8);
        Assert.Equal("dev", Json.Text(turned["turn"]));
        Assert.False(Json.Truthy(turned["stuck"]));
    }
}
