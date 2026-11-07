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

public partial class DashboardWindowTests
{
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
                "Comment on the item you're reading", "Edit the document pitch you're reading", "Attach image file to what you're writing", "View image", "Decline", "Not now", "Hold", "Give secret…", "Ask the Dev to fix…",
                "Next team", "Previous team", "Go to team…", "New idea", "Show only what's your move", "Read this team again", "Read what's waiting again",
                "Select the chip to the right", "Select the chip to the left", "Select the chip below", "Select the chip above",
                "Select the next team's lane", "Select the previous team's lane", "Scroll the lanes down", "Scroll the lanes up",
                "Show the session full-screen", "Fold the lane", "Open on GitHub", "Read the boards that failed again", "Read every board again",
                "Choose the role below", "Choose the role above", "Work", "Overseer", "Assistant", "Details", "Dispatcher log", "Go to what's new",
                "Pause this role", "Interrupt the agent on this card", "Run a dispatch pass now", "Commands", "Settings", "Teams", "New team", "Show keys", "Guide", "Messages", "About", "Trends", "Spending", "Decisions…", "Back to Overseer", "Quit",
                "Widen pane", "Narrow pane", "Reset pane width",
                "Go to team0", "Go to team1", "Go to team 3", "Go to team 4", "Go to team 5", "Go to team 6", "Go to team 7",
                "Go to team 8", "Go to team 9",
            ],
            window.Commands.Registered.Select(command => command.Label));
    }
}
