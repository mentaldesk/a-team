namespace ATeam.Dashboard.Tests;

public class TeamHealthTests
{
    [Fact]
    public void Each_problem_line_check_prints_is_a_problem_and_its_ok_lines_are_not()
    {
        const string output = """
            ok: mentaldesk project 3, field 'Status'
            note     vision    docs/vision.md isn't in mentaldesk/goose yet: the Lead will draft one and open it as a draft PR
            problem  labels    no 'pitch' label, so the team can't tell its pitches from tasks
            identity: a-team-app[bot] · token ok · project 3 read+write ok · Priority readable · push access to mentaldesk/goose ok
            """;

        var health = TeamHealth.Parse(new Reading(output, "note     vision    docs/vision.md isn't in mentaldesk/goose yet"));

        Assert.Equal(
            [
                new TeamProblem("vision", "docs/vision.md isn't in mentaldesk/goose yet: the Lead will draft one and open it as a draft PR", IsNote: true),
                new TeamProblem("labels", "no 'pitch' label, so the team can't tell its pitches from tasks"),
            ],
            health.Problems);
        Assert.Equal("2 problems", health.Column);
    }

    [Fact]
    public void A_healthy_team_is_ok()
    {
        var health = TeamHealth.Parse(new Reading("ok: mentaldesk project 3, field 'Status'\nidentity: a-team-app[bot] · token ok\n", null));

        Assert.Empty(health.Problems);
        Assert.Equal("ok", health.Column);
        Assert.False(health.CanRepair);
    }

    [Fact]
    public void A_check_that_fails_without_naming_a_problem_is_the_problem()
    {
        var health = TeamHealth.Parse(new Reading("", "board.sh: no config for team 'goose'"));

        Assert.Equal([new TeamProblem("check", "board.sh: no config for team 'goose'")], health.Problems);
        Assert.Equal("1 problem", health.Column);
    }

    [Fact]
    public void Only_problems_that_stop_the_team_running_are_fatal_and_notes_are_not()
    {
        var health = new TeamHealth(
        [
            new TeamProblem("vision", "docs/vision.md isn't in mentaldesk/goose yet", IsNote: true),
            new TeamProblem("checkout", "~/code/goose/main isn't there"),
            new TeamProblem("labels", "no 'pitch' label", IsNote: true),
            new TeamProblem("status", "2 of 9 options missing"),
        ]);

        Assert.Equal(["checkout", "status"], health.Fatal.Select(problem => problem.Topic));
        Assert.Equal("tuicode: 2 checks failed — checkout, status", health.Failed("tuicode"));
    }

    [Theory]
    [InlineData("release", "release is continuous, but mentaldesk/goose has no release.yml workflow for it to run")]
    [InlineData("release", "release is 'weekly': expected never, daily or continuous")]
    [InlineData("project", "goose.json names no GitHub Project to copy the board to: pick one, or None, under Mirror in its settings")]
    [InlineData("anything", "a topic the app has never heard of")]
    public void A_note_stops_nothing_whatever_it_is_about_and_is_still_listed(string topic, string detail)
    {
        var health = TeamHealth.Parse(new Reading($"note     {topic.PadRight(10)}{detail}", null));

        Assert.Equal([new TeamProblem(topic, detail, IsNote: true)], health.Problems);
        Assert.Empty(health.Fatal);
        Assert.Null(health.Failed("goose"));
        Assert.Equal("1 problem", health.Column);
    }

    [Fact]
    public void A_topic_can_be_a_problem_on_one_line_and_a_note_on_another()
    {
        var health = TeamHealth.Parse(new Reading("""
            note     project   goose.json names no GitHub Project to copy the board to
            problem  project   can't read aaif-goose project 2: Not Found
            """, null));

        Assert.Equal("project", Assert.Single(health.Fatal).Topic);
        Assert.Equal("can't read aaif-goose project 2: Not Found", health.Fatal[0].Detail);
    }

    [Fact]
    public void Project_notes_are_counted_and_repairable_but_fail_no_check()
    {
        var health = TeamHealth.Parse(new Reading("""
            note     project   can't read aaif-goose project 2: Not Found
            note     status    2 of 9 options missing from 'Status': Exploring, Pitched
            note     items     outside the team's states: #5 Backlog
            note     app       goose-app[bot] can only read project 2: grant the organisation's "Projects: read and write"
            """, null));

        Assert.Null(health.Failed("goose"));
        Assert.Equal("4 problems", health.Column);
        Assert.True(health.CanRepair);
    }

