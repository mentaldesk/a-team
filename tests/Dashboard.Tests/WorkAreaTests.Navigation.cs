using MentalDesk.Tui.Chrome;
using MentalDesk.Tui;
using MentalDesk.Tui.Keys;
using MentalDesk.Tui.Links;
using MentalDesk.Tui.Loading;
using System.Drawing;
using System.Globalization;
using System.Text.Json.Nodes;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public partial class WorkAreaTests
{
    [Fact]
    public void The_arrows_reach_every_column_a_team_has_a_card_in_and_stop_at_the_bottom_of_one()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(49, window.Work.Selected?.Number);
        Assert.Equal("Review · team0", window.Work.Region);

        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(49, window.Work.Selected?.Number);
        Assert.Equal("Review · team0", window.Work.Region);
    }

    [Fact]
    public void Ctrl_PgDn_and_Ctrl_PgUp_move_between_teams_and_come_back_to_the_card_left()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);

        Assert.True(window.NewKeyDownEvent(Key.PageDown.WithCtrl));
        Assert.Equal(133, window.Work.Selected?.Number);
        Assert.Equal("Pitches · team1", window.Work.Region);

        Assert.True(window.NewKeyDownEvent(Key.PageUp.WithCtrl));
        Assert.Equal(49, window.Work.Selected?.Number);
        Assert.Equal("team0", window.Work.Current?.Team);
    }

    [Fact]
    public void A_number_key_brings_that_teams_tab_to_the_front()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(window.NewKeyDownEvent(new Key('2')));
        Assert.Equal("team1", window.Work.Current?.Team);
        Assert.Equal(133, window.Work.Selected?.Number);

        Assert.True(window.NewKeyDownEvent(new Key('1')));
        Assert.Equal("team0", window.Work.Current?.Team);
        Assert.False(window.Commands.IsEnabled("work.team3"));
        Assert.Equal("Go to team1", window.Commands.Registered.Single(command => command.Id == "work.team2").Label);
    }

    [Fact]
    public void Built_again_after_a_team_is_added_Work_lands_on_the_card_it_was_on()
    {
        using var window = Open(resume: new TeamsChanged(Area.Work, new Place("team1", 133, false)));
        window.Refresh();

        Assert.Equal(133, window.Work.Selected?.Number);
        Assert.Equal("team1", window.Work.Current?.Team);
    }

    [Fact]
    public void Write_the_vision_with_me_from_Work_comes_back_on_the_card_it_was_on()
    {
        var handed = new List<Handover>();
        using (var window = Open(handOver: handed.Add))
        {
            window.Refresh();
            LayOut(window, 120, 30);
            window.NewKeyDownEvent(Key.CursorRight);
            window.NewKeyDownEvent(Key.CursorRight);
            window.NewKeyDownEvent(Key.CursorRight);
            var team = window.Work.Current?.Team;
            var card = window.Work.Selected?.Number;

            Assert.True(window.Commands.Execute("team.vision"));

            var handover = Assert.IsType<VisionHandover>(Assert.Single(handed));
            Assert.Equal(["vision", team!], handover.Arguments);
            Assert.Equal(Area.Work, handover.Area);
            Assert.Equal(card, handover.Left?.Number);
        }

        using var back = Open(resume: handed[0]);
        back.Refresh();

        Assert.Equal(((VisionHandover)handed[0]).Left?.Number, back.Work.Selected?.Number);
    }

    [Fact]
    public void Every_tab_says_how_many_cards_wait_there_and_m_narrows_the_count()
    {
        using var window = Open();
        window.Refresh();

        Assert.Equal(["team0 \U0001F4A11 ◇ 2 PR1", "team1 ◇ 1"], window.Work.Lanes.Select(lane => lane.Title));

        window.Press("Ctrl+T M");

        Assert.Equal(["team0 \U0001F4A11 ◇ 1", "team1 ◇ 1"], window.Work.Lanes.Select(lane => lane.Title));
    }

    [Fact]
    public void o_opens_a_card_the_arrows_moved_to()
    {
        var opened = new List<string>();
        using var window = Open(openUrl: Opening.Into(opened));
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(new Key('g'));

        Assert.Equal(["https://github.com/mentaldesk/team0/issues/49"], opened);
    }

    [Fact]
    public void The_work_area_fills_the_window_under_the_menu_and_its_title()
    {
        using var window = Open();
        window.Refresh();

        LayOut(window, 120, 30);

        Assert.NotEqual("", window.Says());
        Assert.Equal(new Rectangle(0, 0, window.Viewport.Width, 1), window.Menu.Frame);
        Assert.Equal(new Rectangle(0, 2, window.Viewport.Width, window.Viewport.Height - 3), window.Work.Frame);
    }

    [Fact]
    public void Every_menu_item_runs_the_command_its_id_names_on_the_key_that_command_has()
    {
        using var window = Open(area: Area.Overseer);

        Assert.Equal(
            [
                "overseer.details", "overseer.fold", "overseer.read", "overseer.work", "overseer.github", "overseer.retry", "overseer.refresh", "work.nextTeam", "work.previousTeam", "team.board",
                "view.dispatcher", "decisions", "settings", "teams", "quit", "view.work", "view.overseer", "assistant", "view.details", "work.team", "work.whatsNew", "work.read",
                "work.priority", "work.try", "work.copyBranch", "work.github", "work.accept", "work.decline", "work.notNow", "work.hold", "work.secret", "work.fix", "work.new", "work.refresh",
                "work.mine", "agent.hold", "agent.interrupt", "dispatch.pass", "log.toolCalls", "agent.collapse", "log.copyLines", "log.copyAll", "log.editor", "help",
                "guide", "messages", "commands", "about",
            ],
            window.MenuItems.Select(item => item.Id));
        Assert.All(window.MenuItems, item =>
        {
            var command = window.Commands.Registered.Single(registered => registered.Id == item.Id);
            Assert.Equal(command.MenuLabel.Replace("_", ""), item.Item.Title.Replace("_", "").TrimStart(AppMenu.Check, ' '));
            Assert.Equal(command.Keys, item.Item.KeyView.Text);
        });
    }

    [Fact]
    public void A_menu_item_and_its_key_do_the_same_thing()
    {
        using var window = Open(area: Area.Overseer);

        window.MenuItems.Single(item => item.Id == "view.work").Item.Action!();
        Assert.Equal(Area.Work, window.CurrentArea);

        window.MenuItems.Single(item => item.Id == "view.overseer").Item.Action!();
        Assert.Equal(Area.Overseer, window.CurrentArea);
        Assert.True(window.Press("Ctrl+G W"));
        Assert.Equal(Area.Work, window.CurrentArea);
    }


    private static string Branched() => Review().Replace("\"base\": \"main\"", "\"base\": \"main\", \"branch\": \"feat/keys\"");

    [Fact]
    public void work_copyBranch_is_on_y_and_acts_on_the_card()
    {
        using var window = Open();

        var command = window.Commands.Registered.Single(registered => registered.Id == "work.copyBranch");

        Assert.Equal("Y", command.Keys);
        Assert.True(command.OnCard);
        Assert.Equal("Copy branch name", command.Label);
    }

    [Fact]
    public void work_copyBranch_is_disabled_on_a_card_with_no_PR()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(6, window.Work.Selected?.Number);
        Assert.False(window.Commands.IsEnabled("work.copyBranch"));
    }

    [Fact]
    public void Y_copies_the_selected_card_s_branch_and_the_status_bar_says_so()
    {
        var clipboard = new FakeClipboard();
        using var window = Open(read: team => Task.FromResult(new Reading(team == "team0" ? Branched() : "[]", null)),
            clipboard: clipboard);
        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal("#49 · awaiting your acceptance since 10:15 · PR #122 · feat/keys", window.Work.Selected?.Line);
        Assert.True(window.Commands.IsEnabled("work.copyBranch"));

        window.NewKeyDownEvent(new Key('y'));

        Assert.Equal("feat/keys", clipboard.GetClipboardData());
        Assert.Equal("Copied feat/keys", window.Says());
    }

    [Fact]
    public void A_reply_goes_at_the_next_key()
    {
        using var window = Open(read: team => Task.FromResult(new Reading(team == "team0" ? Branched() : "[]", null)),
            clipboard: new FakeClipboard());
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(new Key('y'));
        Assert.Equal("Copied feat/keys", window.Says());

        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Null(window.Status.Message);
    }

    [Fact]
    public void A_newer_message_replaces_an_older_one()
    {
        using var window = Open(read: team => Task.FromResult(new Reading(team == "team0" ? Branched() : "[]", null)),
            clipboard: new FakeClipboard());
        window.Refresh();
        LayOut(window, 120, 30);

        window.Commands.Execute("work.copyBranch");
        window.Commands.Execute("work.github");

        Assert.StartsWith("Opened https://", window.Says());
        Assert.Equal(["Copied feat/keys", window.Says()], window.Status.History.Select(message => message.Text));
    }

    [Fact]
    public void An_error_waits_for_a_key_however_long_that_takes()
    {
        var clock = new Clock();
        var reads = 0;
        using var window = Open(read: team => Task.FromResult(team == "team0" && reads++ > 0
            ? new Reading("", "gh: API rate limit exceeded") : new Reading(Waiting(team), null)), clock: clock);
        window.Refresh();
        window.Commands.Execute("work.refresh");
        window.Refresh();

        clock.Now += TimeSpan.FromMinutes(1);
        Assert.Equal("team0: gh: API rate limit exceeded", window.Says());
        Assert.Equal(Severity.Error, window.Status.MessageSeverity);

        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Null(window.Status.Message);
    }

    [Fact]
    public void Error_news_outlasts_keys_pressed_in_its_first_five_seconds()
    {
        var clock = new Clock();
        var reads = 0;
        using var window = Open(read: team => Task.FromResult(team == "team0" && reads++ > 0
            ? new Reading("", "gh: API rate limit exceeded") : new Reading(Waiting(team), null)), clock: clock);
        window.Refresh();
        window.Commands.Execute("work.refresh");
        window.Refresh();

        clock.Now += TimeSpan.FromSeconds(2);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal("team0: gh: API rate limit exceeded", window.Says());

        clock.Now += AppStatusBar.Lifetime;
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Null(window.Status.Message);
    }

    [Fact]
    public void A_failed_re_read_comes_off_the_bar_once_a_read_works()
    {
        var reads = 0;
        using var window = Open(read: team => Task.FromResult(team == "team0" && reads++ == 1
            ? new Reading("", "gh: API rate limit exceeded") : new Reading(Waiting(team), null)));
        window.Refresh();
        window.Commands.Execute("work.refresh");
        window.Refresh();
        Assert.Equal("team0: gh: API rate limit exceeded", window.Says());

        window.Commands.Execute("work.refresh");
        window.Refresh();

        Assert.Null(window.Status.Message);
        Assert.Equal("#6 · waiting to be ranked", window.Says());
    }

    [Fact]
    public void Unseen_errors_count_on_the_bar_until_Messages_lists_them_newest_first()
    {
        var reads = 0;
        using var window = Open(read: team => Task.FromResult(team == "team0" && reads++ == 1
            ? new Reading("", "gh: API rate limit exceeded") : new Reading(Waiting(team), null)));
        window.Refresh();
        window.Commands.Execute("work.refresh");
        window.Refresh();
        window.Commands.Execute("work.refresh");
        window.Refresh();
        LayOut(window, 120, 30);
        window.Commands.Execute("work.github");

        Assert.Equal(1, window.Status.UnreadErrors);
        Assert.Equal("⚠ 1", window.Status.UnreadButton.Text);

        using var messages = new MessagesDialog(window.Status);

        Assert.Equal(0, window.Status.UnreadErrors);
        Assert.Equal([window.Says(), "team0: gh: API rate limit exceeded"], messages.Rows.Select(row => row.Text));
    }

    [Fact]
    public void Y_copies_the_branch_through_the_installed_terminal_clipboard()
    {
        using var terminal = new InstalledTerminal();
        using var window = Open(read: team => Task.FromResult(new Reading(team == "team0" ? Branched() : "[]", null)),
            clipboard: terminal.Clipboard);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(new Key('y'));

        Assert.Equal("feat/keys", terminal.Platform.Text);
        Assert.Equal("Copied feat/keys", window.Says());
    }

    [Fact]
    public void Y_without_a_clipboard_says_why_it_couldn_t_copy()
    {
        using var window = Open(read: team => Task.FromResult(new Reading(team == "team0" ? Branched() : "[]", null)),
            clipboard: new FakeClipboard(isSupportedAlwaysFalse: true));
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(new Key('y'));

        Assert.Equal("Copy failed: there is no clipboard", window.Says());
    }
}
