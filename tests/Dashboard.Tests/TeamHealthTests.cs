namespace ATeam.Dashboard.Tests;

public class TeamHealthTests
{
    [Fact]
    public void Each_problem_line_check_prints_is_a_problem_and_its_ok_lines_are_not()
    {
        const string output = """
            ok: mentaldesk project 3, field 'Status'
            vision    docs/vision.md isn't in mentaldesk/goose yet: the Lead will draft one and open it as a draft PR
            labels    3 of 6 missing: pitch, a-team:idea, a-team:skipped
            identity: a-team-app[bot] · token ok · project 3 read+write ok · Priority readable · push access to mentaldesk/goose ok
            """;

        var health = TeamHealth.Parse(new Reading(output, "vision    docs/vision.md isn't in mentaldesk/goose yet"));

        Assert.Equal(
            [
                new TeamProblem("vision", "docs/vision.md isn't in mentaldesk/goose yet: the Lead will draft one and open it as a draft PR"),
                new TeamProblem("labels", "3 of 6 missing: pitch, a-team:idea, a-team:skipped"),
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
}
