using System.Text.Json.Nodes;
using Xunit;

namespace ATeam.Core.Tests;

public class ChecklistTests
{
    private const string Issue = """
        ## Context

        - [ ] not a criterion

        ## Acceptance criteria

        - [ ] Save is left-aligned
        - [x] Remove is red,
          in every theme
          - [ ] and it says so
        * Plain

        ## Tests

        - [ ] nor this

        """;

    private const string Pr = """
        Closes #12

        ## What it looks like

        ![Save is left-aligned](https://github.com/x/shot.png)
        ![Remove is red, in every theme](https://github.com/x/other.png)

        ## Checked by the team

        - `screen` Save is left-aligned
        - `test` Remove is red, in every theme
        a stray line

        a-team try demo 912

        - `screen` Plain

        <!-- a-team:dev -->
        """;

    private static string Shape(JsonArray criteria) => Json.Compact(criteria);

    [Fact]
    public void Each_criterion_has_its_line_depth_box_and_text_and_a_wrapped_line_joins_its_item()
    {
        var criteria = Checklist.Read(Issue, "");
        Assert.Equal("""
            [{"line":6,"depth":0,"ticked":false,"text":"Save is left-aligned","index":1,"how":null,"picture":null},{"line":7,"depth":0,"ticked":true,"text":"Remove is red, in every theme","index":2,"how":null,"picture":null},{"line":9,"depth":1,"ticked":false,"text":"and it says so","index":3,"how":null,"picture":null},{"line":10,"depth":0,"ticked":false,"text":"Plain","index":4,"how":null,"picture":null}]
            """, Shape(criteria));
    }

    [Fact]
    public void How_each_was_checked_comes_from_its_section_and_only_a_screen_check_has_a_picture()
    {
        var criteria = Checklist.Read(Issue, Pr);
        Assert.Equal("screen", Json.Text(criteria[0]!["how"]));
        Assert.Equal("""{"alt":"Save is left-aligned","url":"https://github.com/x/shot.png"}""", Json.Compact(criteria[0]!["picture"]));
        Assert.Equal("test", Json.Text(criteria[1]!["how"]));
        Assert.Null(criteria[1]!["picture"]);
    }

    [Fact]
    public void A_section_ends_at_the_try_line_so_what_follows_says_nothing()
    {
        var criteria = Checklist.Read(Issue, Pr);
        Assert.Null(criteria[3]!["how"]);
    }

    [Fact]
    public void Brackets_and_spacing_in_a_criterion_dont_stop_it_matching_its_record()
    {
        var criteria = Checklist.Read("## Acceptance criteria\n- [ ] Uses [the  flag]\n", "## Checked by the team\n- `test` Uses the flag  \n");
        Assert.Equal("test", Json.Text(criteria[0]!["how"]));
    }

    [Fact]
    public void A_CRLF_body_reads_the_same()
    {
        Assert.Equal(Shape(Checklist.Read(Issue, Pr)), Shape(Checklist.Read(Issue.Replace("\n", "\r\n"), Pr.Replace("\n", "\r\n"))));
    }

    [Fact]
    public void A_body_with_no_section_has_no_criteria()
    {
        Assert.Empty(Checklist.Read("## Context\n\n- [ ] a list, but not criteria\n", ""));
    }

    [Fact]
    public void Text_before_the_first_bullet_belongs_to_nothing()
    {
        var criteria = Checklist.Read("### acceptance Criteria\nDone when:\n- one\n", "");
        Assert.Equal("one", Json.Text(Assert.Single(criteria)!["text"]));
    }
}

public class TrendsTests
{
    private const string Fortnight = "2026-10-01T00:00:00Z";

    private static string At(int minutes) => DateTimeOffset.Parse("2026-10-10T00:00:00Z").AddMinutes(minutes).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    private static Happening E(long item, int minutes, string who, string what) => new(item, At(minutes), who, what);

    private static JsonObject? Cycle(params Happening[] events) => Trends.Cycle(events, []);

    private static long WithYou(JsonObject? cycle) => Json.Long(cycle!["withYou"]);

