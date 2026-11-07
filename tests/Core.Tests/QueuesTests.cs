using System.Text.Json.Nodes;
using Xunit;

namespace ATeam.Core.Tests;

public class QueuesTests
{
    private const string Day = "2026-10-01";
    private const string Dev = "<!-- a-team:dev -->";
    private const string Lead = "<!-- a-team:lead -->";

    private static Board Demo(bool reviewer = false)
    {
        var config = new JsonObject
        {
            ["repo"] = "mentaldesk/demo",
            ["stakeholders"] = new JsonArray("reviewer"),
            ["app"] = new JsonObject { ["slug"] = "demo-app" },
            ["roles"] = new JsonObject { ["reviewer"] = reviewer },
        };
        return new Board("demo", config, () => throw new InvalidOperationException("no record here"), new Gh(null), dryRun: false);
    }

    private static JsonObject Said(long n, string kind, string time, string author, string body, long eyes = 0) => new()
    {
        ["at"] = $"{Day}T{time}Z",
        ["author"] = author,
        ["body"] = body,
        ["eyes"] = eyes,
        ["kind"] = kind,
        ["n"] = n,
    };

    private static JsonObject Item(long n, string status, bool pitch = false) => new()
    {
        ["number"] = n,
        ["title"] = "A card",
        ["status"] = status,
        ["url"] = $"https://github.com/mentaldesk/demo/issues/{n}",
        ["team"] = "demo",
        ["priority"] = null,
        ["pitch"] = pitch,
    };

    private static JsonObject Card(string marks, string? priority = null) => new()
    {
        ["number"] = 7,
        ["priority"] = priority,
        ["marks"] = Json.Parse(marks),
    };

    private static JsonObject Pr(long n, long pr, string checks = "pass", bool draft = false, string mergeable = "MERGEABLE") => new()
    {
        ["n"] = n,
        ["pr"] = pr,
        ["prUrl"] = $"https://github.com/mentaldesk/demo/pull/{pr}",
        ["draft"] = draft,
        ["self"] = n == pr,
        ["conflicting"] = mergeable == "CONFLICTING",
        ["behind"] = false,
        ["resolving"] = mergeable == "UNKNOWN",
        ["base"] = "main",
        ["head"] = "task",
        ["checks"] = checks,
        ["failedAt"] = "",
    };

    private static string Turned(JsonObject item, List<JsonObject> said, List<JsonObject> prs, bool reviewer = false)
    {
        var turned = Demo(reviewer).Turn(item, said, prs);
        return $"{Json.Text(turned["turn"])}: {Json.Text(turned["reason"])}".Split(" since ")[0];
    }

    [Fact]
    public void A_field_read_back_from_tsv_keeps_its_escapes()
    {
        Assert.Equal(@"a\\b\tc\nd\re", Board.Tsv("a\\b\tc\nd\re"));
        Assert.Equal("Bump Spectre.Console", Board.Tsv("Bump Spectre.Console"));
    }

    [Fact]
    public void A_value_reads_as_jq_prints_it_raw()
    {
        Assert.Equal("null", Board.Raw(null));
        Assert.Equal("text", Board.Raw("text"));
        Assert.Equal("7", Board.Raw(7));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("## Worth considering\n\n- One\n- Two\n  - Under it\n* Three\n1. Four\n2) Five\n", 5)]
    [InlineData("## WORTH CONSIDERING  \r\n- One\r\n", 1)]
    [InlineData("### Worth considering\n- One\n## Next\n- Not this\n", 1)]
    [InlineData("## Worth considering\n- One\n<!-- a-team:reviewer -->\n- Not this\n", 1)]
    [InlineData("Worth considering\n- Not a heading\n", 0)]
    public void A_review_counts_the_points_under_its_heading(string review, int points) =>
        Assert.Equal(points, Board.Considered(review));

    [Fact]
    public void The_themes_named_are_the_next_themes_sections_issues_once_each_in_order()
    {
        const string vision = "# Vision\n#1\n## Next themes\nSee #30, (#4), #4 and owner/repo#9, a&#5; or x#6.\n### Under it #7\n## Later\n#8\n## Next themes  \n#2\n";
        Assert.Equal(["2", "4", "7", "30"], Board.ThemesNamed(vision));
    }

