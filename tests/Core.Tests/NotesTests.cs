using System.Text.Json.Nodes;
using Xunit;

namespace ATeam.Core.Tests;

public class NotesTests
{
    private static Board Demo(Record record, int ideas = 4)
    {
        var config = new JsonObject
        {
            ["repo"] = "mentaldesk/demo", ["stakeholders"] = new JsonArray("reviewer"), ["wip"] = new JsonObject { ["ideas"] = ideas },
        };
        return new Board("demo", config, () => record, new Gh(null), dryRun: false);
    }

    private static void Run(Record record, long id, string role, params long[] items)
    {
        record.Exec($"INSERT INTO runs (id, team, role, pid, log, started, ended) VALUES ({id}, 'demo', '{role}', {id}, 'run{id}.jsonl', '2026-11-1{id % 10}T08:00:00Z', '2026-11-1{id % 10}T09:00:00Z');");
        foreach (var item in items)
            record.Exec($"INSERT INTO run_items (run, item) VALUES ({id}, {item});");
    }

    [Fact]
    public void The_list_is_read_from_the_end_of_the_summary()
    {
        const string summary = """
            Moved #12 to In review.

            Noticed:
            - The brief's `export` line can't carry between calls.
            - The a-team skill names a dashboard binary the build no longer makes,
              so its snapshot step fails.

            That's all.
            """;
        Assert.Equal(
            ["The brief's `export` line can't carry between calls.", "The a-team skill names a dashboard binary the build no longer makes, so its snapshot step fails."],
            Notes.Parse(summary));
    }

    [Theory]
    [InlineData("Done.\n\nNoticed: none")]
    [InlineData("Done.\n\n**Noticed:** nothing.")]
    [InlineData("Done.\n\n## Noticed\n\n- None")]
    [InlineData("Done. Noticed that the build is slow.")]
    [InlineData("Done, and nothing else to say.")]
    [InlineData("")]
    [InlineData(null)]
    public void An_empty_or_missing_list_is_no_notes(string? summary)
    {
        Assert.Empty(Notes.Parse(summary));
    }

    [Fact]
    public void A_list_on_its_own_line_and_a_numbered_list_both_count()
    {
        Assert.Equal(["One thing"], Notes.Parse("**Noticed**: One thing"));
        Assert.Equal(["First", "Second"], Notes.Parse("### Noticed\n1. First\n2) Second"));
    }

    [Fact]
    public void An_indented_sub_bullet_stays_with_the_note_above()
    {
        Assert.Equal(
            ["The build is slow; restore takes a minute", "Second"],
            Notes.Parse("Noticed:\n- The build is slow\n  - restore takes a minute\n- Second"));
        Assert.Equal(["First", "Second"], Notes.Parse("Noticed:\n  - First\n  - Second"));
    }

    [Fact]
    public void Only_the_last_list_is_the_runs()
    {
        Assert.Equal(["later"], Notes.Parse("Noticed:\n- earlier\n\nThen more work.\n\nNoticed:\n- later"));
    }

    [Fact]
    public void A_run_cut_short_has_no_summary()
    {
        var log = Path.GetTempFileName();
        try
        {
            File.WriteAllText(log, """{"type":"assistant","message":{"content":[{"type":"text","text":"Noticed:\n- half"}]}}""" + "\n");
            Assert.Null(Notes.FinalSummary(log));
            File.AppendAllText(log, """{"type":"result","result":"Done.\n\nNoticed:\n- whole"}""" + "\n");
            Assert.Equal(["whole"], Notes.Parse(Notes.FinalSummary(log)));
        }
        finally
        {
            File.Delete(log);
        }
    }

    [Fact]
    public void Notes_are_kept_once_against_their_run_role_and_card()
    {
        using var record = Record.Open(":memory:");
        var board = Demo(record);
        Run(record, 1, "dev", 801);
        Assert.Equal(2, board.KeepNotes(1, ["a", "b"]));
        Assert.Equal(0, board.KeepNotes(1, ["a", "b"]));
        var notes = board.UnhandledNotes();
        Assert.Equal(["a", "b"], notes.Select(note => note.Text));
        Assert.All(notes, note => Assert.Equal(("dev", (long?)801, 1L), (note.Role, note.Card, note.Run)));
    }

    [Fact]
    public void A_run_on_several_cards_is_on_none_of_them_as_evidence()
    {
        using var record = Record.Open(":memory:");
        var board = Demo(record);
        Run(record, 1, "lead", 10, 11);
        board.KeepNotes(1, ["a"]);
        Assert.Null(Assert.Single(board.UnhandledNotes()).Card);
    }

