namespace ATeam.Dashboard.Tests;

public class TeamHealthTests
{
    [Fact]
    public void Each_problem_line_check_prints_is_a_problem_and_its_ok_lines_are_not()
    {
        const string output = """
            ok: mentaldesk project 3, field 'Status'
            vision    docs/vision.md isn't in mentaldesk/goose yet: the Lead will draft one and open it as a draft PR
            labels    no 'pitch' label, so the team can't tell its pitches from tasks
            identity: a-team-app[bot] · token ok · project 3 read+write ok · Priority readable · push access to mentaldesk/goose ok
            """;

        var health = TeamHealth.Parse(new Reading(output, "vision    docs/vision.md isn't in mentaldesk/goose yet"));

        Assert.Equal(
            [
                new TeamProblem("vision", "docs/vision.md isn't in mentaldesk/goose yet: the Lead will draft one and open it as a draft PR"),
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
            new TeamProblem("vision", "docs/vision.md isn't in mentaldesk/goose yet"),
            new TeamProblem("checkout", "~/code/goose/main isn't there"),
            new TeamProblem("labels", "no 'pitch' label"),
            new TeamProblem("status", "2 of 9 options missing"),
        ]);

        Assert.Equal(["checkout", "status"], health.Fatal.Select(problem => problem.Topic));
        Assert.Equal("tuicode: 2 checks failed — checkout, status", health.Failed("tuicode"));
    }

    [Fact]
    public void A_team_with_only_notes_or_one_fatal_problem_says_so_in_kind()
    {
        Assert.Null(new TeamHealth([new TeamProblem("labels", "no 'pitch' label")]).Failed("goose"));
        Assert.Equal(
            "goose: 1 check failed — app",
            new TeamHealth([new TeamProblem("app", "no key")]).Failed("goose"));
        Assert.Equal(
            "goose: 2 checks failed — app",
            new TeamHealth([new TeamProblem("app", "no key"), new TeamProblem("app", "no push")]).Failed("goose"));
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
