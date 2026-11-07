using MentalDesk.Tui;
using System.Drawing;
using System.Text;
using MentalDesk.Tui.Links;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

[Collection("StaticConfiguration")]
public partial class DashboardWindowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");
    private readonly PlatformKeyBinding _quit = Application.DefaultKeyBindings![Command.Quit];

    public void Dispose()
    {
        Application.SetDefaultKeyBinding(Command.Quit, _quit);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_team_whose_check_fails_says_so_in_its_panel_on_each_role_and_once_in_the_message_bar()
    {
        var answer = new TaskCompletionSource<TeamHealth>();
        var ran = 0;
        var checks = new TeamChecks(_ => { ran++; return answer.Task; }, _ => "{}");
        using var window = Open(checks: checks);
        SelectAgent(window, 0);
        Assert.Equal("", window.Team.Problem);
        Assert.All(window.Team.Lines, line => Assert.DoesNotContain("⚠", line));

        answer.SetResult(new TeamHealth([new TeamProblem("checkout", "~/code/a-team/main isn't there"), new TeamProblem("status", "missing")]));
        window.Refresh();

        Assert.Equal("misconfigured: checkout ~/code/a-team/main isn't there", window.Team.Problem);
        Assert.All(window.Team.Lines, line => Assert.StartsWith(Icons.Field(Icon.Misconfigured, IconStyle.Unicode), line));
        Assert.Equal("a-team: 2 checks failed — checkout, status", window.Says());
        Assert.Equal(Severity.Error, window.Status.MessageSeverity);
        window.Refresh();
        Assert.Equal(1, ran);
    }

    [Fact]
    public void A_failed_check_comes_off_the_bar_once_the_team_passes()
    {
        var answers = new Queue<TaskCompletionSource<TeamHealth>>([new(), new()]);
        var asked = new List<TaskCompletionSource<TeamHealth>>();
        var checks = new TeamChecks(_ =>
        {
            asked.Add(answers.Dequeue());
            return asked[^1].Task;
        }, _ => "{}");
        using var window = Open(checks: checks);
        window.Refresh();
        asked[0].SetResult(new TeamHealth([new TeamProblem("checkout", "~/code/a-team/main isn't there")]));
        window.Refresh();
        Assert.Equal("a-team: 1 check failed — checkout", window.Says());

        checks.Check("a-team");
        asked[1].SetResult(new TeamHealth([]));
        window.Refresh();

        Assert.Null(window.Status.Message);
        Assert.Equal(1, window.Status.UnreadErrors);
    }

    [Fact]
    public void The_apps_own_quit_binding_moves_to_the_quit_key_so_Esc_only_goes_back()
    {
        using var window = Open();

        Assert.Equal(Key.Q.WithCtrl, Application.GetDefaultKey(Command.Quit));
    }

    [Fact]
    public void Rebinding_quit_takes_the_apps_quit_binding_with_it()
    {
        using var window = Open(keys: "{ \"quit\": \"z\" }");

        Assert.Equal(new Key('z'), Application.GetDefaultKey(Command.Quit));
    }

    [Fact]
    public void Forgetting_a_removed_team_takes_its_roles_and_its_lanes_out_of_Work_and_Overseer()
    {
        using var window = Open(agents: Agents(6));

        window.Forget(["team0"]);

        Assert.Equal(["team1", "team1", "team2", "team2"], window.Watches.Select(watch => watch.Team));
        Assert.Equal(["team1", "team2"], window.Work.Lanes.Select(lane => lane.Team));
        Assert.Equal(["team1 · idle", "team2 · idle"], window.Overseer.LaneTitles);
    }

    [Theory]
    [InlineData(Area.Work)]
    [InlineData(Area.Overseer)]
    public void View_Dispatcher_log_opens_the_whole_log_full_screen_and_Esc_comes_back(Area area)
    {
        WriteDispatchLog([.. Enumerable.Range(1, 100).Select(n => $"2026-10-04T10:00:00Z a-team dev: line {n}")]);
        using var window = Open(area: area);
        LayOut(window, 120, 30);

        window.Commands.Execute("view.dispatcher");
        LayOut(window, 120, 30);
        window.Refresh();

        Assert.True(window.DispatcherOpen);
        Assert.Equal(new Rectangle(0, 1, window.Viewport.Width, window.Viewport.Height - 2), window.Dispatcher.Frame);
        Assert.False(window.Overseer.Visible || window.Work.Visible);
        Assert.Equal("Log", window.Region.Name);
        Assert.Equal(100, window.WholeDispatchLog.Lines.Count);
        Assert.Equal(ScrollBarVisibilityMode.Auto, window.WholeDispatchLog.VerticalScrollBar.VisibilityMode);
        Assert.True(window.WholeDispatchLog.Following);
        Assert.True(window.NewKeyDownEvent(Key.Home));
        Assert.False(window.WholeDispatchLog.Following);
        Assert.True(window.NewKeyDownEvent(Key.PageDown));
        Assert.True(window.NewKeyDownEvent(Key.End));
        Assert.True(window.WholeDispatchLog.Following);

        Assert.True(window.NewKeyDownEvent(Key.Esc));

        Assert.False(window.DispatcherOpen);
        Assert.False(window.Dispatcher.Visible);
        Assert.Equal(area, window.CurrentArea);
        Assert.True(area == Area.Work ? window.Work.Visible : window.Overseer.Visible);
    }

    [Fact]
    public void b_opens_the_selected_heading_s_team_board_and_says_so()
    {
        WriteTeam("team1", """{"project": {"owner": "someone", "number": 4, "ownerType": "user"}}""");
        var opened = new List<string>();
        using var window = Open(agents: Agents(4), openUrl: Opening.Into(opened));
        SelectAgent(window, 2);

        Assert.True(window.NewKeyDownEvent(new Key('b')));

        Assert.Equal(["https://github.com/users/someone/projects/4"], opened);
        Assert.Equal("Opened https://github.com/users/someone/projects/4", window.Says());
    }

    [Fact]
    public void A_pass_that_broke_before_it_could_log_says_so_on_the_bar_and_at_the_foot_of_the_dispatcher_s_log()
    {
        WriteDispatchLog("2026-10-04T10:00:00Z a-team dev: started 1 on 0.1.12");
        File.SetLastWriteTimeUtc(Path.Combine(_root, "dispatch.log"), DateTime.UtcNow.AddMinutes(-3));
        File.WriteAllLines(Path.Combine(_root, "launchd.log"), ["jq: parse error"]);
        using var window = Open(agents: Agents(2));
        window.Refresh();

        Assert.Equal("dispatcher: last pass failed · jq: parse error", window.Says());
        Assert.Equal(Severity.Error, window.Status.MessageSeverity);

        window.Commands.Execute("view.dispatcher");

        Assert.EndsWith(" · last pass failed", window.Dispatcher.Title);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), window.Dispatcher.SchemeName);
        Assert.Equal(
            [
                new LogLine("10:00 a-team dev: started 1 on 0.1.12", LogLineKind.Prose),
                new LogLine("── the dispatcher's own output since then ──", LogLineKind.DispatchFailed),
                new LogLine("jq: parse error", LogLineKind.DispatchFailed),
            ],
            window.WholeDispatchLog.Lines);

        WriteDispatchLog("2026-10-04T10:02:00Z a-team dev: started 2 on 0.1.12");
        window.Refresh();
        Assert.Null(window.Status.Message);
    }

    [Fact]
    public void Output_older_than_the_last_dispatch_line_shows_neither()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllLines(Path.Combine(_root, "launchd.log"), ["jq: parse error"]);
        File.SetLastWriteTimeUtc(Path.Combine(_root, "launchd.log"), DateTime.UtcNow.AddMinutes(-3));
        WriteDispatchLog("2026-10-04T10:00:00Z a-team dev: started 1 on 0.1.12");
        using var window = Open(agents: Agents(2));
        window.Refresh();

        Assert.Null(window.Status.Message);
        window.Commands.Execute("view.dispatcher");
        Assert.DoesNotContain("last pass failed", window.Dispatcher.Title);
        Assert.Single(window.WholeDispatchLog.Lines);
    }

    [Fact]
    public void Esc_with_nothing_full_screen_does_nothing_and_Ctrl_Q_is_the_way_out()
    {
        using var window = Open(agents: Agents(4));
        SelectAgent(window, 0);

        Assert.False(window.NewKeyDownEvent(Key.Esc));
        Assert.True(window.NewKeyDownEvent(Key.Q.WithCtrl));
    }

    [Fact]
    public void l_copies_the_highlighted_lines_in_full_and_says_so()
    {
        var clipboard = new FakeClipboard();
        WriteLog("a-team", "dev",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"reading"}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash","input":{"command":"cat <<'PY'\nprint(1)\nPY"}}]}}""");
        using var window = Open(clipboard: clipboard);
        window.Refresh();
        SelectAgent(window, 1);
        window.NewKeyDownEvent(Key.Enter);

        Assert.True(window.NewKeyDownEvent(new Key('l')));

        Assert.Equal("cat <<'PY'\nprint(1)\nPY", clipboard.GetClipboardData());
        Assert.Equal("Copied 3 lines  •  20 characters", window.Says());

        window.NewKeyDownEvent(Key.CursorUp);
        window.NewKeyDownEvent(Key.CursorUp.WithShift);
        window.NewKeyDownEvent(new Key('l'));
        Assert.Equal("reading\n", clipboard.GetClipboardData());
        Assert.Equal("Copied 1 line  •  7 characters", window.Says());
    }

    private static string Label(DashboardWindow window, string id) =>
        window.Commands.Registered.Single(command => command.Id == id).Label;

    /// <summary>Selects the agent's lane heading and chooses its role in the team's panel.</summary>
    private static void SelectAgent(DashboardWindow window, int index)
    {
        var watch = window.Watches[index];
        Assert.True(window.Overseer.SelectHeading(watch.Team));
        window.Refresh();
        window.Team.Choose(watch.Role);
        window.Refresh();
        Assert.Equal(watch.Role, window.Team.Role);
    }

    private static (string, string)[] Agents(int count) =>
        [.. Enumerable.Range(0, count).Select(i => ($"team{i / 2}", i % 2 == 0 ? "lead" : "dev"))];

    private static void LayOut(DashboardWindow window, int width, int height)
    {
        window.Frame = new Rectangle(0, 0, width, height);
        window.Layout(new Size(width, height));
    }

    private static void AssertOnlyKeysHint(DashboardWindow window)
    {
        LayOut(window, 120, 30);
        Assert.Equal("Press F1 for help", window.Status.Hints.Says);
        Assert.Equal(window.Status.FrameToScreen().Right - 1, window.Status.Hints.FrameToScreen().Right);
        Assert.DoesNotContain("a-team", window.Says());
    }

    private DashboardWindow Open(
        bool expandToolCalls = false,
        IReadOnlyList<(string, string)>? agents = null,
        Func<string[], Task<string?>>? run = null,
        string? keys = null,
        Func<string, Task<Reading>>? readWaiting = null,
        Func<string, LinkOutcome>? openUrl = null,
        Area area = Area.Overseer,
        IconStyle auto = IconStyle.Unicode,
        Action<Handover>? handOver = null,
        Handover? resume = null,
        Action<string>? showGuide = null,
        Func<RunTask, bool>? confirmStop = null,
        IClipboard? clipboard = null,
        TeamChecks? checks = null,
        DispatchPass? pass = null)
    {
        Directory.CreateDirectory(_root);
        if (keys is not null)
        {
            Directory.CreateDirectory(Config);
            File.WriteAllText(Path.Combine(Config, "dashboard.json"), $"{{ \"keys\": {keys} }}");
        }
        var settings = new DashboardSettings(Config);
        if (expandToolCalls)
            settings.WriteExpandToolCalls(true);
        return new DashboardWindow(
            agents ?? [("a-team", "lead"), ("a-team", "dev")],
            _root,
            settings,
            new TeamConfigs(Config),
            run ?? (_ => Task.FromResult<string?>(null)),
            readWaiting ?? (_ => Task.FromResult(new Reading("[]", null))),
            _ => Task.FromResult(new Reading("{}", null)),
            openUrl ?? Opening.Opened,
            (_, _, _, _, _, _, _, _, _, _, _, _) => null,
            area,
            auto,
            new DashboardOptions
            {
                HandOver = handOver,
                Resume = resume,
                ShowGuide = showGuide,
                ConfirmStop = confirmStop,
                Clipboard = clipboard,
                Checks = checks,
                Pass = pass,
            });
    }

    private string Config => Path.Combine(_root, "config");

    private void WriteDispatchLog(params string[] lines)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllLines(Path.Combine(_root, "dispatch.log"), lines);
    }

    private void WriteRunning(string team, string role)
    {
        var dir = Path.Combine(_root, team, role);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "pid"), Environment.ProcessId.ToString());
    }

    private void WriteRun(string team, string role, int task, long started)
    {
        var dir = Path.Combine(_root, team, role, "runs", task.ToString());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "pid"), Environment.ProcessId.ToString());
        File.WriteAllText(Path.Combine(dir, "last-start"), started.ToString());
        File.WriteAllText(Path.Combine(dir, "task"), $$"""{"number":{{task}},"title":"Task {{task}}"}""");
    }

    private void WriteSession(string team, string role, int? task = null)
    {
        var dir = task is { } n ? Path.Combine(_root, team, role, "runs", n.ToString()) : Path.Combine(_root, team, role);
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "latest.jsonl"),
            "{\"type\":\"system\",\"subtype\":\"init\",\"session_id\":\"sess-" + team + role + "\"}\n");
    }

    private void WriteLog(string team, string role, params string[] events)
    {
        var dir = Path.Combine(_root, team, role);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "latest.jsonl"), string.Join("\n", events) + "\n");
    }

    private void WriteRunHeld(string team, string role, int task)
    {
        var dir = Path.Combine(_root, team, role, "runs", task.ToString());
        File.WriteAllText(Path.Combine(dir, "pid"), "999999");
        File.WriteAllText(Path.Combine(dir, "held"), "100");
    }

    private void WriteTeam(string team, string config)
    {
        var teams = Path.Combine(Config, "teams");
        Directory.CreateDirectory(teams);
        File.WriteAllText(Path.Combine(teams, team + ".json"), config);
    }

    private void WriteHold(string team, string role)
    {
        var teams = Path.Combine(Config, "teams");
        Directory.CreateDirectory(teams);
        File.WriteAllText(
            Path.Combine(teams, team + ".json"),
            "{\"dispatch\": {\"enabled\": true, \"hold\": [\"" + role + "\"]}}");
    }
}