    [Fact]
    public void A_vision_comes_due_by_its_unit_and_months_run_on_as_a_calendar_does()
    {
        Assert.Equal(1759536000 + (30 * 86400), Board.VisionDueAt("2025-10-04T00:00:00Z", "30", "days"));
        Assert.Equal(1759536000 + (2 * 7 * 86400), Board.VisionDueAt("2025-10-04T00:00:00Z", "2", "weeks"));
        Assert.Equal(new DateTimeOffset(2026, 3, 3, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), Board.VisionDueAt("2026-01-31T12:00:00Z", "1", "months"));
        Assert.Equal(new DateTimeOffset(2027, 2, 15, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), Board.VisionDueAt("2026-11-15T00:00:00Z", "3", "months"));
        Assert.Null(Board.VisionDueAt("not a time", "30", "days"));
    }

    [Fact]
    public void An_items_marks_read_as_facts_in_their_own_order()
    {
        Assert.Equal("", Board.Facts(Card("{}")));
        Assert.Equal(
            "Lead recommends High · theme: Speed · skipped · held · needs TOKEN · waiting on a/b#1, c/d#2",
            Board.Facts(Card("""{"upstream": ["a/b#1", "c/d#2"], "secret": {"name": "TOKEN"}, "held": {"at": null}, "skipped": true, "theme": "Speed", "rank": "High"}""")));
        Assert.Equal("waiting on another repo", Board.Facts(Card("""{"upstream": true}""")));
    }

    [Fact]
    public void A_shelve_shows_while_it_cools_at_the_priority_it_had()
    {
        var until = DateTime.UtcNow.AddDays(20);
        var marks = "{\"shelved\": {\"until\": \"" + until.ToString("yyyy-MM-dd") + "\", \"priority\": \"High\"}}";
        Assert.Equal($"shelved until {until:dd MMM}".Replace("until 0", "until "), Board.Facts(Card(marks, "High")));
        Assert.Equal("", Board.Facts(Card(marks, "Low")));
        Assert.Equal("", Board.Facts(Card("""{"shelved": {"until": "2020-01-01", "priority": "High"}}""", "High")));
        Assert.Equal("", Board.Facts(Card("""{"shelved": {"until": null, "priority": null}}""")));
    }

    [Fact]
    public void What_was_said_is_read_off_the_page_with_the_PR_that_closes_it()
    {
        var page = Json.Parse("""
            {"data": {"repository": {"x7": {"number": 7, "createdAt": "A", "body": null, "author": {"__typename": "Bot", "login": "demo-app"},
               "comments": {"nodes": [{"createdAt": "B", "body": "Hi", "author": {"__typename": "User", "login": "reviewer"}, "reactions": {"totalCount": 2}}]},
               "closedByPullRequestsReferences": {"nodes": [{"number": 9, "createdAt": "C", "body": "PR", "author": null,
                 "reviews": {"nodes": [
                   {"createdAt": "D", "body": "", "state": "COMMENTED", "author": null, "comments": {"nodes": [{"createdAt": "E", "body": "Line", "author": null}]}},
                   {"createdAt": "F", "body": "", "state": "CHANGES_REQUESTED", "author": null},
                   {"createdAt": "G", "body": "Words", "state": "COMMENTED", "author": null}]}}]}}}}}
            """);
        var said = Board.GatedComments(page);
        Assert.Equal(["A body", "B comment", "C body", "E line", "F review", "G review"], said.Select(c => $"{Json.Text(c["at"])} {Json.Text(c["kind"])}"));
        Assert.All(said, c => Assert.Equal(7, Json.Long(c["n"])));
        Assert.Equal("demo-app[bot]", Json.Text(said[0]["author"]));
        Assert.Equal(2, Json.Long(said[1]["eyes"]));
        Assert.Equal("", Json.Text(said[2]["author"]));
    }

    [Fact]
    public void A_PRs_checks_are_read_off_the_page_and_only_a_fixs_never_read_main_red()
    {
        var page = Json.Parse("""
            {"data": {"repository": {"x7": {"number": 7, "closedByPullRequestsReferences": {"nodes": [{"number": 9, "url": "u", "isDraft": true, "mergeable": "UNKNOWN",
              "mergeStateStatus": "BEHIND", "baseRefName": "main", "headRefName": "task", "commits": {"nodes": [{"commit": {"checkSuites": {"nodes": [{"checkRuns": {"nodes": [
                {"name": "build", "status": "COMPLETED", "conclusion": "FAILURE", "completedAt": "T2", "url": "l"},
                {"name": "build", "status": "COMPLETED", "conclusion": "FAILURE", "completedAt": "T1", "url": "l"}]}}]}}}]}}]}}}}}
            """);
        var main = new JsonObject { ["failing"] = new JsonArray("build") };
        var pr = Assert.Single(Board.GatedPrs(page, main, new HashSet<long>()));
        Assert.Equal("main-red", Json.Text(pr["checks"]));
        Assert.Equal("T2", Json.Text(pr["failedAt"]));
        Assert.True(Json.Truthy(pr["behind"]));
        Assert.True(Json.Truthy(pr["resolving"]));
        Assert.False(Json.Truthy(pr["self"]));
        Assert.Equal("fail", Json.Text(Assert.Single(Board.GatedPrs(page, main, new HashSet<long> { 7 }))["checks"]));
        Assert.Equal("fail", Json.Text(Assert.Single(Board.GatedPrs(page, null, new HashSet<long>()))["checks"]));
    }

