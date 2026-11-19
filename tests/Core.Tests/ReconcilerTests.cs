using System.Text.Json.Nodes;
using Xunit;

namespace ATeam.Core.Tests;

public class ReconcilerTests
{
    private const string Now = "2026-11-07T00:00:00Z";

    private static JsonArray Nodes(string json) => JsonNode.Parse(json)!.AsArray();

    private static string Node(int number = 8, string state = "OPEN", string? reason = null, string type = "Issue") =>
        $$$"""
        {"__typename": "{{{type}}}", "id": "I_{{{number}}}", "number": {{{number}}}, "title": "T", "url": "u", "state": "{{{state}}}",
         "stateReason": {{{(reason is null ? "null" : $"\"{reason}\"")}}},
         "repository": {"nameWithOwner": "o/r"}, "labels": {"nodes": [{"name": "pitch"}]} }
        """;

    private static ReconcilePlan Plan(string node, Known? known)
    {
        var had = new Dictionary<string, Known>();
        if (known is not null) had["8"] = known;
        return Reconciler.Plan(Nodes($"[{node}]"), had, "demo", "o/r", Now);
    }

    private static Known Row(string status = "Ready", bool shut = false) => new(status, shut);

    [Fact]
    public void A_card_not_in_the_record_is_left_to_arrive_from_the_repos_issues() =>
        Assert.Empty(Plan(Node(), null).Sql);

    [Fact]
    public void An_open_issue_keeps_its_status_and_takes_what_GitHub_says_of_it()
    {
        var plan = Plan(Node(), Row(status: "Pitched"));
        Assert.Contains("UPDATE board SET content", plan.Sql);
        Assert.DoesNotContain("status =", plan.Sql);
    }

    [Fact]
    public void An_issue_closing_takes_its_card_to_Done_and_says_how_it_closed()
    {
        var plan = Plan(Node(state: "CLOSED", reason: "COMPLETED"), Row());
        Assert.Contains("status = 'Done'", plan.Sql);
        Assert.Contains("\"closed\":\"COMPLETED\"", plan.Sql);
    }

    [Fact]
    public void An_issue_reopening_brings_a_shut_card_back()
    {
        var plan = Plan(Node(state: "OPEN"), Row(status: "Done", shut: true));
        Assert.Equal([("8", (string?)"Issue")], plan.Reopened);
    }

    [Fact]
    public void A_card_in_another_repo_is_none_of_this_boards_business()
    {
        var other = Node().Replace("o/r", "o/other");
        Assert.Empty(Plan(other, Row()).Sql);
    }

    [Fact]
    public void A_Priority_set_on_GitHub_is_not_read()
    {
        var ranked = Node().Replace("\"labels\"", "\"issueFieldValues\": {\"nodes\": [{\"name\": \"Urgent\", \"field\": {\"name\": \"Priority\"}}]}, \"labels\"");
        Assert.DoesNotContain("priority", Plan(ranked, Row()).Sql);
        Assert.DoesNotContain("Urgent", Arrivals(ranked).Sql);
    }

    [Theory]
    [InlineData("added as Ready", "Ready")]
    [InlineData("filed as Ready under #3", "Ready")]
    [InlineData("Ready → In progress", "In progress")]
    [InlineData("In progress → Done · merged", "Done")]
    [InlineData("moved to somewhere", null)]
    [InlineData("handed back with a question", null)]
    public void An_events_words_say_where_the_card_landed(string what, string? to) =>
        Assert.Equal(to, Reconciler.Landed(what));

    [Fact]
    public void A_quote_in_a_title_cant_break_out_of_its_statement() =>
        Assert.Equal("'it''s'", Reconciler.Q("it's"));

    private static string Issue(int number = 8, string state = "OPEN", string type = "Issue", string repo = "o/r", string author = "jamescrosswell") =>
        $$$"""
        {"__typename": "{{{type}}}", "id": "I_{{{number}}}", "number": {{{number}}}, "title": "T", "url": "u", "state": "{{{state}}}",
         "repository": {"nameWithOwner": "{{{repo}}}"}, "labels": {"nodes": []}, "author": {"__typename": "User", "login": "{{{author}}}"}}
        """;

    private static ArrivalPlan Arrivals(string issues, Dictionary<string, Known>? had = null) =>
        Reconciler.Arrivals(Nodes($"[{issues}]"), had ?? [], "demo", "o/r", Now);

    [Fact]
    public void An_open_issue_the_board_hasnt_got_arrives_as_an_Idea()
    {
        var plan = Arrivals(Issue());
        Assert.Equal([("8", "Idea")], plan.Arrived);
        Assert.Contains("INSERT OR IGNORE INTO board", plan.Sql);
        Assert.Contains("'Idea'", plan.Sql);
        Assert.Contains("\"author\":\"jamescrosswell\"", plan.Sql);
    }

    [Fact]
    public void An_issue_already_on_the_board_keeps_its_status()
    {
        var plan = Arrivals(Issue(), new() { ["8"] = Row(status: "Pitched") });
        Assert.Empty(plan.Arrived);
        Assert.Empty(plan.Reopened);
        Assert.Empty(plan.Sql);
    }

