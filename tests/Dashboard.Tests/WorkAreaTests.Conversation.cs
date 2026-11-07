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
    public void Enter_shows_the_conversation_under_the_body()
    {
        IssueBody? shown = null;
        var at = new DateTimeOffset(2026, 9, 29, 4, 31, 0, TimeSpan.Zero);
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            readConversation: _ => Task.FromResult(new Reading(
                $$"""[{"who": "dev", "at": "{{at:O}}", "body": "Done.", "pr": 239, "description": true}]""", null)),
            showBody: (_, body, _, _, _, _, _) => shown = body);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(window.NewKeyDownEvent(Key.Enter));
        window.Refresh();

        Assert.Equal(new IssueBody("## Opportunity").With(new Conversation([new Remark("dev", at, "Done.", 239, true)])),
            shown);
    }

    [Fact]
    public void Enter_reads_the_card_s_history_for_the_reader_to_show_beside_the_body()
    {
        IssueBody? shown = null;
        var at = new DateTimeOffset(2026, 10, 3, 10, 41, 0, TimeSpan.Zero);
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            readHistory: item => Task.FromResult(new Reading(
                $$"""{"since": "{{at:O}}", "events": [{"at": "{{at:O}}", "who": "lead", "what": "added as Ready #{{item.Number}}"}]}""",
                null)),
            showBody: (_, body, _, _, _, _, _) => shown = body);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(window.NewKeyDownEvent(Key.Enter));
        window.Refresh();

        Assert.Equal("## Opportunity", shown?.Text);
        Assert.Equal([new HistoryEvent(at, "lead", "added as Ready #6")], shown?.History?.Events);
    }

    [Fact]
    public void A_history_that_wont_read_still_shows_the_body()
    {
        IssueBody? shown = null;
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            readHistory: _ => Task.FromResult(new Reading("", "board.sh: can't read the record")),
            showBody: (_, body, _, _, _, _, _) => shown = body);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(window.NewKeyDownEvent(Key.Enter));
        window.Refresh();

        Assert.Equal("## Opportunity", shown?.Text);
        Assert.Equal("board.sh: can't read the record", shown?.History?.Failure);
    }

    [Fact]
    public void A_task_waiting_on_another_repo_shows_under_Blocked_but_isn_t_waiting_on_you()
    {
        using var window = Open(read: team => Task.FromResult(new Reading(team == "team0" ? QuestionAndUpstream : "[]", null)));
        window.Refresh();

        Assert.Equal(2, window.Work.Lanes[0].Columns.Single(column => column.Gate == "Blocked").Count);
        Assert.Equal("Work · 1 waiting on you", window.WorkTitle);
    }

    [Fact]
    public void A_question_reads_its_body_conversation_and_history_like_any_other_card()
    {
        var at = new DateTimeOffset(2026, 9, 29, 4, 31, 0, TimeSpan.Zero);
        IssueBody? shown = null;
        using var window = Open(
            read: _ => Task.FromResult(new Reading(Question, null)),
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            readConversation: _ => Task.FromResult(new Reading(
                $$"""[{"who": "dev", "at": "{{at:O}}", "body": "Which marker should it post?"}]""", null)),
            readHistory: _ => Task.FromResult(new Reading("""{"since": null, "events": []}""", null)),
            showBody: (_, body, _, _, _, _, _) => shown = body);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(window.NewKeyDownEvent(Key.Enter));
        window.Refresh();

        Assert.Equal(new IssueBody("## Opportunity").With(new Conversation([new Remark("dev", at, "Which marker should it post?")])).Text,
            shown?.Text);
        Assert.NotNull(shown?.History);
    }

    [Fact]
    public void A_card_opened_before_reads_its_conversation_since_then_as_new_even_after_a_restart()
    {
        var clock = new Clock();
        var before = clock.Now.AddHours(-1);
        var after = clock.Now.AddHours(1);
        IssueBody? shown = null;
        DashboardWindow Window() => Open(
            clock: clock,
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            readConversation: _ => Task.FromResult(new Reading(
                $$"""[{"who": "dev", "at": "{{before:O}}", "body": "Old."}, {"who": "lead", "at": "{{after:O}}", "body": "New."}]""",
                null)),
            showBody: (_, body, _, _, _, _, _) => shown = body);
        using (var first = Window())
        {
            first.Refresh();
            LayOut(first, 120, 30);
            first.NewKeyDownEvent(Key.Enter);
            first.Refresh();
        }
        Assert.Null(shown?.Opened);
        Assert.DoesNotContain(" · new ", shown?.Text);

        using var again = Window();
        again.Refresh();
        LayOut(again, 120, 30);
        again.NewKeyDownEvent(Key.Enter);
        again.Refresh();

        Assert.Equal(clock.Now, shown?.Opened);
        Assert.Equal([false, true], shown?.Remarks.Select(said => shown.Lines[said.Line].Text.Contains(" · new ")));
    }

    [Fact]
    public void A_conversation_that_wont_read_still_shows_the_body_with_the_reason_under_it()
    {
        IssueBody? shown = null;
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            readConversation: _ => Task.FromResult(new Reading("", "board.sh: can't read the conversation on #6 (gh: HTTP 502)")),
            showBody: (_, body, _, _, _, _, _) => shown = body);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(window.NewKeyDownEvent(Key.Enter));
        window.Refresh();

        Assert.Equal("## Opportunity\n\nboard.sh: can't read the conversation on #6 (gh: HTTP 502)", shown?.Text);
    }

    [Fact]
    public void Enter_on_a_question_opens_the_reader_on_the_task_it_s_about()
    {
        var read = new List<WaitingItem>();
        (WaitingItem Item, IssueBody Body)? shown = null;
        using var window = Open(
            read: _ => Task.FromResult(new Reading(Question, null)),
            readBody: item =>
            {
                read.Add(item);
                return Task.FromResult(new Reading(Body, null));
            },
            showBody: (item, body, _, _, _, _, _) => shown = (item, body));
        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal("Blocked · team0", window.Work.Region);
        Assert.Equal("#192 · held by the Dev · Which marker should it post?", window.Work.Selected?.Line);

        Assert.True(window.NewKeyDownEvent(Key.Enter));
        window.Refresh();

        Assert.Equal([192], read.Select(item => item.Number));
        Assert.Equal(192, shown?.Item.Number);
        Assert.Equal(new IssueBody("## Opportunity"), shown?.Body);
    }

    [Fact]
    public void Enter_on_a_pitch_s_question_opens_the_reader_on_it_and_a_approves_the_pitch()
    {
        var calls = new List<string[]>();
        IssueBody? shown = null;
        using var window = Open(
            read: _ => Task.FromResult(new Reading(PitchQuestion, null)),
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, body, _, approve, _, _, _) =>
            {
                shown = body;
                approve!();
            });
        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal("Blocked · team0", window.Work.Region);

        Assert.True(window.NewKeyDownEvent(Key.Enter));
        window.Refresh();

        Assert.Equal(new IssueBody("## Opportunity"), shown);
        Assert.Equal(["board", "team0", "approve", "you", "257"], Assert.Single(calls));
    }

    [Fact]
    public void a_in_the_reader_approves_the_pitch_it_shows_and_the_card_leaves_with_no_re_read()
    {
        var calls = new List<string[]>();
        var reads = 0;
        using var window = Open(
            read: team =>
            {
                reads++;
                return Task.FromResult(new Reading(Waiting(team), null));
            },
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, onApprove, _, _, _) => onApprove!());
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(107, window.Work.Selected?.Number);
        var before = reads;

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal([["board", "team0", "approve", "you", "107"]], calls);
        Assert.Equal("◇ Pitches · 1", window.Work.Lanes[0].Columns[1].Title);
        Assert.Equal(108, window.Work.Selected?.Number);
        Assert.Equal(before, reads);
        Assert.Equal("#107 approved", window.Says());
    }

    [Fact]
    public void a_in_the_reader_asks_first_and_approves_nothing_if_you_say_no()
    {
        var calls = new List<string[]>();
        var asked = new List<int>();
        bool? went = null;
        using var window = Open(
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, onApprove, _, _, _) => went = onApprove!(),
            confirmApprove: item =>
            {
                asked.Add(item.Number);
                return false;
            });
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal([107], asked);
        Assert.False(went);
        Assert.Empty(calls);
        Assert.Equal(107, window.Work.Selected?.Number);
    }

    [Fact]
    public void A_failed_approve_leaves_the_card_where_it_was_and_says_why_in_the_error_colour()
    {
        using var window = Open(
            run: _ => Task.FromResult<string?>("board.sh: only a Pitched pitch can be approved (#107 is in 'Approved')"),
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, onApprove, _, _, _) => onApprove!());
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
        window.Refresh();

        Assert.Equal("◇ Pitches · 2", window.Work.Lanes[0].Columns[1].Title);
        Assert.Equal(107, window.Work.Selected?.Number);
        Assert.Equal("approving #107: board.sh: only a Pitched pitch can be approved (#107 is in 'Approved')", window.Says());
        Assert.Equal(Severity.Error, window.Status.MessageSeverity);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    public void The_reader_offers_no_approve_on_anything_but_a_Pitched_pitch(int right, int down)
    {
        var offered = new List<bool>();
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, onApprove, _, _, _) => offered.Add(onApprove is not null));
        window.Refresh();
        LayOut(window, 120, 30);
        for (var i = 0; i < right; i++)
            window.NewKeyDownEvent(Key.CursorRight);
        for (var i = 0; i < down; i++)
            window.NewKeyDownEvent(Key.CursorDown);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal([false], offered);
    }

    [Fact]
    public void A_Pitched_item_that_isn_t_a_pitch_offers_no_approve()
    {
        var offered = new List<bool>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? Unranked : "[]", null)),
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, onApprove, _, _, _) => offered.Add(onApprove is not null));
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal([false], offered);
    }

    [Fact]
    public void work_approve_is_registered_on_a_but_neither_bound_nor_hinted_on_the_board()
    {
        var calls = new List<string[]>();
        using var window = Open(run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        var approve = window.Commands.Registered.Single(command => command.Id == "work.approve");
        Assert.Equal("Approve the pitch you're reading", approve.Label);
        Assert.Equal("A", approve.Keys);
        Assert.False(window.Commands.IsEnabled("work.approve"));

        Assert.False(window.NewKeyDownEvent(new Key('a')));
        window.Refresh();

        Assert.Empty(calls);
        Assert.Equal("◇ Pitches · 2", window.Work.Lanes[0].Columns[1].Title);
    }

    [Fact]
    public void x_on_an_Idea_opens_the_reader_declining_and_the_card_leaves_once_it_s_declined()
    {
        var calls = new List<string[]>();
        var reasons = new List<string>();
        var reads = 0;
        ReaderDecline? offered = null;
        using var window = Open(
            read: team =>
            {
                reads++;
                return Task.FromResult(new Reading(Waiting(team), null));
            },
            run: arguments =>
            {
                calls.Add(arguments);
                reasons.Add(File.ReadAllText(arguments[^1]));
                return Task.FromResult<string?>(null);
            },
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, comment, _) =>
            {
                offered = comment?.Decline;
                Assert.Null(offered!.Run("Covered by #46.").Result);
            });
        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal(6, window.Work.Selected?.Number);
        var before = reads;

        Assert.True(window.NewKeyDownEvent(new Key('x')));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(offered?.Open);
        Assert.Equal(new Key('x'), offered?.Key);
        Assert.Equal("board team0 decline you 6", string.Join(' ', Assert.Single(calls)[..5]));
        Assert.Equal(["Covered by #46."], reasons);
        Assert.DoesNotContain(window.Work.Items, item => item.Number == 6);
        Assert.Equal(before, reads);
        Assert.Equal("#6 declined", window.Says());
    }
}
