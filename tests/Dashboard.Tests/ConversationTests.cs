namespace ATeam.Dashboard.Tests;

public class ConversationTests
{
    private static readonly DateTimeOffset Opened = new(2026, 9, 28, 21, 4, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Asked = new(2026, 9, 29, 4, 10, 0, TimeSpan.Zero);

    private static string Local(DateTimeOffset at) =>
        at.ToLocalTime().ToString("dd MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Each_remark_follows_the_body_under_a_rule_naming_who_and_when()
    {
        var conversation = Conversation.Of(new Reading($$"""
            [{"who": "dev", "at": "{{Opened:O}}", "body": "Only the demote comments now.", "pr": 239, "description": true},
             {"who": "you", "at": "{{Asked:O}}", "body": "Does it say why?", "pr": null, "description": false},
             {"who": "octocat", "at": "{{Asked:O}}", "body": "+1", "pr": 239, "description": false}]
            """, null), 233);

        var text = new IssueBody("## Context").With(conversation).Text;

        Assert.Equal(
            $"## Context\n\n───── PR #239 · dev · {Local(Opened)} ─────\n\nOnly the demote comments now." +
            $"\n\n───── you · {Local(Asked)} ─────\n\nDoes it say why?" +
            $"\n\n───── octocat · {Local(Asked)} ─────\n\n+1",
            text);
    }

    [Fact]
    public void Nothing_said_leaves_the_body_alone()
    {
        var conversation = Conversation.Of(new Reading("[]", null), 6);

        Assert.Equal(new IssueBody("## Context"), new IssueBody("## Context").With(conversation));
    }

    [Fact]
    public void A_failed_read_is_one_line_under_the_body()
    {
        var conversation = Conversation.Of(new Reading("", "board.sh: can't read the conversation on #6 (gh: HTTP 502)"), 6);

        Assert.Equal("## Context\n\nboard.sh: can't read the conversation on #6 (gh: HTTP 502)",
            new IssueBody("## Context").With(conversation).Text);
    }

    [Fact]
    public void Output_that_isnt_a_conversation_says_so_by_number()
    {
        Assert.Equal("couldn't read the conversation on #6", Conversation.Of(new Reading("{}", null), 6).Failure);
    }
}
