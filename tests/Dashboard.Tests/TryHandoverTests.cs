namespace ATeam.Dashboard.Tests;

/// <summary>What a try that failed says about it once the dashboard is back.</summary>
public class TryHandoverTests
{
    private static readonly WaitingItem Task = new(516, "A task", "In review", "https://x/516", "a-team", Pr: 518);

    private static TryHandover Tried(WaitingItem item) => new(item, false, [], null);

    [Fact]
    public void A_try_that_failed_says_why_in_try_s_own_words()
    {
        var back = Tried(Task).Open((arguments, environment) =>
        {
            Assert.Equal(["try", "a-team", "518"], arguments);
            File.WriteAllText(environment["A_TEAM_TRY_FAILED"], "could not fetch pull/518/head in ~/code/a-team/main\n");
            return "try a-team 518 exited 1";
        });

        Assert.Equal("try of #518 failed: could not fetch pull/518/head in ~/code/a-team/main", back.Failure);
    }

    [Fact]
    public void One_that_said_nothing_says_how_it_exited()
    {
        var back = Tried(Task).Open((_, _) => "try a-team 518 exited 2");

        Assert.Equal("try a-team 518 exited 2", back.Failure);
    }

    [Fact]
    public void One_that_worked_says_nothing_and_leaves_no_file_behind()
    {
        string? file = null;
        var back = Tried(Task).Open((_, environment) =>
        {
            file = environment["A_TEAM_TRY_FAILED"];
            return null;
        });

        Assert.Null(back.Failure);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void A_try_of_the_default_branch_names_the_team()
    {
        var back = Tried(Task with { Pr = 0 }).Open((_, environment) =>
        {
            File.WriteAllText(environment["A_TEAM_TRY_FAILED"], "could not read origin's default branch");
            return "try a-team exited 1";
        });

        Assert.Equal("try of a-team failed: could not read origin's default branch", back.Failure);
    }
}