    private static JsonArray Tasks(params (long Item, long? Parent)[] tasks)
    {
        var array = new JsonArray();
        foreach (var (item, parent) in tasks)
            array.Add((JsonNode?)new JsonObject { ["item"] = item, ["parent"] = parent });
        return array;
    }

    [Theory]
    [InlineData("added as Ready", null, "Ready")]
    [InlineData("filed as Ready under #745", null, "Ready")]
    [InlineData("filed as Ready to make main green", null, "Ready")]
    [InlineData("Ready → In progress", "Ready", "In progress")]
    [InlineData("Done → Ready · reopened", "Done", "Ready")]
    public void A_line_that_moved_its_item_says_from_where_to_where(string what, string? from, string to)
    {
        Assert.Equal((from, to), Trends.Move(what));
    }

    [Theory]
    [InlineData("commented")]
    [InlineData("moved to Ready")]
    [InlineData("accepted · closed")]
    public void Any_other_line_moved_nothing(string what)
    {
        Assert.Null(Trends.Move(what));
    }

    [Fact]
    public void A_cycle_runs_from_first_entering_Ready_to_its_last_acceptance_with_every_spell_in_review_counted()
    {
        var cycle = Cycle(
            E(1, 0, "lead", "Idea → Exploring"),
            E(1, 10, "lead", "added as Ready"),
            E(1, 20, "dev", "Ready → In progress"),
            E(1, 30, "dev", "In progress → In review"),
            E(1, 40, "you", "In review → Ready"),
            E(1, 50, "dev", "Ready → In progress"),
            E(1, 60, "dev", "In progress → In review"),
            E(1, 90, "you", "accepted · PR #2 merged"));
        Assert.Equal(At(10), Json.Text(cycle!["ready"]));
        Assert.Equal(At(90), Json.Text(cycle["accepted"]));
        Assert.Equal((10 + 30) * 60, WithYou(cycle));
    }

    [Theory]
    [InlineData("filed as Ready under #745")]
    [InlineData("filed as Ready to make main green")]
    public void A_cycle_of_a_task_the_lead_filed_starts_when_it_was_filed(string filed)
    {
        var cycle = Cycle(
            E(1, 10, "lead", filed),
            E(1, 20, "dev", "Ready → In progress"),
            E(1, 30, "dev", "In progress → In review"),
            E(1, 90, "you", "accepted · PR #2 merged"));
        Assert.Equal(At(10), Json.Text(cycle!["ready"]));
        Assert.Equal(60 * 60, WithYou(cycle));
    }

    [Fact]
    public void A_filed_task_is_ready_from_when_it_was_filed()
    {
        var events = new[]
        {
            E(30, 100, "lead", "filed as Ready under #745"),
            E(30, 500, "dev", "Ready → In progress"),
            E(30, 550, "dev", "In progress → In review"),
            E(30, 600, "you", "accepted · PR #930 merged"),
        };
        var cycle = Trends.Cycle(events, Trends.Waits(events));
        Assert.Equal(At(100), Json.Text(cycle!["ready"]));
        Assert.Equal(50 * 60, WithYou(cycle));
    }

    [Fact]
    public void A_filed_task_handed_back_with_a_question_waits_on_you_until_the_answer()
    {
        var events = new[]
        {
            E(40, 0, "lead", "filed as Ready to make main green"),
            E(40, 50, "dev", "Ready → In progress"),
            E(40, 100, "dev", "In progress → Ready"),
            E(40, 100, "dev", "handed back with a question"),
            E(40, 180, "dev", "question answered"),
            E(40, 190, "dev", "Ready → In progress"),
            E(40, 200, "dev", "In progress → In review"),
            E(40, 300, "you", "accepted · PR #940 merged"),
        };
        Assert.Equal((80 + 100) * 60, WithYou(Trends.Cycle(events, Trends.Waits(events))));
    }