    [Fact]
    public void A_gate_is_the_stakeholders_until_one_comments()
    {
        var body = Said(7, "body", "08:00:00", "demo-app[bot]", $"The task\n{Lead}");
        Assert.Equal("you: awaiting your acceptance", Turned(Item(7, "In review"), [body], []));
        Assert.Equal("you: awaiting your approval", Turned(Item(7, "Pitched"), [body], []));
        Assert.Equal("dev: answering your feedback", Turned(Item(7, "In review"), [body, Said(7, "comment", "09:00:00", "reviewer", "No.")], []));
        Assert.Equal("lead: answering your feedback", Turned(Item(7, "Pitched"), [body, Said(7, "comment", "09:00:00", "reviewer", "No.")], []));
        Assert.Equal("you: awaiting your acceptance", Turned(Item(7, "In review"), [body, Said(7, "comment", "09:00:00", "reviewer", "No.", eyes: 1)], []));
    }

    [Fact]
    public void An_unanswered_comment_outranks_a_PRs_troubles_and_those_go_in_order()
    {
        var comment = Said(7, "comment", "09:00:00", "reviewer", "No.");
        Assert.Equal("dev: answering your feedback", Turned(Item(7, "In review"), [comment], [Pr(7, 9, "fail")]));
        Assert.Equal("dev: CI failing", Turned(Item(7, "In review"), [], [Pr(7, 9, "fail", draft: true, mergeable: "CONFLICTING")]));
        Assert.Equal("dev: main is red", Turned(Item(7, "In review"), [], [Pr(7, 9, "main-red")]));
        Assert.Equal("dev: conflicts with main", Turned(Item(7, "In review"), [], [Pr(7, 9, "pass", mergeable: "CONFLICTING")]));
        Assert.Equal("dev: resolving mergeable status", Turned(Item(7, "In review"), [], [Pr(7, 9, "pass", mergeable: "UNKNOWN")]));
        Assert.Equal("dev: CI running", Turned(Item(7, "In review"), [], [Pr(7, 9, "pending")]));
        Assert.Equal("dev: still a draft", Turned(Item(7, "In review"), [], [Pr(7, 9, "pass", draft: true)]));
        Assert.Equal("reviewer: awaiting review", Turned(Item(7, "In review"), [], [Pr(7, 9, "pass", draft: true)], reviewer: true));
        Assert.Equal("you: awaiting your acceptance", Turned(Item(7, "In review"), [], [Pr(7, 9)]));
    }

    [Fact]
    public void A_reviews_points_ride_along_and_it_stops_waiting_on_the_reviewer()
    {
        var review = Said(7, "comment", "09:00:00", "demo-app[bot]", "## Worth considering\n- One\n\n<!-- a-team:reviewer -->");
        var turned = Demo(reviewer: true).Turn(Item(7, "In review"), [review], [Pr(7, 9, "pass", draft: true)]);
        Assert.Equal("dev", Json.Text(turned["turn"]));
        Assert.True(Json.Truthy(turned["reviewed"]));
        Assert.Equal(1, Json.Long(turned["consider"]));
    }

    [Fact]
    public void A_Dependabot_PR_is_the_Devs_while_it_runs_and_the_Leads_to_read_at_the_gate()
    {
        var running = Item(7, "In progress");
        running["dependency"] = true;
        Assert.Equal("dev: green, about to merge", Turned(running, [], [Pr(7, 7)]));
        var gated = Item(7, "In review");
        gated["dependency"] = true;
        Assert.Equal("you: awaiting your acceptance", Turned(gated, [Said(7, "comment", "09:00:00", "reviewer", "No.")], [Pr(7, 7)]));
        Assert.Equal("dev: CI failing", Turned(gated, [], [Pr(7, 7, "fail")]));
    }

