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
        Assert.Equal(["8"], plan.Arrived);
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
        Assert.Equal(["8"], plan.Arrived);
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
    public void Closed_issues_pull_requests_and_other_repos_dont_arrive()
    {
        var plan = Arrivals(string.Join(",", Issue(state: "CLOSED"), Issue(9, type: "PullRequest"), Issue(10, repo: "o/other")));
        Assert.Empty(plan.Arrived);
        Assert.Empty(plan.Sql);
    }

    [Fact]
    public void A_bot_author_is_kept_as_its_login_with_bot()
    {
        var bot = Issue().Replace("\"User\"", "\"Bot\"").Replace("jamescrosswell", "other-app");
        Assert.Contains("\"author\":\"other-app[bot]\"", Arrivals(bot).Sql);
    }
}