    [Fact]
    public void A_problem_beside_a_note_is_the_reason_given_and_the_note_is_not()
    {
        var health = TeamHealth.Parse(new Reading("""
            note     release   release is 'weekly': expected never, daily or continuous
            problem  workdir   ~/code/goose isn't there, so the agents would have nothing to work in
            """, null));

        Assert.Equal("workdir", Assert.Single(health.Fatal).Topic);
        Assert.Equal("goose: 1 check failed — workdir", health.Failed("goose"));
        Assert.Equal("2 problems", health.Column);
    }

    [Fact]
    public void A_docs_page_not_written_yet_is_a_note_that_Health_does_not_count()
    {
        var health = TeamHealth.Parse(new Reading("""
            note     docs      docs/guide/index.md isn't in mentaldesk/goose yet: the Customer lead will propose the docs as a draft PR
            note     labels    no 'pitch' label, so the team can't tell its pitches from tasks
            """, null));

        Assert.Equal("1 problem", health.Column);
        Assert.Empty(health.Fatal);
        Assert.Equal("ok", new TeamHealth([health.Problems[0]]).Column);
        Assert.Equal("1 problem", new TeamHealth([new TeamProblem("docs", "goose.json names no docs page")]).Column);
    }

    [Fact]
    public void GitHub_not_answering_for_the_docs_page_is_a_note_that_Health_counts()
    {
        var health = TeamHealth.Parse(new Reading("""
            note     docs      GitHub didn't answer for docs/guide/index.md: checking again next pass
            """, null));

        Assert.Equal("1 problem", health.Column);
        Assert.Empty(health.Fatal);
    }

    [Fact]
    public void A_team_with_only_notes_or_one_fatal_problem_says_so_in_kind()
    {
        Assert.Null(new TeamHealth([new TeamProblem("labels", "no 'pitch' label", IsNote: true)]).Failed("goose"));
        Assert.Equal(
            "goose: 1 check failed — app",
            new TeamHealth([new TeamProblem("app", "no key")]).Failed("goose"));
        Assert.Equal(
            "goose: 2 checks failed — app",
            new TeamHealth([new TeamProblem("app", "no key"), new TeamProblem("app", "no push")]).Failed("goose"));
    }

    [Fact]
    public void A_board_too_new_for_this_a_team_says_why_and_the_fix_on_the_bar_and_in_the_panel()
    {
        var problem = new TeamProblem("board", "the board is in format 2, from a-team 0.4.0, and this is 0.3.0: brew upgrade a-team");

        Assert.Equal(
            "goose: stopped: brew upgrade a-team · the board is in format 2, from a-team 0.4.0, and this is 0.3.0",
            new TeamHealth([problem, new TeamProblem("app", "no key")]).Failed("goose"));
        Assert.Equal(
            "stopped: the board is in format 2, from a-team 0.4.0, and this is 0.3.0: brew upgrade a-team",
            RoleWatch.Misconfigured(problem));
    }

    [Theory]
    [InlineData("status", true)]
    [InlineData("labels", true)]
    [InlineData("project", false)]
    [InlineData("app", false)]
    [InlineData("vision", false)]
    public void Only_what_setting_the_board_up_puts_right_can_be_repaired(string topic, bool repairable) =>
        Assert.Equal(repairable, new TeamHealth([new TeamProblem(topic, "wrong")]).CanRepair);

    [Fact]
    public void A_problem_reads_as_its_topic_in_a_column_then_what_s_wrong() =>
        Assert.Equal(
            "project   no single-select field 'Status' on aaif-goose project 2",
            new TeamProblem("project", "no single-select field 'Status' on aaif-goose project 2").ToString());

    [Fact]
    public void A_problem_too_wide_for_its_rows_wraps_its_detail_under_itself() =>
        Assert.Equal(
            ["labels    mentaldesk/goose has no", "          'blocked' label"],
            new TeamProblem("labels", "mentaldesk/goose has no 'blocked' label").Rows(34));

    [Fact]
    public void A_problem_that_fits_or_has_no_room_to_wrap_is_one_row()
    {
        var problem = new TeamProblem("labels", "mentaldesk/goose has no 'blocked' label");

        Assert.Equal([problem.ToString()], problem.Rows(80));
        Assert.Equal([problem.ToString()], problem.Rows(0));
    }
}