    [Fact]
    public void An_issue_on_the_board_with_no_status_becomes_an_Idea()
    {
        var plan = Arrivals(Issue(), new() { ["8"] = Row(status: StatusRoute.None) });
        Assert.Equal([("8", "Idea")], plan.Arrived);
        Assert.Contains("SET status = 'Idea'", plan.Sql);
    }

    [Fact]
    public void A_closed_card_open_again_is_read_again_rather_than_added()
    {
        var plan = Arrivals(Issue(), new() { ["8"] = Row(status: "Done", shut: true) });
        Assert.Empty(plan.Arrived);
        Assert.Equal(["I_8"], plan.Reopened);
        Assert.Empty(plan.Sql);
    }

    [Fact]
    public void A_closed_pull_request_open_again_is_read_again()
    {
        var plan = Arrivals(Issue(type: "PullRequest"), new() { ["8"] = Row(status: "Done", shut: true) });
        Assert.Empty(plan.Arrived);
        Assert.Equal(["I_8"], plan.Reopened);
    }

    [Fact]
    public void Closed_items_and_other_repos_dont_arrive()
    {
        var plan = Arrivals(string.Join(",", Issue(state: "CLOSED"), Issue(9, type: "PullRequest", state: "CLOSED"), Issue(10, repo: "o/other")));
        Assert.Empty(plan.Arrived);
        Assert.Empty(plan.Sql);
    }

    private static string Pull(int number = 9, bool draft = false, string closes = "", string author = "jamescrosswell") =>
        Issue(number, type: "PullRequest", author: author).TrimEnd()[..^1]
        + $$$""", "isDraft": {{{(draft ? "true" : "false")}}}, "closingIssuesReferences": {"nodes": [{{{closes}}}]}}""";

    private static string Closing(int number, string repo = "o/r") =>
        $$$"""{"number": {{{number}}}, "repository": {"nameWithOwner": "{{{repo}}}"}}""";

    [Theory]
    [InlineData(false, "In review")]
    [InlineData(true, "In progress")]
    public void A_PR_that_closes_nothing_on_the_board_arrives_in_the_lane_its_draft_says(bool draft, string lane)
    {
        var plan = Arrivals(Pull(draft: draft));
        Assert.Equal([("9", lane)], plan.Arrived);
        Assert.Contains($"'{lane}'", plan.Sql);
        Assert.DoesNotContain("priority", plan.Sql);
    }

    [Fact]
    public void A_PR_that_closes_a_card_on_the_board_shows_on_that_card_instead()
    {
        var plan = Arrivals(Pull(closes: Closing(8)), new() { ["8"] = Row(status: "In progress") });
        Assert.Empty(plan.Arrived);
        Assert.Empty(plan.Sql);
    }

    [Fact]
    public void A_PR_closing_an_issue_arriving_with_it_shows_on_that_issues_card()
    {
        var plan = Arrivals($"{Pull(closes: Closing(8))}, {Issue()}");
        Assert.Equal([("8", "Idea")], plan.Arrived);
    }

    [Fact]
    public void A_PR_closing_an_issue_the_board_hasnt_got_or_one_in_another_repo_arrives()
    {
        var plan = Arrivals(Pull(closes: $"{Closing(8, "o/other")}, {Closing(40)}"), new() { ["8"] = Row() });
        Assert.Equal([("9", "In review")], plan.Arrived);
    }

    [Fact]
    public void Dependabots_PR_arrives_In_progress_for_the_dispatcher_to_merge_or_hand_on() =>
        Assert.Equal([("9", "In progress")], Arrivals(Pull(author: "dependabot")).Arrived);

    [Fact]
    public void A_PR_on_the_board_with_no_status_goes_to_its_lane()
    {
        var plan = Arrivals(Pull(draft: true), new() { ["9"] = Row(status: StatusRoute.None) });
        Assert.Equal([("9", "In progress")], plan.Arrived);
        Assert.Contains("SET status = 'In progress'", plan.Sql);
    }

    [Theory]
    [InlineData("Issue")]
    [InlineData("PullRequest")]
    public void A_Done_card_open_on_GitHub_is_read_again_though_the_board_never_saw_it_close(string type)
    {
        var plan = Arrivals(Issue(type: type), new() { ["8"] = Row(status: "Done", shut: false) });
        Assert.Empty(plan.Arrived);
        Assert.Equal(["I_8"], plan.Reopened);
    }

    [Fact]
    public void A_Done_card_open_on_GitHub_comes_back_though_its_record_never_saw_it_shut()
    {
        var plan = Plan(Node(state: "OPEN"), Row(status: "Done", shut: false));
        Assert.Equal([("8", (string?)"Issue")], plan.Reopened);
    }

    [Fact]
    public void A_bot_author_is_kept_as_its_login_with_bot()
    {
        var bot = Issue().Replace("\"User\"", "\"Bot\"").Replace("jamescrosswell", "other-app");
        Assert.Contains("\"author\":\"other-app[bot]\"", Arrivals(bot).Sql);
    }

