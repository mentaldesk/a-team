using Terminal.Gui.Drawing;

namespace ATeam.Dashboard.Tests;

public class DispatchPassTests : IDisposable
{
    private const string Fallback = "/apps/a-team/bin/a-team";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");
    private readonly List<(string Binary, string[] Arguments)> _ran = [];
    private TaskCompletionSource<Reading> _pass = new();

    public DispatchPassTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void It_runs_the_installed_dispatcher()
    {
        Install("/opt/homebrew/bin/a-team");

        Pass().Start();

        var (binary, arguments) = Assert.Single(_ran);
        Assert.Equal("/opt/homebrew/bin/a-team", binary);
        Assert.Equal(["dispatch"], arguments);
    }

    [Fact]
    public void A_dry_run_dispatcher_runs_a_dry_pass()
    {
        Install("/opt/homebrew/bin/a-team", dryRun: true);

        Pass().Start();

        Assert.Equal(["dispatch", "--dry-run"], Assert.Single(_ran).Arguments);
    }

    [Fact]
    public void With_nothing_installed_it_runs_the_apps_own_a_team()
    {
        Pass().Start();

        var (binary, arguments) = Assert.Single(_ran);
        Assert.Equal(Fallback, binary);
        Assert.Equal(["dispatch"], arguments);
    }

    [Fact]
    public void While_it_runs_it_says_so_and_has_nothing_to_report()
    {
        var pass = Pass();

        pass.Start();

        Assert.Equal("Running a pass…", pass.Progress);
        Assert.Null(pass.Finished());
    }

    [Fact]
    public void A_second_start_while_one_runs_starts_nothing_and_says_one_is_already_running()
    {
        var pass = Pass();
        pass.Start();

        pass.Start();

        Assert.Single(_ran);
        Assert.Equal("A pass is already running.", pass.Progress);
    }

    [Fact]
    public void A_pass_that_started_runs_says_how_many()
    {
        Log("2026-10-04T10:00:00Z a-team lead: started 41 on 0.1.12: an earlier pass");
        var pass = Pass();
        pass.Start();
        Log("2026-10-04T10:02:00Z a-team dev: started 42 on 0.1.12: #334: Ready task #334 to build",
            "2026-10-04T10:02:01Z a-team lead: started 43 on 0.1.12: new feedback");

        _pass.SetResult(new Reading("", null));

        Assert.Equal(("Pass done: started 2 runs.", Schemes.Accent), pass.Finished());
        Assert.Null(pass.Progress);
    }

    [Fact]
    public void A_pass_that_started_nothing_says_so()
    {
        Log("2026-10-04T10:00:00Z a-team lead: started 41 on 0.1.12: an earlier pass");
        var pass = Pass();
        pass.Start();
        Log("2026-10-04T10:02:00Z a-team dev: triggers failed: rate limited");

        _pass.SetResult(new Reading("", null));

        Assert.Equal(("Pass done: nothing to start.", Schemes.Accent), pass.Finished());
    }

    [Fact]
    public void A_pass_that_failed_says_its_first_line_in_the_Error_scheme_once()
    {
        var pass = Pass();
        pass.Start();

        _pass.SetResult(new Reading("", "dispatch.sh: line 12: jq: command not found"));

        Assert.Equal(("dispatch.sh: line 12: jq: command not found", Schemes.Error), pass.Finished());
        Assert.Null(pass.Finished());
    }

    [Fact]
    public void Once_finished_another_pass_can_start()
    {
        var pass = Pass();
        pass.Start();
        _pass.SetResult(new Reading("", null));
        pass.Finished();
        _pass = new TaskCompletionSource<Reading>();

        pass.Start();

        Assert.Equal(2, _ran.Count);
        Assert.Equal("Running a pass…", pass.Progress);
    }

    private DispatchPass Pass() => new(_root, Fallback, (binary, arguments) =>
    {
        _ran.Add((binary, arguments));
        return _pass.Task;
    });

    private void Install(string bin, bool dryRun = false) =>
        File.WriteAllText(Path.Combine(_root, "dispatcher.json"), $$"""
            { "bin": "{{bin}}", "version": "0.1.12", "dryRun": {{(dryRun ? "true" : "false")}}, "interval": 120, "installedAt": 0 }
            """);

    private void Log(params string[] lines) => File.AppendAllLines(Path.Combine(_root, "dispatch.log"), lines);
}