    [Fact]
    public void Evidence_counts_each_run_on_a_card_that_noticed_it_more_than_once()
    {
        using var record = Record.Open(":memory:");
        var board = Demo(record);
        Run(record, 1, "dev", 801);
        Run(record, 2, "dev", 801);
        Run(record, 3, "dev", 818);
        board.KeepNotes(1, ["Same thing"]);
        board.KeepNotes(2, ["same thing"]);
        board.KeepNotes(3, ["Same thing."]);
        var group = Assert.Single(Notes.Group(board.UnhandledNotes()));
        Assert.Equal("Noticed by the Dev on #801 twice and #818", Notes.Evidence(group));
    }

    [Fact]
    public void Notes_that_say_the_same_thing_are_one_group_saying_who_noticed_it_and_how_often()
    {
        using var record = Record.Open(":memory:");
        var board = Demo(record);
        Run(record, 1, "dev", 801);
        Run(record, 2, "dev", 818);
        Run(record, 3, "lead");
        Run(record, 4, "customer");
        board.KeepNotes(1, ["The `export` line can't carry between calls."]);
        board.KeepNotes(2, ["the export line can't carry between calls"]);
        board.KeepNotes(3, ["The export line can't carry between calls!", "Something else"]);
        var groups = Notes.Group(board.UnhandledNotes());
        Assert.Equal(2, groups.Count);
        Assert.Equal("Noticed by the Dev on #801 and #818, and the Lead once", Notes.Evidence(groups[0]));
        Assert.Equal("Noticed by the Lead once", Notes.Evidence(groups[1]));
        var brief = Notes.Brief("demo", groups, 2);
        Assert.StartsWith("## Noticed since the last Lead run (2 groups, 4 notes)", brief);
        Assert.Contains("1. Dev ×2, Lead ×1: The `export` line can't carry between calls.", brief);
        Assert.Contains("notes: 1 2 3 · runs: #801 11 Nov · #818 12 Nov · Lead 13 Nov", brief);
        Assert.Contains("Room for 2 more Ideas", brief);
    }

    [Fact]
    public void A_handled_note_leaves_the_brief_and_one_left_alone_stays()
    {
        using var record = Record.Open(":memory:");
        var board = Demo(record);
        Run(record, 1, "dev", 801);
        board.KeepNotes(1, ["drop me", "keep me"]);
        board.HandleNotes(board.NotesToHandle(["1"]), "dropped", null, "already fixed");
        Assert.Equal(["keep me"], board.UnhandledNotes().Select(note => note.Text));
        var shown = board.NotesOfRuns([1])[1];
        Assert.Equal("dropped", Json.Text(shown[0]?["handled"]));
        Assert.Equal("already fixed", Json.Text(shown[0]?["why"]));
        Assert.Null(shown[1]?["handled"]);
    }

    [Fact]
    public void A_note_is_handled_once_and_only_the_teams_own()
    {
        using var record = Record.Open(":memory:");
        var board = Demo(record);
        Run(record, 1, "dev", 801);
        record.Exec("INSERT INTO runs (id, team, role, pid, log, started) VALUES (9, 'other', 'dev', 9, 'x', '2026-11-11T08:00:00Z');");
        board.KeepNotes(1, ["a"]);
        record.Exec("INSERT INTO notes (run, n, text) VALUES (9, 1, 'theirs');");
        board.HandleNotes(board.NotesToHandle(["1"]), "answered", null, "yes");
        Assert.Equal("note 1 is answered already", Assert.Throws<Die>(() => board.NotesToHandle(["1"])).Message);
        Assert.Equal("note 2 isn't one of demo's", Assert.Throws<Die>(() => board.NotesToHandle(["2"])).Message);
    }

    [Fact]
    public void An_Ideas_evidence_goes_under_its_Evidence_heading_or_a_new_one()
    {
        Assert.Equal("## Opportunity\n\nX\n\n## Evidence\n\nNoticed by the Lead once.\n\n- a link\n\n<!-- a-team:lead -->",
            Notes.WithEvidence("## Opportunity\n\nX\n\n## Evidence\n\n- a link\n\n<!-- a-team:lead -->", "Noticed by the Lead once"));
        Assert.Equal("X\n\n**Evidence**\n\nNoticed by the Lead once.\n\n<!-- a-team:lead -->",
            Notes.WithEvidence("X\n\n<!-- a-team:lead -->", "Noticed by the Lead once"));
    }
}
