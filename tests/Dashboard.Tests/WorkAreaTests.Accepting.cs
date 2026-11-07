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
    public void Accepting_from_the_reader_merges_the_task_it_shows_and_the_reader_closes()
    {
        var calls = new List<string[]>();
        var closes = new List<bool>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? Review() : "[]", null)),
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, accept, _, _) => closes.Add(accept!.Run()),
            confirmAccept: _ => true);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
        window.Refresh();

        Assert.Equal([true], closes);
        Assert.Equal([["board", "team0", "accept", "you", "49"]], calls);
        Assert.Equal("merged PR #122", window.Says());
    }

    [Fact]
    public void A_document_pitch_waits_under_Pitches_and_a_merges_it_once_you_ve_said_so()
    {
        var calls = new List<string[]>();
        var asked = new List<WaitingItem>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? DocumentPitch() : "[]", null)),
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            confirmAccept: item =>
            {
                asked.Add(item);
                return true;
            });
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("Pitches", WorkView.GateOf(window.Work.Selected!));
        Assert.True(window.NewKeyDownEvent(new Key('a')));
        window.Refresh();

        Assert.Equal(60, Assert.Single(asked).Number);
        Assert.Equal("Merges #60 into main, squashed. The Lead's next run reads it.", AcceptDialog.Says(asked[0]));
        Assert.Equal([["board", "team0", "accept", "you", "60"]], calls);
        Assert.Equal("merged #60", window.Says());
    }

    [Fact]
    public void A_document_pitch_s_reader_shows_its_file_after_its_body_and_offers_e()
    {
        var shown = new List<IssueBody>();
        var edits = new List<ReaderEdit?>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? DocumentPitch() : "[]", null)),
            readBody: _ => Task.FromResult(new Reading("""{"number": 60, "title": "t", "body": "## Why now"}""", null)),
            readDocument: _ => Task.FromResult(new Reading(VisionDocument, null)),
            showBody: (_, body, _, _, _, _, _) => shown.Add(body),
            showEdit: edits.Add);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal("## Why now\n\n---\n\n# docs/vision.md\n\n# Vision\n\nThe tenet.\n", Assert.Single(shown).Text);
        var edit = Assert.Single(edits);
        Assert.NotNull(edit);
        Assert.Null(edit.Uneditable);
    }

    [Fact]
    public void A_document_pitch_that_changes_several_files_can_t_be_edited_from_its_reader()
    {
        var edits = new List<ReaderEdit?>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? DocumentPitch() : "[]", null)),
            readBody: _ => Task.FromResult(new Reading("""{"number": 60, "title": "t", "body": "## Why now"}""", null)),
            readDocument: _ => Task.FromResult(new Reading("""{"number": 60, "files": ["docs/vision.md", "README.md"]}""", null)),
            showEdit: edits.Add);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal("#60 changes 2 files, so it can't be edited here: g opens it on GitHub", Assert.Single(edits)?.Uneditable);
    }

    [Fact]
    public void e_hands_your_editor_the_file_and_the_reader_reads_it_again_once_your_edit_is_committed()
    {
        var handed = new List<Handover>();
        using var first = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? DocumentPitch() : "[]", null)),
            handOver: handed.Add,
            readBody: _ => Task.FromResult(new Reading("""{"number": 60, "title": "t", "body": "## Why now"}""", null)),
            readDocument: _ => Task.FromResult(new Reading(VisionDocument, null)),
            showEdit: edit => edit!.Run(new IssueBody("## Why now"), 7));
        first.Refresh();
        LayOut(first, 120, 30);
        first.NewKeyDownEvent(Key.Enter);
        first.Refresh();

        var handover = Assert.IsType<DocumentHandover>(Assert.Single(handed));
        Assert.Equal("# Vision\n\nThe tenet.\n", File.ReadAllText(handover.File));
        File.Delete(handover.File);
        Assert.Equal(7, handover.Reader.Top);

        var reads = 0;
        var reopened = new List<ReaderEdit?>();
        using var back = Open(
            readBody: _ =>
            {
                reads++;
                return Task.FromResult(new Reading("""{"number": 60, "title": "t", "body": "## Why now"}""", null));
            },
            readDocument: _ => Task.FromResult(new Reading(VisionDocument, null)),
            showEdit: reopened.Add,
            resume: handover with { Said = "committed your edit to docs/vision.md", Committed = true });
        back.Refresh();
        LayOut(back, 120, 30);
        back.FocusResumed();
        back.ReopenReader();
        back.Refresh();

        Assert.Equal(1, reads);
        var edit = Assert.Single(reopened);
        Assert.Equal("committed your edit to docs/vision.md", edit?.Said);
        Assert.Equal(7, edit?.Top);
    }

    [Fact]
    public void The_reader_offers_no_accept_on_a_pitch()
    {
        var offered = new List<bool>();
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, accept, _, _) => offered.Add(accept is not null));
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal([false], offered);
    }

    [Fact]
    public void work_comment_is_registered_on_c()
    {
        using var window = Open();

        var comment = window.Commands.Registered.Single(command => command.Id == "work.comment");
        Assert.Equal("Comment on the item you're reading", comment.Label);
        Assert.Equal("C", comment.Keys);
        Assert.False(window.Commands.IsEnabled("work.comment"));
    }

    [Fact]
    public void Commenting_from_the_reader_posts_your_words_as_you_on_the_item_it_shows()
    {
        var calls = new List<string[]>();
        var bodies = new List<string>();
        var said = new List<string?>();
        ReaderComment? offered = null;
        using var window = Open(
            run: arguments =>
            {
                calls.Add(arguments);
                bodies.Add(File.ReadAllText(arguments[^1]));
                return Task.FromResult<string?>(null);
            },
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, comment, _) =>
            {
                offered = comment;
                said.Add(comment!.Post("Not yet: shelve it.").Result);
            });
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal(new Key('c'), offered?.Key);
        Assert.Equal("comment", offered?.Hint);
        Assert.Equal([null], said);
        Assert.Equal(["board", "team0", "comment", "you"], calls.Single()[..4]);
        Assert.Equal(["Not yet: shelve it."], bodies);
        Assert.False(File.Exists(calls.Single()[^1]));
    }

    [Fact]
    public void A_comment_that_fails_to_post_says_why_to_the_reader()
    {
        string? said = "";
        using var window = Open(
            run: _ => Task.FromResult<string?>("gh: HTTP 502"),
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, comment, _) => said = comment!.Post("Shelve it.").Result);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal("gh: HTTP 502", said);
    }

    [Fact]
    public void o_in_the_reader_opens_what_o_on_the_board_would_have()
    {
        var opened = new List<string>();
        using var window = Open(
            openUrl: Opening.Into(opened),
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, onGitHub, _, _, _, _) => onGitHub());
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal(["https://github.com/mentaldesk/team0/pull/122"], opened);
    }

    [Fact]
    public void Closing_the_reader_leaves_the_same_card_selected_and_the_board_unread()
    {
        var reads = 0;
        using var window = Open(
            read: team =>
            {
                reads++;
                return Task.FromResult(new Reading(Waiting(team), null));
            },
            readBody: _ => Task.FromResult(new Reading(Body, null)));
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        var selected = window.Work.Selected;
        var region = window.Work.Region;
        var before = reads;

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Same(selected, window.Work.Selected);
        Assert.Equal(region, window.Work.Region);
        Assert.Equal(before, reads);
    }

    [Fact]
    public void A_read_that_failed_opens_no_reader_and_says_why_in_the_error_colour()
    {
        var shown = false;
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading("", "board.sh: can't read #6 (gh: Not Found (HTTP 404))")),
            showBody: (_, _, _, _, _, _, _) => shown = true);
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.False(shown);
        Assert.Equal("board.sh: can't read #6 (gh: Not Found (HTTP 404))", window.Says());
        Assert.Equal(Severity.Error, window.Status.MessageSeverity);
    }

    [Fact]
    public void A_card_with_no_body_opens_no_reader_and_says_so_in_the_error_colour()
    {
        var shown = false;
        using var window = Open(showBody: (_, _, _, _, _, _, _) => shown = true);
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.False(shown);
        Assert.Equal("#107 has no description", window.Says());
        Assert.Equal(Severity.Error, window.Status.MessageSeverity);
    }

    [Fact]
    public void Reading_and_opening_on_GitHub_are_commands_of_their_own()
    {
        using var window = Open();

        Assert.Equal(
            [("work.read", "Open", "Enter"), ("work.github", "Open on GitHub", "G")],
            window.Commands.Registered
                .Where(command => command.Id is "work.read" or "work.github")
                .Select(command => (command.Id, command.Label, command.Keys)));
    }

    [Fact]
    public void A_second_p_while_the_first_is_still_reading_is_refused_not_queued()
    {
        var reads = 0;
        var finish = new TaskCompletionSource<Reading>();
        using var window = Open(readBody: _ =>
        {
            reads++;
            return finish.Task;
        });
        window.Refresh();
        LayOut(window, 120, 30);

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.Commands.Execute("work.priority");

        Assert.Equal(1, reads);
    }

    [Fact]
    public void Cancelling_the_dialog_asks_the_board_for_nothing()
    {
        var calls = 0;
        using var window = Open(run: _ =>
        {
            calls++;
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        LayOut(window, 120, 30);

        window.Commands.Execute("work.priority");
        window.Refresh();

        Assert.Equal(0, calls);
        Assert.Equal("#6 · waiting to be ranked", window.Says());
    }

    [Fact]
    public void Ctrl_G_D_goes_nowhere_now_and_says_it_isn_t_a_key()
    {
        using var window = Open();
        window.Refresh();

        window.Press("Ctrl+G D");

        Assert.Equal(Area.Work, window.CurrentArea);
        Assert.Equal("Ctrl+G D isn't a key", window.Says());
        Assert.DoesNotContain(window.Commands.Registered, command => command.Keys == "Ctrl+G D");
        Assert.DoesNotContain(window.MenuItems, item => item.Id == "view.dashboard");
    }

    [Fact]
    public void The_area_you_were_in_is_what_the_app_opens_in_next_time()
    {
        using var window = Open();

        window.Press("Ctrl+G O");

        Assert.Equal(Area.Overseer, new DashboardSettings(Config).ReadArea());
        Assert.True(window.Press("Ctrl+G W"));
        Assert.Equal(Area.Work, new DashboardSettings(Config).ReadArea());
    }

    [Fact]
    public void The_status_bar_says_when_the_cards_were_last_synced_from_GitHub()
    {
        var clock = new Clock();
        using var window = Open(read: team => Task.FromResult(new Reading(SyncedAt(team, clock.Now - TimeSpan.FromMinutes(2)), null)), clock: clock);
        Assert.Equal("All items", window.Status.FocusWord);

        window.Refresh();

        Assert.Equal("All items · synced 2m ago", window.Status.FocusWord);
    }

    [Fact]
    public void A_sync_that_has_only_just_landed_says_so()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("synced just now", new Synced(now, null).Stamp(now.AddSeconds(30)));
        Assert.Equal("synced 14m ago", new Synced(now, null).Stamp(now.AddMinutes(14)));
    }

    [Fact]
    public void A_sync_that_failed_says_GitHub_was_unreachable_and_when_one_last_worked()
    {
        var clock = new Clock();
        using var window = Open(read: team => Task.FromResult(new Reading(
            SyncedAt(team, clock.Now - TimeSpan.FromMinutes(14), clock.Now - TimeSpan.FromMinutes(1)), null)), clock: clock);

        window.Refresh();

        Assert.Equal("All items · synced 14m ago · GitHub unreachable, retrying", window.Status.FocusWord);
    }
}