    private static JsonNode Card(int number = 8, string status = "Idea", string? label = null, string? mark = null, JsonObject? following = null, int? parent = null)
    {
        var marks = new JsonObject();
        if (mark is not null) marks[mark] = true;
        if (following is not null) marks["following"] = following;
        return new JsonObject
        {
            ["number"] = number, ["type"] = "Issue", ["status"] = status, ["parent"] = parent,
            ["labels"] = label is null ? new JsonArray() : new JsonArray(label), ["marks"] = marks,
        };
    }

    private static FollowPlan Follow(string open, params JsonNode[] cards) =>
        Reconciler.Follow(Nodes($"[{open}]"), cards, "demo", "o/r", Now, login => login == "jamescrosswell" ? "you" : login ?? "someone");

    private static JsonObject Following(int pr = 9, bool gone = false)
    {
        var mark = new JsonObject { ["pr"] = pr, ["draft"] = false, ["by"] = "you" };
        if (gone) mark["gone"] = true;
        return mark;
    }

    [Fact]
    public void Your_Idea_an_open_PR_closes_goes_In_progress_marked_with_the_PR()
    {
        var plan = Follow($"{Issue()}, {Pull(draft: true, closes: Closing(8))}", Card());
        Assert.Equal([("8", "Idea → In progress · PR #9 opened")], plan.Moved);
        Assert.Contains("SET status = 'In progress'", plan.Sql);
        Assert.Contains("""{"pr":9,"draft":true,"by":"you"}""", plan.Sql);
    }

    [Theory]
    [InlineData("Ready", null, null, null)]
    [InlineData("Idea", "pitch", null, null)]
    [InlineData("Idea", null, "dev", null)]
    [InlineData("Idea", null, "idea", null)]
    [InlineData("Idea", null, null, 3)]
    public void A_pitch_a_task_a_discovery_or_an_item_past_Idea_stays_where_it_is(string status, string? label, string? mark, int? parent) =>
        Assert.Empty(Follow($"{Issue()}, {Pull(closes: Closing(8))}", Card(status: status, label: label, mark: mark, parent: parent)).Sql);

    [Fact]
    public void A_PR_in_another_repo_moves_nothing() =>
        Assert.Empty(Follow($"{Issue()}, {Pull(closes: Closing(8)).Replace("\"nameWithOwner\": \"o/r\"}, \"labels\"", "\"nameWithOwner\": \"o/other\"}, \"labels\"")}", Card()).Sql);

    [Fact]
    public void Its_PR_gone_from_one_read_it_stays_In_progress_in_case_the_PR_merged_mid_read()
    {
        var plan = Follow(Issue(), Card(status: "In progress", following: Following()));
        Assert.Empty(plan.Moved);
        Assert.DoesNotContain("SET status", plan.Sql);
        Assert.Contains("'$.following.gone'", plan.Sql);
    }

    [Fact]
    public void Its_PR_back_in_the_next_read_clears_the_doubt() =>
        Assert.Contains("""{"pr":9,"draft":false,"by":"you"}""",
            Follow($"{Issue()}, {Pull(closes: Closing(8))}", Card(status: "In progress", following: Following(gone: true))).Sql);

    [Fact]
    public void Its_PR_closing_unmerged_takes_it_back_to_Idea_on_the_second_read()
    {
        var plan = Follow(Issue(), Card(status: "In progress", following: Following(gone: true)));
        Assert.Equal([("8", "In progress → Idea · PR #9 closed unmerged")], plan.Moved);
        Assert.Contains("SET status = 'Idea'", plan.Sql);
        Assert.Contains("json_remove(marks, '$.following')", plan.Sql);
    }

    [Fact]
    public void Its_PR_no_longer_closing_it_takes_it_back_and_says_so() =>
        Assert.Equal([("8", "In progress → Idea · PR #9 no longer closes it")],
            Follow($"{Issue()}, {Pull()}", Card(status: "In progress", following: Following(gone: true))).Moved);

    [Fact]
    public void Closed_with_its_PR_merged_it_waits_for_the_sync_to_take_it_to_Done() =>
        Assert.Empty(Follow("", Card(status: "In progress", following: Following(gone: true))).Sql);

    [Fact]
    public void Another_open_PR_closing_it_keeps_it_In_progress_naming_that_one()
    {
        var plan = Follow($"{Issue()}, {Pull(12, closes: Closing(8), author: "demo-app")}", Card(status: "In progress", following: Following()));
        Assert.Empty(plan.Moved);
        Assert.Contains("""{"pr":12,"draft":false,"by":"demo-app"}""", plan.Sql);
        Assert.Empty(Follow($"{Issue()}, {Pull(closes: Closing(8))}", Card(status: "In progress", following: Following())).Sql);
    }

    [Fact]
    public void One_In_progress_that_follows_no_PR_is_the_teams_and_stays() =>
        Assert.Empty(Follow(Issue(), Card(status: "In progress", mark: "dev")).Sql);
}
