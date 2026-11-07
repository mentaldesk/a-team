using System.Text.Json.Nodes;
using Xunit;

namespace ATeam.Core.Tests;

public class LinksTests
{
    private const string Today = "2026-11-07";
    private const string Lead = "<!-- a-team:lead -->";
    private const string Dev = "<!-- a-team:dev -->";

    private static Board Demo(Record? record = null)
    {
        var config = new JsonObject { ["repo"] = "mentaldesk/demo", ["stakeholders"] = new JsonArray("reviewer"), ["app"] = new JsonObject { ["slug"] = "demo-app" } };
        return new Board("demo", config, () => record ?? throw new InvalidOperationException("no record here"), new Gh(null), dryRun: false);
    }

    private static JsonObject Held(string by) => new() { ["at"] = $"{Today}T09:00:00Z", ["by"] = by };

    private static JsonObject Said(string kind, string time, string author, string body) =>
        new() { ["kind"] = kind, ["at"] = $"{Today}T{time}Z", ["author"] = author, ["body"] = body };

    private static JsonObject Bot(string time, string marker) => Said("comment", time, "demo-app[bot]", $"Words.\n\n{marker}");

    private static JsonObject You(string time, string kind = "comment") => Said(kind, time, "reviewer", "Words.");

    [Fact]
    public void The_Devs_question_is_the_stakeholders_to_answer_then_the_Devs_to_read()
    {
        var asked = Bot("08:51:00", Dev);
        Assert.Equal(("dev", "you"), Demo().HoldOf(Held("dev"), [asked]));
        Assert.Equal(("dev", "dev"), Demo().HoldOf(Held("dev"), [asked, You("09:30:00")]));
        Assert.Equal(("dev", "dev"), Demo().HoldOf(Held("dev"), [asked, You("09:30:00", "line")]));
    }

    [Fact]
    public void A_Dev_comment_more_than_ten_minutes_before_the_hold_is_no_question()
    {
        Assert.Equal((null, "you"), Demo().HoldOf(Held("lead"), [Bot("08:49:59", Dev)]));
        Assert.Equal(("dev", "you"), Demo().HoldOf(Held("lead"), [Bot("08:50:00", Dev)]));
    }

    [Fact]
    public void The_Leads_ask_is_its_own_to_clear_once_a_stakeholder_replies_after_it()
    {
        Assert.Equal(("lead", "you"), Demo().HoldOf(Held("lead"), [Bot("09:10:00", Lead)]));
        Assert.Equal(("lead", "lead"), Demo().HoldOf(Held("lead"), [Bot("09:10:00", Lead), You("09:20:00")]));
    }

    [Fact]
    public void A_stakeholder_who_spoke_before_the_Lead_holds_it()
    {
        Assert.Equal(("you", "you"), Demo().HoldOf(Held("team"), [You("08:55:00"), Bot("09:10:00", Lead)]));
    }

    [Fact]
    public void A_hold_of_yours_stays_yours_whoever_comments_on_it()
    {
        Assert.Equal((null, "you"), Demo().HoldOf(Held("you"), [Bot("09:10:00", Lead)]));
        Assert.Equal((null, "you"), Demo().HoldOf(Held("you"), [Bot("09:10:00", Dev)]));
        Assert.Equal(("you", "you"), Demo().HoldOf(Held("you"), [You("09:05:00"), Bot("09:10:00", Lead)]));
    }

    [Fact]
    public void With_no_time_for_the_hold_nobody_said_why()
    {
        Assert.Equal((null, "you"), Demo().HoldOf(null, [Bot("09:10:00", Lead), You("09:20:00")]));
    }

    [Theory]
    [InlineData("One line\n", "One line", "")]
    [InlineData("\n  \n  Trimmed  \n\n \nWhy\n\nand more  \n\n", "Trimmed", "Why\n\nand more  ")]
    [InlineData("Only\n   \n", "Only", "")]
    [InlineData("First\nWhy\n   ", "First", "Why\n   ")]
    [InlineData("\t\r\n", "", "")]
    [InlineData("", "", "")]
    public void A_decisions_file_is_its_first_line_with_anything_on_it_then_why(string text, string decision, string why)
    {
        Assert.Equal((decision, why), Commands.DecisionIn(text));
    }

    [Fact]
    public void A_Ready_task_or_bump_can_be_held_but_nothing_else()
    {
        JsonNode Card(string status, string type, bool held, bool dependabot = false)
        {
            var card = new JsonObject
            {
                ["number"] = 1,
                ["status"] = status,
                ["type"] = type,
                ["labels"] = new JsonArray(),
                ["marks"] = held ? new JsonObject { ["held"] = new JsonObject { ["at"] = null, ["by"] = null } } : new JsonObject(),
            };
            if (dependabot) card["dependabot"] = true;
            return card;
        }
        Assert.True(Cards.Held(Card("Ready", "Issue", held: true)));
        Assert.True(Cards.Held(Card("Ready", "PullRequest", held: true, dependabot: true)));
        Assert.False(Cards.Held(Card("Ready", "PullRequest", held: true)));
        Assert.False(Cards.Held(Card("Ready", "Issue", held: false)));
        Assert.False(Cards.Held(Card("In progress", "Issue", held: true)));
    }

    [Fact]
    public void The_registers_full_text_index_works_through_the_systems_SQLite_and_follows_rewording()
    {
        using var record = Record.Open(":memory:");
        var board = Demo(record);
        var id = board.RecordDecision(false, "Hint bars follow TuiCode", "why it's so", "you", "lean", "7", "https://x/7", "2026-10-01T00:00:00Z", "");
        Assert.Equal("1", id);
        Assert.Equal("1", record.Scalar("SELECT rowid FROM decisions_text WHERE decisions_text MATCH 'follow*';"));
        board.ChangeDecisions("UPDATE decisions SET decision = 'Pane colours' WHERE id = 1;");
        Assert.Null(record.Scalar("SELECT rowid FROM decisions_text WHERE decisions_text MATCH 'follow*';"));
        Assert.Equal("1", record.Scalar("SELECT rowid FROM decisions_text WHERE decisions_text MATCH 'colours';"));
    }

    [Fact]
    public void Replacing_a_decision_points_the_old_entry_at_the_new_one()
    {
        using var record = Record.Open(":memory:");
        var board = Demo(record);
        board.RecordDecision(false, "Old", "", "you", "lean", "7", "https://x/7", "2026-10-01T00:00:00Z", "");
        Assert.Equal("2", board.RecordDecision(false, "New", "", "you", "lean", "8", "https://x/8", "2026-10-02T00:00:00Z", "1"));
        Assert.Equal("2", record.Scalar("SELECT replaced_by FROM decisions WHERE id = 1;"));
        Assert.Null(record.Scalar("SELECT replaced_by FROM decisions WHERE id = 2;"));
    }

    [Fact]
    public void The_shared_schema_needs_no_FTS5()
    {
        using var record = Record.Open(":memory:");
        Assert.Equal("0", record.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name LIKE 'decisions%';"));
    }
}