    [Fact]
    public void An_item_whose_first_task_status_isnt_entering_Ready_has_no_cycle()
    {
        Assert.Null(Cycle(E(1, 0, "dev", "Ready → In progress"), E(1, 10, "you", "accepted · closed")));
        Assert.Null(Cycle(E(1, 0, "lead", "Exploring → Pitched"), E(1, 10, "you", "accepted · closed")));
    }

    [Fact]
    public void Someone_elses_acceptance_is_no_cycle()
    {
        Assert.Null(Cycle(E(1, 0, "lead", "added as Ready"), E(1, 10, "octocat", "accepted · closed")));
    }

    [Fact]
    public void A_ready_task_doesnt_wait_on_you_while_the_slots_are_full_of_prs_in_review()
    {
        var events = new[]
        {
            E(31, 0, "dev", "Ready → In progress"),
            E(32, 50, "dev", "Ready → In progress"),
            E(30, 100, "lead", "added as Ready"),
            E(31, 200, "dev", "In progress → In review"),
            E(32, 250, "dev", "In progress → In review"),
            E(31, 400, "you", "accepted · PR #931 merged"),
            E(30, 500, "dev", "Ready → In progress"),
            E(30, 550, "dev", "In progress → In review"),
            E(30, 600, "you", "accepted · PR #930 merged"),
        };
        var own = events.Where(e => e.Item == 30).ToList();
        Assert.Equal(50 * 60, WithYou(Trends.Cycle(own, Trends.Waits(events))));
    }

    [Fact]
    public void A_task_handed_back_with_a_question_waits_on_you_until_the_answer()
    {
        var events = new[]
        {
            E(40, 0, "lead", "added as Ready"),
            E(40, 50, "dev", "Ready → In progress"),
            E(40, 100, "dev", "In progress → Ready"),
            E(40, 100, "dev", "handed back with a question"),
            E(40, 180, "dev", "question answered"),
            E(40, 190, "dev", "Ready → In progress"),
            E(40, 200, "dev", "In progress → In review"),
            E(40, 300, "you", "accepted · PR #940 merged"),
        };
        Assert.Equal((80 + 100) * 60, WithYou(Trends.Cycle(events, Trends.Waits(events))));
    }

    [Fact]
    public void Cycles_come_oldest_acceptance_first_and_an_item_with_none_is_left_out()
    {
        var cycles = Trends.Cycles(
        [
            E(1, 0, "lead", "added as Ready"),
            E(2, 0, "lead", "added as Ready"),
            E(3, 0, "dev", "Ready → In progress"),
            E(2, 10, "you", "accepted · closed"),
            E(1, 20, "you", "accepted · closed"),
            E(3, 30, "you", "accepted · closed"),
        ], []);
        Assert.Equal([At(10), At(20)], cycles.Select(cycle => Json.Text(cycle!["accepted"])));
    }

    [Fact]
    public void Touches_count_your_comments_on_a_task_or_its_pr_from_review_onwards()
    {
        List<Happening> said =
        [
            E(4, 0, "dev", "In progress → In review"),
            E(4, 10, "you", "commented"),
            E(104, 20, "you", "commented"),
            E(4, 30, "octocat", "commented"),
            E(4, 40, "you", "accepted · PR #104 merged"),
            E(104, 40, "you", "accepted · PR #104 merged"),
            E(5, -10, "you", "commented"),
            E(5, 0, "dev", "In progress → In review"),
            E(5, 40, "you", "accepted · closed"),
        ];
        var touches = Trends.Touches(said, [4, 5], Fortnight);
        Assert.Equal("""[{"accepted":"2026-10-10T00:40:00Z","touches":2},{"accepted":"2026-10-10T00:40:00Z","touches":0}]""", Json.Compact(touches));
    }

    [Fact]
    public void Acceptance_before_the_fortnight_or_of_what_isnt_a_task_doesnt_count()
    {
        List<Happening> said = [E(4, 40, "you", "accepted · closed"), E(5, 40, "you", "accepted · closed")];
        Assert.Empty(Trends.Touches(said, [5], At(100)));
        Assert.Single(Trends.Touches(said, [5], Fortnight));
    }

