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
    public void Overseers_stamp_still_says_when_its_boards_were_read()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("read 2m ago", DashboardWindow.Stamped(now, now.AddMinutes(2)));
    }

    [Fact]
    public void While_a_sync_runs_Works_title_says_so_and_once_it_lands_the_cards_are_read_again()
    {
        var reads = 0;
        var sync = new TaskCompletionSource<Reading>();
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(SyncedAt(team, DateTimeOffset.UtcNow), null));
        }, syncBoard: _ => sync.Task);
        window.Refresh();

        Assert.EndsWith(" · Syncing…", window.WorkTitle);
        Assert.Equal(2, reads);

        sync.SetResult(new Reading("", null));
        window.Refresh();

        Assert.DoesNotContain("Syncing…", window.WorkTitle);
        Assert.Equal(4, reads);
        Assert.Equal("All items · synced just now", window.Status.FocusWord);
    }

    [Fact]
    public void With_Work_in_front_it_syncs_every_minute_and_never_more_often()
    {
        var clock = new Clock();
        var syncs = new List<string>();
        using var window = Open(syncBoard: team =>
        {
            syncs.Add(team);
            return Task.FromResult(new Reading("", null));
        }, clock: clock);
        window.Refresh();
        window.Refresh();
        Assert.Equal(["team0", "team1"], syncs.Order());

        clock.Now += DashboardWindow.SyncEvery - TimeSpan.FromSeconds(1);
        window.Refresh();
        Assert.Equal(2, syncs.Count);

        clock.Now += TimeSpan.FromSeconds(1);
        window.Refresh();
        Assert.Equal(4, syncs.Count);
    }

    [Fact]
    public void A_sync_the_dispatcher_ran_reads_that_teams_cards_again_and_keeps_the_selection()
    {
        var reads = new List<string>();
        using var window = Open(read: team =>
        {
            reads.Add(team);
            return Task.FromResult(new Reading(Waiting(team), null));
        });
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(107, window.Work.Selected?.Number);
        reads.Clear();

        var synced = Path.Combine(_root, "team0", "detail-synced");
        Directory.CreateDirectory(Path.GetDirectoryName(synced)!);
        File.WriteAllText(synced, "");
        File.SetLastWriteTimeUtc(synced, DateTime.UtcNow.AddSeconds(5));
        window.Refresh();
        window.Refresh();

        Assert.Equal(["team0"], reads);
        Assert.Equal(107, window.Work.Selected?.Number);
    }

    [Fact]
    public void A_read_that_failed_says_so_and_leaves_the_cards_and_the_stamp_as_they_were()
    {
        var failing = false;
        using var window = Open(read: team => Task.FromResult(failing
            ? new Reading("", "a-team board: API rate limit exceeded\nand a second line")
            : new Reading(Waiting(team), null)));
        window.Refresh();
        LayOut(window, 120, 30);
        var stamp = window.Status.FocusWord;

        failing = true;
        window.NewKeyDownEvent(Key.F5);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("team1: a-team board: API rate limit exceeded", window.Says());
        Assert.Equal(["\U0001F4A1Triage · 1", "◇ Pitches · 2", "Blocked · 0", "Review · 1", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Blocked · 0", "Review · 0"],
            Titles(window));
        Assert.Equal(stamp, window.Status.FocusWord);
        Assert.Equal(6, window.Work.Selected?.Number);
    }

    [Fact]
    public void A_second_refresh_while_one_is_running_is_refused()
    {
        var reads = 0;
        var finish = new TaskCompletionSource<Reading>();
        using var window = Open(read: _ =>
        {
            reads++;
            return finish.Task;
        });

        window.NewKeyDownEvent(Key.F5);
        Assert.All(window.Work.Lanes, lane => Assert.Equal(LoadState.Loading, lane.Load.State));
        Assert.DoesNotContain("Reading…", window.Says());
        window.NewKeyDownEvent(Key.F5);

        Assert.Equal(2, reads);
        finish.SetResult(new Reading("[]", null));
        window.Refresh();
        window.NewKeyDownEvent(Key.F5);
        Assert.Equal(4, reads);
    }

    [Fact]
    public void Reads_happen_when_you_ask_and_never_on_a_timer()
    {
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });

        for (var tick = 0; tick < 5; tick++)
            window.Refresh();

        Assert.Equal(2, reads);
    }

    [Fact]
    public void Coming_back_with_nothing_changed_reads_nothing_and_keeps_the_card()
    {
        var clock = new Clock();
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(SyncedAt(team, clock.Now), null));
        }, clock: clock);
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(107, window.Work.Selected?.Number);

        window.Press("Ctrl+G O");
        clock.Now += DashboardWindow.ReadEvery - TimeSpan.FromSeconds(1);
        window.Refresh();
        window.Press("Ctrl+G W");

        Assert.Equal(2, reads);
        Assert.Equal(107, window.Work.Selected?.Number);
        Assert.NotNull(window.Work.SelectedCard);
        Assert.EndsWith("synced 4m ago", window.Status.FocusWord);
    }

    [Fact]
    public void Coming_back_keeps_the_PR_row()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal("https://github.com/mentaldesk/team0/pull/122", window.Work.SelectedUrl);

        window.Press("Ctrl+G O");
        window.Press("Ctrl+G W");

        Assert.Equal("https://github.com/mentaldesk/team0/pull/122", window.Work.SelectedUrl);
    }

    [Fact]
    public void Coming_back_after_a_run_started_reads_again_and_keeps_the_card()
    {
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.Press("Ctrl+G O");
        WriteRun("team1", "lead", Environment.ProcessId);
        window.Press("Ctrl+G W");
        window.Refresh();

        Assert.Equal(4, reads);
        Assert.Equal(107, window.Work.Selected?.Number);
    }

    [Fact]
    public void Coming_back_after_a_run_finished_reads_again_and_keeps_the_card()
    {
        WriteRun("team0", "dev", Environment.ProcessId);
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.Press("Ctrl+G O");
        WriteRun("team0", "dev", 999999);
        window.Press("Ctrl+G W");
        window.Refresh();

        Assert.Equal(4, reads);
        Assert.Equal(107, window.Work.Selected?.Number);
    }

    [Fact]
    public void Coming_back_five_minutes_after_the_last_read_reads_again_and_keeps_the_card()
    {
        var clock = new Clock();
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(SyncedAt(team, clock.Now), null));
        }, clock: clock);
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.Press("Ctrl+G O");
        clock.Now += DashboardWindow.ReadEvery;
        window.Press("Ctrl+G W");
        window.Refresh();

        Assert.Equal(4, reads);
        Assert.Equal(107, window.Work.Selected?.Number);
        Assert.EndsWith("synced just now", window.Status.FocusWord);
    }

    [Fact]
    public void Coming_back_after_a_read_that_failed_reads_again()
    {
        var reads = 0;
        using var window = Open(read: _ =>
        {
            reads++;
            return Task.FromResult(new Reading("", "gh: API rate limit exceeded"));
        });
        window.Refresh();

        window.Press("Ctrl+G O");
        window.Press("Ctrl+G W");

        Assert.Equal(4, reads);
    }

    [Fact]
    public void A_read_that_moves_the_card_to_another_column_takes_the_selection_with_it()
    {
        var waiting = Waiting("team0");
        using var window = Open(read: team => Task.FromResult(new Reading(team == "team0" ? waiting : Waiting(team), null)));
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(107, window.Work.Selected?.Number);

        waiting = waiting.Replace("\"priority\": \"High\", ", "");
        window.Commands.Execute("work.refresh");
        window.Refresh();

        Assert.Equal(107, window.Work.Selected?.Number);
        Assert.Equal("Triage · team0", window.Work.Region);
    }

    [Fact]
    public void F5_reads_now()
    {
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });
        window.Refresh();

        window.NewKeyDownEvent(Key.F5);
        window.Refresh();

        Assert.Equal(4, reads);
    }

    [Fact]
    public void Starting_in_Work_reads_and_lands_on_the_first_card()
    {
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });
        window.Refresh();

        Assert.Equal(2, reads);
        Assert.Equal(6, window.Work.Selected?.Number);
    }

    [Fact]
    public void Esc_in_Work_does_nothing_at_all()
    {
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        Assert.False(window.NewKeyDownEvent(Key.Esc));

        Assert.Equal(Area.Work, window.CurrentArea);
        Assert.Equal(107, window.Work.Selected?.Number);
        Assert.Equal(2, reads);
    }

    [Fact]
    public void The_arrows_move_between_cards()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal(108, window.Work.Selected?.Number);
    }
}