    [Fact]
    public void A_PR_that_is_the_item_only_counts_at_review_otherwise_it_only_names_the_branch()
    {
        var turned = Demo().Turn(Item(7, "Pitched"), [], [Pr(7, 7, "fail")]);
        Assert.Null(turned["pr"]);
        Assert.Equal("main", Json.Text(turned["base"]));
        Assert.Equal("task", Json.Text(turned["branch"]));
        Assert.Equal("you", Json.Text(turned["turn"]));
    }

    private static JsonObject Blocked(string by = "team") => new() { ["7"] = new JsonObject { ["at"] = $"{Day}T09:00:00Z", ["by"] = by } };

    private static JsonObject Hold(List<JsonObject> said, JsonObject? blocked = null) =>
        Assert.Single(Demo().Holds([Item(7, "Ready")], said, blocked ?? Blocked()));

    [Fact]
    public void A_Dev_comment_just_before_the_hold_is_its_question()
    {
        var asked = Said(7, "comment", "08:55:00", "demo-app[bot]", $"Which one?\n\n{Dev}\n");
        var question = Hold([asked]);
        Assert.Equal("dev", Json.Text(question["heldBy"]));
        Assert.Equal("Which one?", Json.Text(question["question"]));
        Assert.Equal("you", Json.Text(question["turn"]));
        Assert.Null(question["unread"]);
        var answered = Assert.Single(Demo().Questions([Item(7, "Ready")], [asked, Said(7, "comment", "09:30:00", "reviewer", "This.")], Blocked()));
        Assert.Equal("dev", Json.Text(answered["turn"]));
        Assert.Equal($"{Day}T09:30:00Z", Json.Text(answered["unread"]));
        Assert.Empty(Demo().Questions([Item(7, "Ready")], [Said(7, "comment", "08:49:59", "demo-app[bot]", $"Too early.\n\n{Dev}")], Blocked()));
    }

    [Fact]
    public void The_Lead_or_a_stakeholder_holds_it_by_who_spoke_first_since()
    {
        var lead = Said(7, "comment", "09:10:00", "demo-app[bot]", $"Waiting on the design.\n\n{Lead}");
        var you = Said(7, "comment", "09:20:00", "reviewer", "Fine.");
        var early = Said(7, "comment", "09:05:00", "reviewer", "Hold this.");
        Assert.Equal("lead you", Who(Hold([lead])));
        Assert.Equal("lead lead", Who(Hold([lead, you])));
        Assert.Equal("you you", Who(Hold([early, lead])));
        Assert.Equal("Hold this.", Json.Text(Hold([early, lead])["question"]));
        Assert.Equal("you you", Who(Hold([lead, you], Blocked("you"))));
        Assert.Equal(" you", Who(Hold([], Blocked("you"))));
        Assert.Equal(" you", Who(Hold([], [])));
    }

    private static string Who(JsonObject hold) => $"{Json.Text(hold["heldBy"])} {Json.Text(hold["turn"])}";

    [Fact]
    public void Of_comments_tied_on_time_a_question_is_the_last_of_them()
    {
        var first = Said(7, "comment", "09:10:00", "demo-app[bot]", $"First.\n\n{Dev}");
        var second = Said(7, "comment", "09:10:00", "demo-app[bot]", $"Second.\n\n{Dev}");
        Assert.Equal("Second.", Json.Text(Hold([first, second])["question"]));
    }

    private static List<string> Reasons(string pr, List<JsonObject>? lines = null, long open = 12) =>
        Demo().UpstreamReasons(Json.Parse(pr)!, lines ?? [], "mentaldesk/style", "demo-app", new HashSet<long> { open }).Select(reason => $"{reason.N} {reason.Why}").ToList();

    private static string Upstream(string rollup, string more = "") => """
        {"number": 84, "isDraft": true, "headRefOid": "abcdef1234", "createdAt": "2026-10-01T08:00:00Z",
         "body": "Needed by MentalDesk/Demo#12\n\n<!-- a-team:dev -->", "statusCheckRollup":
        """ + rollup + more + "}";

    [Fact]
    public void Its_PR_upstream_wakes_the_Dev_on_the_task_it_is_needed_by()
    {
        Assert.Equal(["12 CI failed on mentaldesk/style#84 at abcdef1"], Reasons(Upstream("""[{"status": "COMPLETED", "conclusion": "TIMED_OUT"}]""")));
        Assert.Equal(["12 CI failed on mentaldesk/style#84 at abcdef1"], Reasons(Upstream("""[{"__typename": "StatusContext", "state": "ERROR"}]""")));
        Assert.Equal(["12 mentaldesk/style#84 is green but still a draft"], Reasons(Upstream("""[{"status": "COMPLETED", "conclusion": "SUCCESS"}]""")));
        Assert.Empty(Reasons(Upstream("""[{"__typename": "StatusContext", "state": "PENDING"}]""")));
        Assert.Empty(Reasons(Upstream("[]")));
        Assert.Empty(Reasons(Upstream("""[{"status": "COMPLETED", "conclusion": "FAILURE"}]"""), open: 13));
        Assert.Empty(Reasons(Upstream("""[{"status": "COMPLETED", "conclusion": "FAILURE"}]""").Replace("<!-- a-team:dev -->", "")));
    }