    [Fact]
    public void A_pitch_you_approved_with_nothing_to_say_and_never_commented_under_is_untouched()
    {
        List<Happening> said =
        [
            E(9, 0, "lead", "Exploring → Pitched"),
            E(9, 10, "you", "Pitched → Approved"),
            E(9, 20, "you", "commented"),
            E(9, 90, "lead", "closed as done"),
        ];
        Assert.Equal("""[{"at":"2026-10-10T01:30:00Z","untouched":true}]""", Json.Compact(Trends.Finished(said, Tasks(), Fortnight)));
    }

    [Theory]
    [InlineData(9, 5, "you", "commented")]
    [InlineData(20, 40, "you", "commented")]
    [InlineData(120, 40, "you", "commented")]
    [InlineData(130, 40, "you", "commented")]
    public void A_comment_while_pitched_or_on_a_task_its_pr_or_a_pr_named_at_closing_touches_it(long item, int minutes, string who, string what)
    {
        List<Happening> said =
        [
            E(9, 0, "lead", "Exploring → Pitched"),
            E(9, 10, "you", "Pitched → Approved"),
            E(20, 50, "you", "accepted · PR #120 merged"),
            E(9, 90, "lead", "closed as done · built by #130"),
            E(item, minutes, who, what),
        ];
        said = said.OrderBy(e => e.At, StringComparer.Ordinal).ToList();
        Assert.False(Trends.Finished(said, Tasks((20, 9)), Fortnight)[0]!["untouched"]!.GetValue<bool>());
    }

    [Fact]
    public void A_pitch_never_approved_isnt_untouched()
    {
        List<Happening> said = [E(9, 0, "lead", "Exploring → Pitched"), E(9, 90, "lead", "closed as done")];
        Assert.False(Trends.Finished(said, Tasks(), Fortnight)[0]!["untouched"]!.GetValue<bool>());
    }
}

public class RecordRowsTests
{
    [Fact]
    public void Each_column_is_in_its_own_json_type_as_sqlite3_json_prints_it()
    {
        using var record = Record.Open(":memory:");
        var rows = record.Rows("SELECT 1 AS n, 'it''s' AS s, NULL AS z, 1.0 AS whole, 1.25 AS part, json_array(1) AS text;");
        Assert.Equal("""[{"n":1,"s":"it's","z":null,"whole":1.0,"part":1.25,"text":"[1]"}]""", Json.Compact(rows));
    }

    [Fact]
    public void No_rows_is_an_empty_array()
    {
        using var record = Record.Open(":memory:");
        Assert.Empty(record.Rows("SELECT 1 AS n WHERE 0;"));
    }
}

public class SpendingTests
{
    private static JsonNode Card(long n, long? parent = null, bool pitch = false, string? theme = null)
    {
        var labels = new JsonArray();
        if (pitch) labels.Add((JsonNode?)"pitch");
        return new JsonObject
        {
            ["number"] = n,
            ["title"] = $"#{n}",
            ["url"] = $"u/{n}",
            ["status"] = "Done",
            ["type"] = "Issue",
            ["parent"] = parent,
            ["labels"] = labels,
            ["marks"] = theme is null ? new JsonObject() : new JsonObject { ["theme"] = theme },
        };
    }

    [Fact]
    public void The_cards_run_on_come_with_the_pitches_over_them_and_nothing_else()
    {
        var runs = Json.Parse("""[{"started":"a","role":"dev","cost":1.25,"items":[517,999]},{"started":"b","role":"lead","cost":0,"items":[]}]""")!.AsArray();
        var spent = Commands.SpentOn(runs, [Card(81, pitch: true, theme: "Think bigger"), Card(517, parent: 81), Card(90)]);
        Assert.Equal("a", Json.Text(spent["since"]));
        Assert.Equal(2, spent["runs"]!.AsArray().Count);
        Assert.Equal("""[{"number":81,"title":"#81","url":"u/81","status":"Done","parent":null,"pitch":true,"theme":"Think bigger"},{"number":517,"title":"#517","url":"u/517","status":"Done","parent":81,"pitch":false,"theme":null}]""", Json.Compact(spent["items"]));
    }
}
