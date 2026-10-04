namespace ATeam.Dashboard.Tests;

public class TeamChecksTests
{
    private readonly Dictionary<string, string> _files = new() { ["a-team"] = "{}", ["tuicode"] = "{}" };
    private readonly List<string> _checked = [];
    private readonly Dictionary<string, TaskCompletionSource<TeamHealth>> _pending = [];

    private TeamChecks Checks() => new(
        team =>
        {
            _checked.Add(team);
            var answer = new TaskCompletionSource<TeamHealth>();
            _pending[team] = answer;
            return answer.Task;
        },
        team => _files.GetValueOrDefault(team));

    [Fact]
    public void Each_team_is_checked_once_and_a_refresh_checks_none_again()
    {
        var checks = Checks();

        checks.Follow(["a-team", "tuicode"]);
        checks.Follow(["a-team", "tuicode"]);
        checks.Follow(["a-team", "tuicode"]);

        Assert.Equal(["a-team", "tuicode"], _checked);
    }

    [Fact]
    public void A_change_to_a_team_s_file_checks_that_team_again_and_no_other()
    {
        var checks = Checks();
        checks.Follow(["a-team", "tuicode"]);

        _files["tuicode"] = """{"checkout": "~/code/TuiCode/main"}""";
        checks.Follow(["a-team", "tuicode"]);
        checks.Follow(["a-team", "tuicode"]);

        Assert.Equal(["a-team", "tuicode", "tuicode"], _checked);
    }

    [Fact]
    public void Nothing_is_wrong_until_the_check_answers_and_then_its_first_fatal_problem_is()
    {
        var checks = Checks();
        checks.Follow(["tuicode"]);
        Assert.Null(checks.Fatal("tuicode"));

        _pending["tuicode"].SetResult(new TeamHealth(
            [new TeamProblem("vision", "not yet"), new TeamProblem("checkout", "isn't there"), new TeamProblem("status", "missing")]));

        Assert.Equal(new TeamProblem("checkout", "isn't there"), checks.Fatal("tuicode"));
    }

    [Fact]
    public void A_team_checked_again_keeps_what_it_last_said_until_the_new_check_answers()
    {
        var checks = Checks();
        checks.Follow(["tuicode"]);
        _pending["tuicode"].SetResult(new TeamHealth([new TeamProblem("status", "missing")]));
        Assert.Equal("status", checks.Fatal("tuicode")?.Topic);

        checks.Check("tuicode");
        Assert.Equal("status", checks.Fatal("tuicode")?.Topic);

        _pending["tuicode"].SetResult(new TeamHealth([]));
        Assert.Null(checks.Fatal("tuicode"));
    }

    [Fact]
    public void A_check_that_answers_with_fatal_problems_is_announced_once()
    {
        var checks = Checks();
        checks.Follow(["a-team", "tuicode"]);
        Assert.Empty(checks.Answered());

        _pending["a-team"].SetResult(new TeamHealth([new TeamProblem("labels", "no 'pitch' label")]));
        _pending["tuicode"].SetResult(new TeamHealth([new TeamProblem("checkout", "isn't there"), new TeamProblem("status", "missing")]));

        Assert.Equal(["tuicode: 2 checks failed — checkout, status"], checks.Answered());
        Assert.Empty(checks.Answered());
    }

    [Fact]
    public void Checking_again_replaces_what_the_team_last_said()
    {
        var checks = Checks();
        checks.Follow(["tuicode"]);
        _pending["tuicode"].SetResult(new TeamHealth([new TeamProblem("status", "missing")]));

        checks.Check("tuicode");
        _pending["tuicode"].SetResult(new TeamHealth([]));

        Assert.Null(checks.Fatal("tuicode"));
        checks.Follow(["tuicode"]);
        Assert.Equal(["tuicode", "tuicode"], _checked);
    }

    [Fact]
    public void Asking_for_a_team_already_checked_shares_its_check()
    {
        var checks = Checks();
        checks.Follow(["tuicode"]);

        Assert.Same(checks.Latest("tuicode"), checks.For("tuicode"));
        Assert.Single(_checked);
    }
}