    [Fact]
    public void A_stakeholders_word_upstream_wakes_it_until_the_Dev_speaks_after_it()
    {
        const string asked = """{"author": {"login": "reviewer"}, "createdAt": "2026-10-01T09:00:00Z", "body": "Why?"}""";
        const string answered = """{"author": {"login": "app/demo-app"}, "createdAt": "2026-10-01T10:00:00Z", "body": "Because.\n\n<!-- a-team:dev -->"}""";
        const string feedback = "12 stakeholder feedback on mentaldesk/style#84 (2026-10-01T09:00:00Z)";
        Assert.Equal([feedback], Reasons(Upstream("[]", $", \"comments\": [{asked}]")));
        Assert.Empty(Reasons(Upstream("[]", $", \"comments\": [{asked}, {answered}]")));
        Assert.Equal([feedback], Reasons(Upstream("[]"), [(JsonObject)Json.Parse(asked)!]));
        Assert.Empty(Reasons(Upstream("[]", """, "reviews": [{"author": {"login": "reviewer"}, "submittedAt": "2026-10-01T09:00:00Z", "body": "", "state": "COMMENTED"}]""")));
        Assert.Equal([feedback], Reasons(Upstream("[]", """, "reviews": [{"author": {"login": "reviewer"}, "submittedAt": "2026-10-01T09:00:00Z", "body": "", "state": "CHANGES_REQUESTED"}]""")));
    }

    private static JsonObject Wait(string state, string until = "closed", string? package = null, JsonNode? bump = null)
    {
        var wait = new JsonObject
        {
            ["ref"] = "mentaldesk/style#84",
            ["repo"] = "mentaldesk/style",
            ["n"] = 84,
            ["until"] = until,
            ["package"] = package,
            ["kind"] = "Issue",
            ["state"] = state,
            ["met"] = false,
            ["shown"] = state,
        };
        if (bump is not null) wait["bump"] = bump;
        return wait;
    }

    private static string Chained(JsonObject wait, JsonNode? reach = null)
    {
        Board.ConfigDir = "/nowhere";
        return Json.Compact(Demo().Chains(new JsonArray(wait), reach, new HashSet<long> { 5 }));
    }

    [Fact]
    public void A_wait_not_yet_met_says_the_links_still_to_go()
    {
        Assert.Equal("""[[{"team":null,"refs":[84],"what":"style#84","shown":"open"}]]""", Chained(Wait("open")));
        var reach = Json.Parse("""{"mentaldesk/style#84": {"refs": [90, 84, 91], "pr": 91}}""");
        Assert.Equal("""[[{"team":null,"refs":[84,90,91],"what":"style#91","shown":"open"}]]""", Chained(Wait("open"), reach));
        Assert.Equal("""[[{"team":null,"refs":[],"what":"Pkg's release","shown":"yours"},{"team":"demo","refs":[],"what":"demo's bump","shown":"merges by itself"}]]""",
            Chained(Wait("merged", "released", "Pkg")));
        Assert.Equal("""[[{"team":null,"refs":[],"what":"style's release","shown":"yours"}]]""", Chained(Wait("closed", "released")));
        Assert.Equal("""[[{"team":null,"refs":[],"what":"style#84","shown":"dropped"}]]""", Chained(Wait("dropped")));
    }

    [Fact]
    public void A_bump_holding_a_released_wait_is_named_and_yours_once_held()
    {
        Assert.Equal("""[[{"team":"demo","refs":[5],"what":"#5","shown":"yours"}]]""", Chained(Wait("released", "released", "Pkg", new JsonObject { ["number"] = 5 })));
        Assert.Equal("""[[{"team":"demo","refs":[6],"what":"#6","shown":"merges by itself"}]]""", Chained(Wait("released", "released", "Pkg", new JsonObject { ["number"] = 6 })));
    }

    [Fact]
    public void A_met_wait_has_no_chain()
    {
        var met = Wait("closed");
        met["met"] = true;
        Assert.Equal("[]", Chained(met));
    }
}
