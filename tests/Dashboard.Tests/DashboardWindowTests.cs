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
public class DashboardWindowTests : IDisposable
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

    [Fact]
    public void Cmd_C_copies_the_highlighted_lines_as_l_does()
    {
        var clipboard = new FakeClipboard();
        WriteLog("a-team", "dev", """{"type":"assistant","message":{"content":[{"type":"text","text":"reading"}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash","input":{"command":"ls"}}]}}""");
        using var window = Open(clipboard: clipboard);
        window.Refresh();
        SelectAgent(window, 1);
        window.NewKeyDownEvent(Key.C.WithCtrl);
        Assert.Equal("", clipboard.GetClipboardData());
        window.NewKeyDownEvent(Key.Enter);

        Assert.True(window.NewKeyDownEvent(Key.C.WithCtrl));

        Assert.Equal("ls", clipboard.GetClipboardData());
    }

    [Fact]
    public void a_copies_the_whole_log()
    {
        var clipboard = new FakeClipboard();
        WriteLog("a-team", "dev",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Read","input":{"file_path":"a"}},{"type":"tool_use","name":"Read","input":{"file_path":"b"}}]}}""");
        using var window = Open(clipboard: clipboard);
        window.Refresh();
        SelectAgent(window, 1);
        window.NewKeyDownEvent(Key.Enter);

        Assert.True(window.NewKeyDownEvent(new Key('a')));

        Assert.Equal("a\nb", clipboard.GetClipboardData());
    }

    [Fact]
    public void l_and_a_copy_through_the_installed_terminal_clipboard()
    {
        using var terminal = new InstalledTerminal();
        WriteLog("a-team", "dev",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Read","input":{"file_path":"a"}},{"type":"tool_use","name":"Read","input":{"file_path":"b"}}]}}""");
        using var window = Open(clipboard: terminal.Clipboard);
        window.Refresh();
        SelectAgent(window, 1);
        window.NewKeyDownEvent(Key.Enter);

        window.NewKeyDownEvent(new Key('l'));
        Assert.Equal("b", terminal.Platform.Text);
        Assert.Equal("Copied 1 line  •  1 character", window.Says());

        window.NewKeyDownEvent(new Key('a'));
        Assert.Equal("a\nb", terminal.Platform.Text);
        Assert.Equal("Copied 2 lines  •  2 characters", window.Says());
    }

    [Fact]
    public void e_writes_the_whole_session_to_a_file_and_hands_it_to_the_editor()
    {
        var handed = new List<Handover>();
        WriteLog("a-team", "dev",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"reading"}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash","input":{"command":"cat <<'PY'\nprint(1)\nPY"}}]}}""");
        using var window = Open(handOver: handed.Add);
        window.Refresh();
        SelectAgent(window, 1);

        Assert.False(window.Commands.IsEnabled("log.editor"));
        window.NewKeyDownEvent(Key.Enter);
        Assert.True(window.NewKeyDownEvent(new Key('e')));

        var handover = Assert.IsType<EditorHandover>(Assert.Single(handed));
        try
        {
            Assert.Equal(("a-team", "dev", Area.Overseer, null), (handover.Team, handover.Role, handover.Area, handover.Card));
            Assert.Equal(handover.File, handover.Arguments[^1]);
            Assert.Equal(["reading", "", "Bash cat <<'PY'", "print(1)", "PY"], File.ReadAllLines(handover.File));
        }
        finally
        {
            File.Delete(handover.File);
        }
    }

    [Fact]
    public void Back_from_the_editor_the_role_s_session_is_full_screen_where_you_left_it()
    {
        var handed = new List<Handover>();
        WriteLog("a-team", "dev", """{"type":"assistant","message":{"content":[{"type":"text","text":"one\ntwo\nthree"}]}}""");
        using (var window = Open(handOver: handed.Add))
        {
            window.Refresh();
            SelectAgent(window, 1);
            window.NewKeyDownEvent(Key.Enter);
            window.NewKeyDownEvent(Key.CursorUp);
            window.NewKeyDownEvent(Key.CursorUp);
            window.NewKeyDownEvent(Key.CursorUp.WithShift);
            window.NewKeyDownEvent(new Key('e'));
        }
        var handover = Assert.IsType<EditorHandover>(Assert.Single(handed));
        File.Delete(handover.File);

        using var back = Open(resume: handover);
        back.Refresh();
        LayOut(back, 120, 40);

        Assert.Equal(Area.Overseer, back.CurrentArea);
        Assert.True(back.SessionFull);
        Assert.Equal(("a-team", "dev"), (back.Overseer.Board.Selected?.Team, back.Team.Role));
        Assert.Equal("one\ntwo", back.Card.CopySelection().Text);
    }

    [Fact]
    public void An_editor_that_failed_says_so_in_red_once_you_re_back()
    {
        WriteLog("a-team", "dev", """{"type":"assistant","message":{"content":[{"type":"text","text":"one"}]}}""");
        var place = new LogPlace([], 0, true, 0, 0, false);
        using var window = Open(resume: new EditorHandover("a-team", "dev", place, "x.log", ["vim"]) { Failure = "vim exited 1" });
        window.Refresh();
        LayOut(window, 120, 40);

        Assert.True(window.SessionFull);
        Assert.Equal("vim exited 1", window.Says());
        Assert.Equal(Severity.Error, window.Status.MessageSeverity);
    }

    [Fact]
    public void With_no_clipboard_copying_says_so_in_red_and_nothing_moves()
    {
        WriteLog("a-team", "dev", """{"type":"assistant","message":{"content":[{"type":"text","text":"one\ntwo"}]}}""");
        using var window = Open(clipboard: new FakeClipboard(isSupportedAlwaysFalse: true));
        window.Refresh();
        SelectAgent(window, 1);
        window.NewKeyDownEvent(Key.Enter);
        window.NewKeyDownEvent(Key.CursorUp);

        window.NewKeyDownEvent(new Key('l'));

        Assert.Equal("Copy failed: there is no clipboard", window.Says());
        Assert.Equal(Severity.Error, window.Status.MessageSeverity);
        Assert.Equal("two", window.Card.CopySelection().Text);
    }

    [Fact]
    public void Every_action_the_dashboard_has_is_a_command_you_can_run_by_name()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal(
            [
                "Send question", "Stop answering", "Start a new conversation", "Next pane", "Previous pane",
                "Select the next line of the log", "Select the line above in the log", "Extend the selection down",
                "Extend the selection up", "Scroll the log up", "Scroll the log down",
                "Jump to the top of the log", "Jump to the bottom of the log", "Show tool calls in full",
                "Copy the selected lines", "Copy the whole log", "Open the whole log in your editor",
                "Select the column to the right", "Select the column to the left", "Select the card below",
                "Select the card above", "Open", "Set priority", "Try", "Copy branch name", "Open on GitHub", "Open team's board on GitHub",
                "Write the vision with me", "Review the vision", "Approve the pitch you're reading", "Accept",
                "Comment on the item you're reading", "Edit the document pitch you're reading", "Attach image file to what you're writing", "View image", "Decline", "Not now", "Hold",
                "Next team", "Previous team", "Go to team…", "New idea", "Show only what's your move", "Read this team again", "Read what's waiting again",
                "Select the chip to the right", "Select the chip to the left", "Select the chip below", "Select the chip above",
                "Select the next team's lane", "Select the previous team's lane", "Scroll the lanes down", "Scroll the lanes up",
                "Show the session full-screen", "Fold the lane", "Open on GitHub", "Read the boards that failed again", "Read every board again",
                "Choose the role below", "Choose the role above", "Work", "Overseer", "Assistant", "Details", "Dispatcher log", "Go to what's new",
                "Pause this role", "Interrupt the agent on this card", "Run a dispatch pass now", "Commands", "Settings", "Teams", "New team", "Show keys", "Guide", "Messages", "About", "Trends", "Spending", "Decisions…", "Back to Overseer", "Quit",
                "Go to team0", "Go to team1", "Go to team 3", "Go to team 4", "Go to team 5", "Go to team 6", "Go to team 7",
                "Go to team 8", "Go to team 9",
            ],
            window.Commands.Registered.Select(command => command.Label));
    }

    [Fact]
    public void Partway_through_a_chord_the_status_bar_shows_it_and_finishing_it_runs_the_command()
    {
        using var window = Open(agents: Agents(4));

        Assert.True(window.NewKeyDownEvent(Key.G.WithCtrl));
        Assert.Equal("Ctrl+G…", window.Status.Says);
        Assert.Equal(Area.Overseer, window.CurrentArea);

        window.NewKeyDownEvent(new Key('w'));

        Assert.Equal(Area.Work, window.CurrentArea);
        Assert.NotEqual("Ctrl+G…", window.Status.Says);
    }

    [Fact]
    public void A_key_that_finishes_no_chord_cancels_it_says_so_and_does_nothing_else()
    {
        using var window = Open(agents: Agents(4));
        SelectAgent(window, 0);

        window.NewKeyDownEvent(Key.G.WithCtrl);
        Assert.True(window.NewKeyDownEvent(Key.Tab));

        Assert.Equal("Ctrl+G Tab isn't a key", window.Says());
        Assert.Equal(Area.Overseer, window.CurrentArea);
        Assert.True(window.Overseer.Board.OnHeading);
    }

    [Fact]
    public void Esc_partway_through_a_chord_cancels_it()
    {
        using var window = Open(agents: Agents(4));

        window.NewKeyDownEvent(Key.G.WithCtrl);
        window.NewKeyDownEvent(Key.Esc);

        Assert.Equal("Ctrl+G cancelled", window.Says());
    }

    [Theory]
    [InlineData('d')]
    [InlineData('w')]
    [InlineData('o')]
    [InlineData('s')]
    [InlineData('n')]
    [InlineData('q')]
    [InlineData('u')]
    [InlineData('m')]
    public void The_bare_letters_the_defaults_dropped_do_nothing_now(char letter)
    {
        using var window = Open(agents: Agents(4));

        Assert.False(window.NewKeyDownEvent(new Key(letter)));
        Assert.Equal(Area.Overseer, window.CurrentArea);
    }

    [Fact]
    public void The_default_keys_follow_TuiCode_s_families()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal(
            [
                ("view.work", "Ctrl+G W"), ("view.overseer", "Ctrl+G O"), ("assistant", "Ctrl+G A"),
                ("work.team", "Ctrl+G T"), ("work.whatsNew", "Ctrl+G N"), ("log.toolCalls", "Ctrl+T T"), ("work.mine", "Ctrl+T M"),
                ("commands", "Ctrl+E"), ("settings", "Ctrl+,"), ("work.new", "Ctrl+N"), ("quit", "Ctrl+Q"), ("assistant.clear", "Ctrl+L"),
            ],
            new[] { "view.work", "view.overseer", "assistant", "work.team", "work.whatsNew", "log.toolCalls", "work.mine", "commands",
                "settings", "work.new", "quit", "assistant.clear" }.Select(id => (id, window.Commands.KeysFor(id))));
    }

    [Fact]
    public void The_card_and_log_keys_stay_single_letters()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal(["A", "T", "P", "X", "L", "H", "G", "Y", "C", "E", "I"],
            new[] { "work.accept", "work.try", "work.priority", "work.decline", "work.notNow", "work.hold", "work.github", "work.copyBranch", "work.comment",
                "log.editor", "agent.interrupt" }.Select(id => window.Commands.KeysFor(id)));
    }

    [Fact]
    public void Help_opens_with_F1()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal("F1", window.Commands.Registered.Single(c => c.Id == "help").Keys);
    }

    [Theory]
    [InlineData(Area.Work, GuideDialog.Work)]
    [InlineData(Area.Overseer, GuideDialog.Overseer)]
    public void Guide_opens_on_the_page_for_the_area_and_has_no_key(Area area, string page)
    {
        var opened = new List<string>();
        using var window = Open(agents: Agents(4), area: area, showGuide: opened.Add);

        window.Commands.Execute("guide");

        Assert.Equal([page], opened);
        Assert.Equal(Key.Empty, window.Commands.KeyFor("guide"));
    }

    [Fact]
    public void Settings_opens_with_Ctrl_comma_and_s_is_gone()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal("Ctrl+,", window.Commands.Registered.Single(c => c.Id == "settings").Keys);
        Assert.DoesNotContain(window.Commands.Registered, c => c.Keys == "S");
        Assert.False(window.NewKeyDownEvent(new Key('s')));
    }

    [Fact]
    public void A_key_the_file_names_runs_that_command_and_the_one_in_the_source_no_longer_does()
    {
        using var window = Open(keys: "{ \"view.work\": \"z\" }");

        Assert.True(window.NewKeyDownEvent(new Key('z')));
        Assert.Equal(Area.Work, window.CurrentArea);
        window.Commands.Execute("view.overseer");
        window.Press("Ctrl+G W");

        Assert.Equal(Area.Overseer, window.CurrentArea);
    }

    [Fact]
    public void A_command_the_file_says_nothing_about_keeps_the_key_it_had()
    {
        using var window = Open(keys: "{ \"log.toolCalls\": \"x\" }");

        Assert.Equal("F1", window.Commands.Registered.Single(command => command.Id == "help").Keys);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"nobody.registered.this\": \"F4\" }")]
    [InlineData("{ \"settings\": \"nonsense\" }")]
    public void An_override_we_cannot_use_is_ignored_on_its_own_and_leaves_every_default_alone(string keys)
    {
        using var window = Open(keys: keys);

        Assert.Equal("Ctrl+,", window.Commands.Registered.Single(command => command.Id == "settings").Keys);
        Assert.Equal("Ctrl+T T", window.Commands.KeysFor("log.toolCalls"));
    }

    [Theory]
    [InlineData("{ \"nobody.registered.this\": \"F4\", \"help\": \"F2\" }")]
    [InlineData("{ \"settings\": \"nonsense\", \"help\": \"F2\" }")]
    public void The_rest_of_the_file_still_applies_around_an_override_we_cannot_use(string keys)
    {
        using var window = Open(keys: keys);

        Assert.Equal("F2", window.Commands.Registered.Single(command => command.Id == "help").Keys);
    }

    [Fact]
    public void The_commands_list_names_the_key_the_file_bound()
    {
        using var window = Open(agents: Agents(4), keys: "{ \"commands\": \"Ctrl+K\" }");

        using var dialog = new CommandsDialog(window.Commands.Registered);

        Assert.Contains(dialog.Matches.Select(command => command.Keys), keys => keys == "Ctrl+K");
    }

    [Fact]
    public void The_status_bar_s_one_hint_is_press_F1_for_help_at_its_right_end_in_Overseer_a_full_screen_log_and_Work()
    {
        using var window = Open(agents: Agents(4));
        AssertOnlyKeysHint(window);

        SelectAgent(window, 0);
        AssertOnlyKeysHint(window);

        window.Commands.Execute("view.dispatcher");
        AssertOnlyKeysHint(window);

        window.Commands.Execute("view.work");
        AssertOnlyKeysHint(window);

        window.Commands.Execute("view.overseer");
        AssertOnlyKeysHint(window);
    }

    [Fact]
    public void The_status_bar_s_hint_follows_the_keys_command_s_key()
    {
        using var window = Open(agents: Agents(4), keys: "{ \"help\": \"F2\" }");

        Assert.Equal("Press F2 for help", window.Status.Hints.Says);
    }

    [Fact]
    public void The_status_bar_is_the_last_row_of_the_window_in_a_band_of_its_own()
    {
        using var window = Open(agents: Agents(4));

        LayOut(window, 120, 30);

        Assert.Equal(new Rectangle(0, window.Viewport.Height - 1, window.Viewport.Width, 1), window.Status.Frame);
        Assert.Equal(StatusBar.Scheme, window.Status.SchemeName);
        Assert.Null(window.Status.Message);
    }

    [Fact]
    public void What_is_running_shows_on_the_status_bar_and_takes_no_row_of_its_own()
    {
        using var window = Open(agents: Agents(4), run: _ => new TaskCompletionSource<string?>().Task);
        LayOut(window, 120, 30);
        SelectAgent(window, 0);
        LayOut(window, 120, 30);
        var status = window.Status.Frame;
        var lanes = window.Overseer.Frame;

        window.Commands.Execute("agent.hold");
        LayOut(window, 120, 30);

        Assert.Equal("Pausing this role…", window.Says());
        Assert.Null(window.Status.MessageSeverity);
        Assert.Equal(status, window.Status.Frame);
        Assert.Equal(lanes, window.Overseer.Frame);
    }

    [Fact]
    public void The_help_hint_is_a_button_you_can_click()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal("Press F1 for help", Assert.IsType<Button>(window.Status.Hints.SubViews.Single()).Text);
    }

    [Fact]
    public void The_window_has_no_border_and_no_title_so_the_menu_is_row_zero()
    {
        using var window = Open(agents: Agents(4));

        LayOut(window, 120, 30);

        Assert.Equal("", window.Title);
        Assert.Equal(LineStyle.None, window.BorderStyle);
        Assert.Equal(new Size(120, 30), window.Viewport.Size);
        Assert.Equal(new Rectangle(0, 0, 120, 1), window.Menu.Frame);
    }

    [Fact]
    public void Every_dispatcher_line_gets_one_row_of_its_own_coloured_by_what_it_says()
    {
        WriteDispatchLog(
            "2026-09-20T16:53:09Z a-team lead: would start: 3 Ideas to shape",
            "2026-09-20T17:01:17Z tuicode dev: triggers failed: gh: Not Found (HTTP 404)",
            "2026-09-20T17:04:02Z tuicode dev: started 41234: 1 task Ready");
        using var window = Open(agents: Agents(4));

        window.Commands.Execute("view.dispatcher");

        Assert.Equal(
            ["16:53 a-team lead: would start: 3 Ideas to shape",
             "17:01 tuicode dev: triggers failed: gh: Not Found (HTTP 404)",
             "17:04 tuicode dev: started 41234: 1 task Ready"],
            window.WholeDispatchLog.Lines.Select(line => line.Text));
        Assert.Equal(
            [LogLineKind.DispatchSkipped, LogLineKind.DispatchFailed, LogLineKind.Prose],
            window.WholeDispatchLog.Lines.Select(line => line.Kind));
    }

    [Fact]
    public void With_nothing_installed_the_dispatcher_s_log_says_so_in_Error_and_its_lines_do_not()
    {
        using var window = Open(agents: Agents(4));
        LayOut(window, 120, 30);

        window.Commands.Execute("view.dispatcher");

        Assert.Equal("dispatcher · nothing installed · run: a-team install", window.Dispatcher.Title);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), window.Dispatcher.SchemeName);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Base), window.WholeDispatchLog.SchemeName);
    }

    [Fact]
    public void The_message_block_takes_no_rows_until_there_is_something_to_say()
    {
        using var window = Open(agents: Agents(4));

        LayOut(window, 120, 30);

        Assert.Equal("", window.Says());
        Assert.Equal(window.Viewport.Height - 2, window.Overseer.Frame.Height);
    }

    [Fact]
    public void Typing_pass_in_the_Commands_palette_finds_running_one_now()
    {
        using var window = Open();

        Assert.Equal(["dispatch.pass"], window.Commands.Enabled
            .Where(command => command.Label.Contains("pass", StringComparison.OrdinalIgnoreCase)).Select(command => command.Id));
    }

    [Fact]
    public void A_pass_now_says_it_is_running_in_Accent_then_what_it_started()
    {
        var finish = new TaskCompletionSource<Reading>();
        using var window = Open(pass: new DispatchPass(_root, "a-team", (_, _) => finish.Task));

        window.Commands.Execute("dispatch.pass");

        Assert.Equal("Running a pass…", window.Says());
        Assert.Null(window.Status.MessageSeverity);

        WriteDispatchLog("2026-10-04T10:02:00Z a-team dev: started 42 on 0.1.12: #334: Ready task #334 to build");
        finish.SetResult(new Reading("", null));
        window.Refresh();

        Assert.Equal("Pass done: started 1 run.", window.Says());
        window.Commands.Execute("view.dispatcher");
        Assert.Equal("10:02 a-team dev: started 42 on 0.1.12: #334: Ready task #334 to build", Assert.Single(window.WholeDispatchLog.Lines).Text);
    }

    [Fact]
    public void A_pass_now_that_failed_says_its_first_line_in_Error()
    {
        using var window = Open(pass: new DispatchPass(_root, "a-team", (_, _) => Task.FromResult(new Reading("", "a-team: no such command"))));

        window.Commands.Execute("dispatch.pass");
        window.Refresh();

        Assert.Equal("a-team: no such command", window.Says());
        Assert.Equal(Severity.Error, window.Status.MessageSeverity);
    }

    [Fact]
    public void A_second_pass_now_while_one_runs_only_says_one_is_already_running()
    {
        var calls = 0;
        using var window = Open(pass: new DispatchPass(_root, "a-team", (_, _) =>
        {
            calls++;
            return new TaskCompletionSource<Reading>().Task;
        }));

        window.Commands.Execute("dispatch.pass");
        window.Commands.Execute("dispatch.pass");

        Assert.Equal(1, calls);
        Assert.Equal("A pass is already running.", window.Says());
    }

    [Fact]
    public void Pausing_a_role_already_being_paused_says_so_until_the_first_one_resolves()
    {
        var calls = 0;
        var finish = new TaskCompletionSource<string?>();
        using var window = Open(agents: Agents(4), run: _ =>
        {
            calls++;
            return finish.Task;
        });
        SelectAgent(window, 0);

        window.Commands.Execute("agent.hold");
        window.Commands.Execute("agent.hold");
        Assert.Equal(1, calls);
        Assert.Equal("team0's lead is already being paused", window.Says());

        finish.SetResult(null);
        window.Refresh();
        window.Commands.Execute("agent.hold");
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Pausing_interrupting_and_asking_for_a_vision_review_queue_behind_each_other()
    {
        var calls = new List<string[]>();
        var finishes = new List<TaskCompletionSource<string?>>();
        using var window = Open(agents: Agents(4), run: arguments =>
        {
            calls.Add(arguments);
            var finish = new TaskCompletionSource<string?>();
            finishes.Add(finish);
            return finish.Task;
        }, pass: new DispatchPass(_root, "a-team", (_, _) => new TaskCompletionSource<Reading>().Task));
        WriteRunning("team1", "dev");
        window.Refresh();
        LayOut(window, 120, 30);

        SelectAgent(window, 3);
        window.Commands.Execute("agent.interrupt");
        window.Commands.Execute("team.visionReview");
        SelectAgent(window, 0);
        window.Commands.Execute("agent.hold");

        Assert.Equal([["stop", "team1", "dev"]], calls);
        Assert.Equal("Interrupting… · 2 more queued", window.Says());

        for (var i = 0; i < 3; i++)
        {
            finishes[i].SetResult(null);
            window.Refresh();
        }

        Assert.Equal([["stop", "team1", "dev"], ["board", "team1", "vision-review", "you"], ["pause", "team0", "lead"]], calls);
    }

    [Fact]
    public void A_command_that_worked_leaves_the_message_block_empty_again()
    {
        using var window = Open(agents: Agents(4));
        SelectAgent(window, 0);

        window.Commands.Execute("agent.hold");
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("team0", window.Says());
        Assert.Null(window.Status.Message);
    }

    [Fact]
    public void A_command_that_failed_shows_one_line_and_leaves_the_dashboard_usable()
    {
        using var window = Open(
            agents: Agents(4),
            run: _ => Task.FromResult<string?>("a-team pause: can't write /nope/team0.json\nstack\ntrace"));
        SelectAgent(window, 0);

        window.Commands.Execute("agent.hold");
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("pausing team0's lead: a-team pause: can't write /nope/team0.json", window.Says());
        Assert.True(window.NewKeyDownEvent(Key.CursorDown));
        Assert.Equal(("team1", true), (window.Overseer.Board.Selected?.Team, window.Overseer.Board.OnHeading));
    }

    [Fact]
    public void Teams_opens_Settings_on_its_Teams_page_and_nothing_pauses_a_team_from_outside_it()
    {
        using var window = Open(agents: Agents(4));

        var ids = window.Commands.Registered.Select(command => command.Id).ToList();
        Assert.DoesNotContain("team.pause", ids);
        Assert.Contains("teams", ids);
        Assert.Contains("teams.new", ids);
        Assert.DoesNotContain(window.MenuItems, item => item.Id == "team.pause");
    }

    [Fact]
    public void Pause_this_role_holds_the_selected_role_without_stopping_its_run()
    {
        var calls = new List<string[]>();
        using var window = Open(agents: Agents(4), run: arguments =>
        {
            calls.Add(arguments);
            return new TaskCompletionSource<string?>().Task;
        });
        WriteRunning("team1", "dev");
        window.Refresh();
        SelectAgent(window, 3);

        Assert.True(window.NewKeyDownEvent(new Key('h')));

        Assert.Equal([["pause", "team1", "dev"]], calls);
        Assert.Equal("Pausing this role…", window.Says());
    }

    [Fact]
    public void Pause_this_role_reads_Let_this_role_start_again_once_held_which_resumes_it()
    {
        var calls = new List<string[]>();
        using var window = Open(agents: Agents(2), run: arguments =>
        {
            calls.Add(arguments);
            if (arguments[0] == "pause")
                WriteHold(arguments[1], arguments[2]);
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        SelectAgent(window, 1);
        Assert.True(window.Commands.IsEnabled("agent.hold"));
        Assert.Equal("Pause this role", Label(window, "agent.hold"));

        window.Commands.Execute("agent.hold");
        window.Refresh();

        Assert.True(window.Watches[1].Held);
        Assert.StartsWith(Icons.Field(Icon.StoppedByYou, IconStyle.Unicode) + "Dev           paused", window.Team.Lines[1], StringComparison.Ordinal);
        Assert.Equal("Let this role start again", Label(window, "agent.hold"));
        Assert.Equal("Let t_his role start again", window.MenuItems.Single(item => item.Id == "agent.hold").Item.Title);

        window.Commands.Execute("agent.hold");

        Assert.Equal([["pause", "team0", "dev"], ["resume", "team0", "dev"]], calls);
    }

    [Fact]
    public void i_interrupts_the_selected_agents_run_and_says_so_while_it_runs()
    {
        var calls = new List<string[]>();
        using var window = Open(agents: Agents(4), run: arguments =>
        {
            calls.Add(arguments);
            return new TaskCompletionSource<string?>().Task;
        });
        WriteRunning("team1", "dev");
        window.Refresh();
        SelectAgent(window, 3);

        Assert.True(window.NewKeyDownEvent(new Key('i')));

        Assert.Equal([["stop", "team1", "dev"]], calls);
        Assert.Equal("Interrupting…", window.Says());
    }

    [Fact]
    public void Interrupting_a_run_reaches_its_line_on_the_next_refresh()
    {
        using var window = Open(agents: Agents(2), run: arguments =>
        {
            WriteHold(arguments[1], arguments[2]);
            return Task.FromResult<string?>(null);
        });
        WriteRunning("team0", "dev");
        window.Refresh();
        SelectAgent(window, 1);

        window.Commands.Execute("agent.interrupt");
        window.Refresh();

        Assert.True(window.Watches[1].Held);
        Assert.Equal("Let the chosen role start again", Label(window, "agent.interrupt"));
    }

    [Fact]
    public void A_held_role_is_offered_Let_it_start_again_which_resumes_it()
    {
        var calls = new List<string[]>();
        WriteHold("team0", "dev");
        using var window = Open(agents: Agents(2), run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        SelectAgent(window, 1);

        Assert.Equal("Let the chosen role start again", Label(window, "agent.interrupt"));
        Assert.True(window.NewKeyDownEvent(new Key('i')));

        Assert.Equal([["resume", "team0", "dev"]], calls);
    }

    [Fact]
    public void Interrupt_is_enabled_only_for_a_selected_role_that_is_running_or_held()
    {
        WriteHold("team0", "dev");
        WriteRunning("team1", "lead");
        using var window = Open(agents: Agents(4));
        window.Refresh();
        Assert.False(window.Commands.IsEnabled("agent.interrupt"));

        var enabled = Enumerable.Range(0, 4).Select(index =>
        {
            SelectAgent(window, index);
            return window.Commands.IsEnabled("agent.interrupt");
        }).ToList();

        Assert.Equal([false, true, true, false], enabled);
    }

    [Fact]
    public void i_does_nothing_on_an_idle_role()
    {
        var calls = 0;
        using var window = Open(agents: Agents(2), run: _ =>
        {
            calls++;
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        SelectAgent(window, 1);

        Assert.False(window.NewKeyDownEvent(new Key('i')));
        window.Commands.Execute("agent.interrupt");

        Assert.Equal(0, calls);
    }

    [Fact]
    public void An_interrupt_that_failed_shows_the_file_in_red()
    {
        using var window = Open(
            agents: Agents(2),
            run: _ => Task.FromResult<string?>("a-team stop: can't write /nope/team0.json"));
        WriteRunning("team0", "dev");
        window.Refresh();
        SelectAgent(window, 1);

        window.Commands.Execute("agent.interrupt");
        window.Refresh();

        Assert.Equal("interrupting team0's dev: a-team stop: can't write /nope/team0.json", window.Says());
        Assert.Equal(Severity.Error, window.Status.MessageSeverity);
        Assert.False(window.Watches[1].Held);
        Assert.Equal("Interrupt the chosen role", Label(window, "agent.interrupt"));
    }

    [Fact]
    public void i_on_a_run_with_a_session_hands_it_to_attach_instead_of_stopping_it()
    {
        var calls = new List<string[]>();
        var handed = new List<Handover>();
        using var window = Open(agents: Agents(4), handOver: handed.Add, run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        WriteRunning("team1", "dev");
        WriteSession("team1", "dev");
        window.Refresh();
        SelectAgent(window, 3);

        Assert.True(window.NewKeyDownEvent(new Key('i')));

        var handover = Assert.IsType<AttachHandover>(Assert.Single(handed));
        Assert.Equal(["attach", "team1", "dev"], handover.Arguments);
        Assert.Equal((Area.Overseer, null), (handover.Area, handover.Card));
        Assert.Empty(calls);
    }

    [Fact]
    public void i_on_a_held_role_with_a_session_lets_it_start_again_rather_than_attaching()
    {
        var calls = new List<string[]>();
        var handed = new List<Handover>();
        WriteHold("team0", "dev");
        WriteSession("team0", "dev");
        using var window = Open(agents: Agents(2), handOver: handed.Add, run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        SelectAgent(window, 1);

        Assert.True(window.NewKeyDownEvent(new Key('i')));

        Assert.Equal([["resume", "team0", "dev"]], calls);
        Assert.Empty(handed);
    }

    [Fact]
    public void i_does_not_attach_to_a_finished_run()
    {
        var handed = new List<Handover>();
        WriteSession("team0", "dev");
        using var window = Open(agents: Agents(2), handOver: handed.Add);
        window.Refresh();
        SelectAgent(window, 1);

        Assert.False(window.NewKeyDownEvent(new Key('i')));
        Assert.Empty(handed);
    }

    [Fact]
    public void Review_the_vision_asks_for_one_for_the_selected_team_then_runs_a_pass_so_the_Lead_starts_now()
    {
        var calls = new List<string[]>();
        var passes = 0;
        using var window = Open(agents: Agents(4),
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            pass: new DispatchPass(_root, "a-team", (_, _) =>
            {
                passes++;
                return new TaskCompletionSource<Reading>().Task;
            }));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.False(window.Commands.IsEnabled("team.visionReview"));
        SelectAgent(window, 3);
        Assert.Equal("Review the vision · team1", window.Commands.Registered.Single(c => c.Id == "team.visionReview").Label);
        Assert.True(window.Commands.Execute("team.visionReview"));
        window.Refresh();

        Assert.Equal([["board", "team1", "vision-review", "you"]], calls);
        Assert.Equal(1, passes);
    }

    [Fact]
    public void A_refused_review_starts_no_pass_and_says_why()
    {
        var passes = 0;
        using var window = Open(agents: Agents(4),
            run: _ => Task.FromResult<string?>("board.sh: team1.json names no vision"),
            pass: new DispatchPass(_root, "a-team", (_, _) =>
            {
                passes++;
                return new TaskCompletionSource<Reading>().Task;
            }));
        window.Refresh();
        LayOut(window, 120, 30);
        SelectAgent(window, 3);

        window.Commands.Execute("team.visionReview");
        window.Refresh();

        Assert.Equal(0, passes);
        Assert.Equal("asking for team1's vision review: board.sh: team1.json names no vision", window.Says());
    }

    [Fact]
    public void Write_the_vision_with_me_names_the_selected_team_and_hands_its_interview_the_terminal()
    {
        var handed = new List<Handover>();
        using var window = Open(agents: Agents(4), handOver: handed.Add);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.False(window.Commands.IsEnabled("team.vision"));
        SelectAgent(window, 3);
        Assert.True(window.Commands.IsEnabled("team.vision"));
        Assert.Equal("Write the vision with me · team1", window.Commands.Registered.Single(c => c.Id == "team.vision").Label);

        Assert.True(window.Commands.Execute("team.vision"));

        var handover = Assert.IsType<VisionHandover>(Assert.Single(handed));
        Assert.Equal(["vision", "team1"], handover.Arguments);
        Assert.Equal(Area.Overseer, handover.Area);
    }

    [Fact]
    public void Back_from_a_session_that_would_not_resume_the_team_s_panel_has_the_role_chosen_and_says_so_in_red()
    {
        using var window = Open(
            agents: Agents(4),
            resume: new AttachHandover("team1", "dev") { Failure = "attach team1 dev exited 1" });
        window.Refresh();
        LayOut(window, 120, 40);

        Assert.Equal(Area.Overseer, window.CurrentArea);
        Assert.Equal(("team1", "dev"), (window.Overseer.Board.Selected?.Team, window.Team.Role));
        Assert.False(window.SessionFull);
        Assert.Equal("attach team1 dev exited 1", window.Says());
        Assert.Equal(Severity.Error, window.Status.MessageSeverity);
    }

    [Fact]
    public void With_several_runs_i_says_to_interrupt_one_from_its_card()
    {
        var calls = 0;
        WriteRun("team0", "dev", 246, 100);
        WriteRun("team0", "dev", 303, 300);
        using var window = Open(agents: Agents(2), confirmStop: _ => throw new InvalidOperationException(), run: _ =>
        {
            calls++;
            return Task.FromResult<string?>(null);
        });
        SelectAgent(window, 1);

        Assert.True(window.NewKeyDownEvent(new Key('i')));

        Assert.Equal(0, calls);
        Assert.Equal("team0's dev is on 2 cards: interrupt one from its card", window.Says());
        Assert.EndsWith(" · #246, #303", window.Team.Lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void With_one_run_on_a_task_i_asks_to_stop_that_run_alone_as_on_its_card()
    {
        var calls = new List<string[]>();
        var asked = new List<RunTask>();
        WriteRun("team0", "dev", 246, 100);
        using var window = Open(agents: Agents(2), confirmStop: task =>
        {
            asked.Add(task);
            return true;
        }, run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        SelectAgent(window, 1);

        window.Commands.Execute("agent.interrupt");

        Assert.Equal(new RunTask(246, "Task 246"), Assert.Single(asked));
        Assert.Equal([["stop", "team0", "dev", "246"]], calls);
    }

    [Fact]
    public void Declining_the_stop_leaves_the_run_going()
    {
        var calls = 0;
        WriteRun("team0", "dev", 246, 100);
        using var window = Open(agents: Agents(2), confirmStop: _ => false, run: _ =>
        {
            calls++;
            return Task.FromResult<string?>(null);
        });
        SelectAgent(window, 1);

        window.Commands.Execute("agent.interrupt");

        Assert.Equal(0, calls);
        Assert.Null(window.Status.Message);
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
            handOver,
            resume,
            showGuide: showGuide,
            confirmStop: confirmStop,
            clipboard: clipboard,
            checks: checks,
            pass: pass);
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
