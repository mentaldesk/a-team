using System.Drawing;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

/// <summary>The reader: the body as written, the keys that scroll it, and the two ways out.</summary>
public class ReaderDialogTests
{
    private const string Pitch = """
        ## Opportunity

        Switching between two files means finding your place again in both.

        ```
        ┌─ Work ───────────┐
        │ #180  a pitch    │
        └──────────────────┘
        ```

        | Option | Cost |
        |--------|------|
        | Reader | low  |
        """;

    private static readonly Remark Said = new("you", DateTimeOffset.UnixEpoch, "Shelve it until #150 lands.");

    private static readonly WaitingItem Item =
        new(180, "The dashboard tells me a pitch needs me", "Pitched", "https://github.com/x/180", "a-team",
            "you", "awaiting your approval since 22:25");

    private static ReaderDialog Open(string body, Action? onGitHub = null, int height = 20, Action? onApprove = null)
    {
        var dialog = new ReaderDialog(Item, new IssueBody(body), onGitHub ?? (() => { }), onApprove);
        dialog.Layout(new Size(60, height));
        return dialog;
    }

    [Fact]
    public void A_question_s_title_names_the_task_and_says_it_is_the_Dev_s_question()
    {
        var question = Item with { Status = "Ready", Question = "Which marker?" };
        using var dialog = new ReaderDialog(question, new IssueBody(question.Question), () => { });

        Assert.Equal("#180  The dashboard tells me a pitch needs me · the Dev's question", dialog.Title);
        Assert.Equal(["Which marker?"], dialog.Body.Lines.Select(line => line.Text));
    }

    [Fact]
    public void A_pitch_s_question_is_the_Lead_s()
    {
        var question = Item with { Pitch = true, Question = "## Needs your answer" };
        using var dialog = new ReaderDialog(question, new IssueBody(question.Question), () => { });

        Assert.Equal("#180  The dashboard tells me a pitch needs me · the Lead's question", dialog.Title);
    }

    [Fact]
    public void The_body_is_shown_character_for_character_fences_tables_and_all()
    {
        using var dialog = Open(Pitch);

        Assert.Equal(Pitch.Split('\n'), dialog.Body.Lines.Select(line => line.Text));
    }

    [Fact]
    public void The_body_is_read_as_markdown_on_the_reader_s_own_surface()
    {
        using var dialog = Open(Pitch);

        Assert.True(dialog.Body.ReadsMarkdown);
        Assert.Equal(LogSchemes.Reader, dialog.Body.SchemeName);
        Assert.Equal(LogLineKind.Heading, dialog.Body.Lines[0].Kind);
    }

    [Fact]
    public void The_title_carries_the_number_and_the_title()
    {
        using var dialog = Open(Pitch);

        Assert.Equal("#180  The dashboard tells me a pitch needs me", dialog.Title);
    }

    [Fact]
    public void It_opens_on_the_text_at_the_top()
    {
        using var dialog = Open(Long(), height: 10);

        Assert.True(dialog.Body.HasFocus);
        Assert.False(dialog.Body.Following);
        Assert.Equal(0, dialog.Body.Top);
    }

    [Fact]
    public void The_hints_name_the_reader_s_keys_on_its_last_row()
    {
        using var dialog = Open(Pitch);

        Assert.Equal("h show history · Shift+arrows select · g on GitHub · Esc close", dialog.Hints.Says);
        Assert.Equal(dialog.Viewport.Height - 1, dialog.Hints.Frame.Y);
    }

    [Fact]
    public void A_pitch_to_approve_puts_a_between_scrolling_and_GitHub()
    {
        using var dialog = Open(Pitch, onApprove: () => { });

        Assert.Equal("h show history · Shift+arrows select · a approve · g on GitHub · Esc close", dialog.Hints.Says);
    }

    [Fact]
    public void a_approves_at_once()
    {
        var approved = 0;
        using var dialog = Open(Pitch, onApprove: () => approved++);

        Assert.True(dialog.NewKeyDownEvent(new Key('a')));

        Assert.Equal(1, approved);
    }

    [Fact]
    public void Clicking_the_approve_hint_approves()
    {
        var approved = 0;
        using var dialog = Open(Pitch, onApprove: () => approved++);

        dialog.Hints.Hints.Single(hint => hint.Text == "a approve").InvokeCommand(Command.Accept);

        Assert.Equal(1, approved);
    }

    [Fact]
    public void A_task_to_accept_puts_its_key_after_scrolling()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Pitch), () => { },
            accept: new ReaderCommand(new Key('A'), "accept", () => true));
        dialog.Layout(new Size(60, 20));

        Assert.Equal("h show history · Shift+arrows select · A accept · g on GitHub · Esc close", dialog.Hints.Says);
        Assert.True(dialog.Hints.Hints.Single(hint => hint.Text == "A accept").Enabled);
    }

    [Fact]
    public void Accept_on_a_PR_in_trouble_is_greyed_but_its_key_still_says_why()
    {
        var ran = 0;
        using var dialog = new ReaderDialog(Item, new IssueBody(Pitch), () => { },
            accept: new ReaderCommand(new Key('A'), "accept", () => ++ran < 0, Enabled: false));
        dialog.Layout(new Size(60, 20));

        Assert.False(dialog.Hints.Hints.Single(hint => hint.Text == "A accept").Enabled);
        Assert.True(dialog.NewKeyDownEvent(new Key('A')));
        Assert.Equal(1, ran);
    }

    [Fact]
    public void Accept_follows_its_key_not_a()
    {
        var ran = 0;
        using var dialog = new ReaderDialog(Item, new IssueBody(Pitch), () => { },
            accept: new ReaderCommand(new Key('z'), "accept", () => ++ran > 0));

        Assert.False(dialog.NewKeyDownEvent(new Key('A')));
        Assert.True(dialog.NewKeyDownEvent(new Key('z')));
        Assert.Equal(1, ran);
    }

    [Fact]
    public void Comment_sits_after_approve_and_before_GitHub()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Pitch), () => { }, () => { },
            comment: Commenting());
        dialog.Layout(new Size(80, 20));

        Assert.Equal("h show history · Shift+arrows select · a approve · c comment · g on GitHub · Esc close", dialog.Hints.Says);
    }

    [Fact]
    public void Without_a_pitch_to_approve_comment_is_still_offered()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Pitch), () => { },
            comment: Commenting());
        dialog.Layout(new Size(80, 20));

        Assert.Equal("h show history · Shift+arrows select · c comment · g on GitHub · Esc close", dialog.Hints.Says);
    }

    [Fact]
    public void A_posted_comment_leaves_the_reader_open_saying_so()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Long()), () => { },
            comment: Commenting());
        dialog.Layout(new Size(60, 10));

        Say(dialog, Said.Body);

        Assert.Equal("commented on #180", dialog.Message.Says);
    }

    [Fact]
    public void A_posted_comment_joins_the_end_of_the_conversation_in_view()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Long()), () => { },
            comment: Commenting());
        dialog.Layout(new Size(60, 10));
        var before = dialog.Body.Lines.Count;

        Say(dialog, Said.Body);
        dialog.Layout(new Size(60, 10));

        var added = dialog.Body.Lines.Skip(before).Select(line => line.Text).ToList();
        Assert.Contains(Said.Heading, added);
        Assert.Equal("Shelve it until #150 lands.", added[^1]);
        var top = dialog.Body.Top;
        dialog.NewKeyDownEvent(Key.End.WithCtrl);
        dialog.Layout(new Size(60, 10));
        Assert.Equal(dialog.Body.Top, top);
    }

    [Fact]
    public void With_nothing_to_approve_a_does_nothing()
    {
        using var dialog = Open(Pitch);

        Assert.False(dialog.NewKeyDownEvent(new Key('a')));
    }

    [Fact]
    public void Clicking_a_hint_runs_its_key()
    {
        var opened = 0;
        using var dialog = Open(Pitch, () => opened++);

        dialog.Hints.Hints.Single(hint => hint.Text == "g on GitHub").InvokeCommand(Command.Accept);

        Assert.Equal(1, opened);
    }

    [Fact]
    public void The_body_opens_with_a_cursor_that_the_arrows_PgUp_PgDn_Home_and_End_move()
    {
        using var dialog = Open(Long(), height: 10);

        Assert.True(dialog.Body.ShowsCaret);
        Assert.True(dialog.NewKeyDownEvent(Key.CursorDown));
        Assert.Equal((1, 0), dialog.Body.Caret);
        Assert.True(dialog.NewKeyDownEvent(Key.CursorRight));
        Assert.True(dialog.NewKeyDownEvent(Key.End));
        Assert.Equal((1, 6), dialog.Body.Caret);
        Assert.True(dialog.NewKeyDownEvent(Key.Home));
        Assert.Equal((1, 0), dialog.Body.Caret);

        Assert.True(dialog.NewKeyDownEvent(Key.PageDown));
        var page = dialog.Body.Top;
        Assert.True(page > 1);
        Assert.True(dialog.NewKeyDownEvent(Key.PageUp));
        Assert.Equal(0, dialog.Body.Top);

        Assert.True(dialog.NewKeyDownEvent(Key.End.WithCtrl));
        Assert.Equal(59, dialog.Body.Caret!.Value.Line);
        Assert.True(dialog.Body.Top > page);
        Assert.True(dialog.NewKeyDownEvent(Key.Home.WithCtrl));
        Assert.Equal(0, dialog.Body.Top);
    }

    [Fact]
    public void g_opens_the_item_on_GitHub()
    {
        var opened = 0;
        using var dialog = Open(Pitch, () => opened++);

        Assert.True(dialog.NewKeyDownEvent(new Key('g')));

        Assert.Equal(1, opened);
    }

    [Fact]
    public void Esc_closes_it()
    {
        using var dialog = Open(Pitch);

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));
    }

    [Fact]
    public void A_task_with_a_PR_puts_try_between_scrolling_and_accept()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Pitch), () => { },
            accept: new ReaderCommand(new Key('a'), "accept", () => true),
            tryIt: new ReaderTry(new Key('t'), (_, _) => { }));
        dialog.Layout(new Size(60, 20));

        Assert.Equal("h show history · Shift+arrows select · t try · a accept · g on GitHub · Esc close", dialog.Hints.Says);
    }

    [Fact]
    public void Without_a_PR_t_is_neither_hinted_nor_handled()
    {
        using var dialog = Open(Pitch);

        Assert.DoesNotContain("try", dialog.Hints.Says);
        Assert.False(dialog.NewKeyDownEvent(new Key('t')));
    }

    [Fact]
    public void Try_hands_over_what_the_reader_shows_and_where_it_s_scrolled_to()
    {
        var tried = new List<(IssueBody Body, int Top)>();
        var body = new IssueBody(Long());
        using var dialog = new ReaderDialog(Item, body, () => { },
            tryIt: new ReaderTry(new Key('T'), (shown, top) => tried.Add((shown, top))));
        dialog.Layout(new Size(60, 10));
        dialog.NewKeyDownEvent(Key.PageDown);
        var top = dialog.Body.Top;

        Assert.True(dialog.NewKeyDownEvent(new Key('T')));

        Assert.True(top > 0);
        Assert.Equal([(body, top)], tried);
    }

    [Fact]
    public void Reopened_after_a_try_it_opens_where_it_was_saying_what_went_wrong()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Long()), () => { },
            tryIt: new ReaderTry(new Key('t'), (_, _) => { }, Top: 12, Failure: "try team0 122 exited 1"));
        dialog.Layout(new Size(60, 10));

        Assert.Equal(12, dialog.Body.Top);
        Assert.Equal("try team0 122 exited 1", dialog.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), dialog.Message.SchemeName);
    }

    private static readonly History Recorded = new([
        new HistoryEvent(new(new DateTime(2026, 10, 3, 13, 40, 0, DateTimeKind.Local)), "dev", "In progress → In review"),
        .. Enumerable.Range(1, 40).Select(n =>
            new HistoryEvent(new(new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Local)), "lead", $"commented {n}")),
    ]);

    private static ReaderDialog Wide(ReaderPanes? panes = null, int width = 120)
    {
        var dialog = new ReaderDialog(Item, new IssueBody(Long(), History: Recorded), () => { }, panes: panes,
            width: width);
        dialog.Layout(new Size(width, 20));
        return dialog;
    }

    [Fact]
    public void A_posted_comment_tops_History_without_reopening_the_reader()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Long(), History: Recorded), () => { },
            comment: Commenting(), width: 120);
        dialog.Layout(new Size(120, 20));

        Say(dialog, Said.Body);

        Assert.EndsWith("you   commented", dialog.HistoryLog.Lines[0].Text);
        Assert.Equal(Recorded.Events.Count + 1, dialog.HistoryLog.Lines.Count);
    }

    [Fact]
    public void A_posted_comment_leaves_a_History_that_wouldnt_read_saying_so()
    {
        using var dialog = new ReaderDialog(Item,
            new IssueBody(Long(), History: new History([], Failure: "couldn't read #180's history")), () => { },
            comment: Commenting(), width: 120);

        Say(dialog, Said.Body);

        Assert.Equal(["couldn't read #180's history"], dialog.HistoryLog.Lines.Select(line => line.Text));
    }

    [Fact]
    public void A_wide_reader_shows_History_beside_the_body_newest_first()
    {
        using var dialog = Wide();

        Assert.True(dialog.HistoryShown);
        Assert.Equal("3 Oct 13:40  dev   In progress → In review", dialog.HistoryLog.Lines[0].Text);
        Assert.Equal(dialog.Viewport.Width - ReaderPanes.HistoryWidth, dialog.Body.SuperView!.Frame.Width);
        Assert.Equal("Tab switch pane · h hide history · Shift+arrows select · g on GitHub · Esc close",
            dialog.Hints.Says);
    }

    [Fact]
    public void h_hides_History_for_every_reader_after_and_the_body_takes_the_full_width()
    {
        var panes = new ReaderPanes();
        using (var dialog = Wide(panes))
        {
            Assert.True(dialog.NewKeyDownEvent(new Key('h')));
            dialog.Layout(new Size(120, 20));

            Assert.False(dialog.HistoryShown);
            Assert.Equal(dialog.Viewport.Width, dialog.Body.SuperView!.Frame.Width);
            Assert.Equal("h show history · Shift+arrows select · g on GitHub · Esc close", dialog.Hints.Says);
        }

        using var next = Wide(panes);
        Assert.False(next.HistoryShown);
        Assert.True(next.NewKeyDownEvent(new Key('h')));
        Assert.True(next.HistoryShown);
        Assert.True(panes.HistoryShown);
    }

    [Fact]
    public void A_narrow_terminal_opens_without_History_and_leaves_the_choice_as_it_was()
    {
        var panes = new ReaderPanes();
        using var dialog = Wide(panes, width: 80);

        Assert.False(dialog.HistoryShown);
        Assert.True(panes.HistoryShown);
    }

    [Fact]
    public void Tab_moves_between_the_panes_and_the_arrows_scroll_the_one_with_focus()
    {
        using var dialog = Wide();

        Assert.True(dialog.NewKeyDownEvent(Key.Tab));
        Assert.True(dialog.HistoryLog.HasFocus);
        Assert.True(dialog.HistoryLog.ShowsCaret);
        Assert.False(dialog.Body.ShowsCaret);
        dialog.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal((1, 0), dialog.HistoryLog.Caret);
        Assert.NotEqual((1, 0), dialog.Body.Caret);

        Assert.True(dialog.NewKeyDownEvent(Key.Tab));
        Assert.True(dialog.Body.HasFocus);
        dialog.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal((1, 0), dialog.Body.Caret);
        Assert.Equal((1, 0), dialog.HistoryLog.Caret);
    }

    [Fact]
    public void Hiding_History_while_it_has_focus_gives_focus_back_to_the_body()
    {
        using var dialog = Wide();
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(new Key('h'));

        Assert.True(dialog.Body.HasFocus);
    }

    private static ReaderDialog Ranking(
        string body = Pitch, Rank rank = Rank.None, int width = 60, int height = 20, Action? onApprove = null,
        ReaderComment? comment = null, Action? onGitHub = null, IssueBody? read = null)
    {
        var dialog = new ReaderDialog(Item, read ?? new IssueBody(body), onGitHub ?? (() => { }), onApprove,
            comment: comment, width: width, rank: rank);
        dialog.Layout(new Size(width, height));
        return dialog;
    }

    [Fact]
    public void Without_a_rank_there_is_no_row_of_ranks()
    {
        using var dialog = Open(Pitch);

        Assert.Null(dialog.Ranks);
        Assert.DoesNotContain("Enter set", dialog.Hints.Says);
        Assert.DoesNotContain("rank", dialog.Hints.Says);
    }

    [Theory]
    [InlineData(Rank.None)]
    [InlineData(Rank.Medium)]
    public void The_ranks_open_on_the_rank_given_with_the_keyboard_already_there(Rank rank)
    {
        using var dialog = Ranking(rank: rank);

        Assert.Equal(rank, dialog.Ranks!.Value);
        Assert.True(dialog.Ranks.HasFocus);
        Assert.Equal((int)rank, dialog.Ranks.FocusedItem);
    }

    [Fact]
    public void The_ranks_are_the_Priority_field_s_options_and_None_in_its_colours()
    {
        using var dialog = Ranking();

        Assert.Equal(["None", "Low", "Medium", "High", "Urgent"], dialog.Ranks!.Labels);
        Assert.Equal(Orientation.Horizontal, dialog.Ranks.Orientation);
        Assert.Equal(
            ["", "Priority.Low.Form", "Priority.Medium.Form", "Priority.High.Form", "Priority.Urgent.Form"],
            dialog.Ranks.SubViews.Select(row => row.SchemeName ?? ""));
    }

    [Theory]
    [InlineData("n", Rank.None)]
    [InlineData("l", Rank.Low)]
    [InlineData("m", Rank.Medium)]
    [InlineData("h", Rank.High)]
    [InlineData("u", Rank.Urgent)]
    public void A_rank_s_initial_puts_the_keyboard_on_it_without_setting_it(string key, Rank rank)
    {
        using var dialog = Ranking(rank: Rank.Low);

        dialog.NewKeyDownEvent(new Key(key[0]));

        Assert.Equal(rank, dialog.Ranks!.Value);
        Assert.Equal((int)rank, dialog.Ranks.FocusedItem);
        Assert.Null(dialog.Chosen);
    }

    [Fact]
    public void Left_and_Right_move_between_the_ranks_and_leave_the_body_where_it_was()
    {
        using var dialog = Ranking(Long(), Rank.Medium, height: 12);

        dialog.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(Rank.High, dialog.Ranks!.Value);
        Assert.Equal((int)Rank.High, dialog.Ranks.FocusedItem);

        dialog.NewKeyDownEvent(Key.CursorLeft);
        Assert.Equal(Rank.Medium, dialog.Ranks.Value);
        Assert.Equal((int)Rank.Medium, dialog.Ranks.FocusedItem);
        Assert.Equal(0, dialog.Body.Top);
    }

    [Theory]
    [InlineData(Rank.Urgent, "Right", Rank.None)]
    [InlineData(Rank.None, "Left", Rank.Urgent)]
    public void Left_and_Right_wrap_at_the_ends(Rank start, string arrow, Rank rank)
    {
        using var dialog = Ranking(rank: start);

        dialog.NewKeyDownEvent(arrow == "Right" ? Key.CursorRight : Key.CursorLeft);

        Assert.Equal(rank, dialog.Ranks!.Value);
        Assert.Equal((int)rank, dialog.Ranks.FocusedItem);
    }

    [Fact]
    public void Arrows_initials_and_the_rank_hint_all_keep_the_mark_on_the_highlighted_rank()
    {
        using var dialog = Ranking(rank: Rank.None);
        var rankHint = dialog.Hints.Hints.Single(hint => hint.Text == "←/→ rank");

        foreach (var move in new Action[]
        {
            () => dialog.NewKeyDownEvent(Key.CursorRight),
            () => dialog.NewKeyDownEvent(new Key('h')),
            () => rankHint.InvokeCommand(Command.Accept),
            () => dialog.NewKeyDownEvent(Key.CursorLeft),
            () => dialog.NewKeyDownEvent(new Key('l')),
            () => dialog.NewKeyDownEvent(Key.CursorLeft),
        })
        {
            move();
            Assert.Equal(dialog.Ranks!.FocusedItem, (int)dialog.Ranks.Value!);
        }
        Assert.Equal(Rank.None, dialog.Ranks!.Value);
    }

    [Fact]
    public void Enter_sets_the_rank_the_keyboard_is_on_and_closes()
    {
        using var dialog = Ranking(rank: Rank.Medium);

        dialog.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(Rank.High, dialog.Ranks!.Value);
        dialog.NewKeyDownEvent(Key.Enter);

        Assert.Equal(Rank.High, dialog.Chosen);
    }

    [Fact]
    public void Esc_closes_it_and_sets_nothing()
    {
        using var dialog = Ranking(rank: Rank.Medium);

        dialog.NewKeyDownEvent(Key.CursorRight);
        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.Null(dialog.Chosen);
    }

    [Fact]
    public void The_scrolling_keys_still_scroll_the_body_with_the_ranks_showing()
    {
        using var dialog = Ranking(Long(), Rank.Low, height: 12);

        Assert.True(dialog.NewKeyDownEvent(Key.CursorDown));
        Assert.Equal(1, dialog.Body.Top);
        Assert.True(dialog.NewKeyDownEvent(Key.CursorUp));
        Assert.Equal(0, dialog.Body.Top);
        Assert.True(dialog.NewKeyDownEvent(Key.PageDown));
        var page = dialog.Body.Top;
        Assert.True(page > 1);
        Assert.True(dialog.NewKeyDownEvent(Key.PageUp));
        Assert.Equal(0, dialog.Body.Top);
        Assert.True(dialog.NewKeyDownEvent(Key.End));
        Assert.True(dialog.Body.Top > page);
        Assert.True(dialog.NewKeyDownEvent(Key.Home));
        Assert.Equal(0, dialog.Body.Top);
        Assert.Equal(Rank.Low, dialog.Ranks!.Value);
    }

    [Fact]
    public void Tab_with_the_ranks_showing_scrolls_History_and_leaves_the_keyboard_on_the_ranks()
    {
        using var dialog = Ranking(Long(), Rank.Low, width: 120, height: 12, read: new IssueBody(Long())
        {
            History = new History([.. Enumerable.Range(1, 30).Select(day => new HistoryEvent(
                DateTimeOffset.UnixEpoch.AddDays(day), "lead", "moved"))]),
        });

        Assert.True(dialog.NewKeyDownEvent(Key.Tab));
        dialog.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal(1, dialog.HistoryLog.Top);
        Assert.Equal(0, dialog.Body.Top);
        Assert.True(dialog.Ranks!.HasFocus);
    }

    [Fact]
    public void The_hints_add_rank_and_set_and_leave_h_to_High()
    {
        using var dialog = Ranking(onApprove: () => { }, comment: Commenting(),
            width: 120);

        Assert.Equal(
            "Up/Down/PgUp/PgDn scroll · Tab switch pane · ←/→ rank · Enter set · a approve · c comment · g on GitHub · Esc close",
            dialog.Hints.Says);
    }

    [Fact]
    public void Clicking_set_sets_the_rank_and_clicking_rank_moves_to_the_next()
    {
        using var dialog = Ranking(rank: Rank.Low);

        dialog.Hints.Hints.Single(hint => hint.Text == "←/→ rank").InvokeCommand(Command.Accept);
        Assert.Equal(Rank.Medium, dialog.Ranks!.Value);
        dialog.Hints.Hints.Single(hint => hint.Text == "Enter set").InvokeCommand(Command.Accept);

        Assert.Equal(Rank.Medium, dialog.Chosen);
    }

    [Fact]
    public void Commenting_keeps_the_reader_open_with_the_rank_chosen_and_Enter_still_sets_it()
    {
        using var dialog = Ranking(Long(), Rank.None, height: 12,
            comment: Commenting());

        dialog.NewKeyDownEvent(new Key('m'));
        Say(dialog, Said.Body);

        Assert.Equal("commented on #180", dialog.Message.Says);
        Assert.Equal("Shelve it until #150 lands.", dialog.Body.Lines[^1].Text);
        Assert.Equal(Rank.Medium, dialog.Ranks!.Value);
        Assert.Null(dialog.Chosen);

        dialog.NewKeyDownEvent(Key.Enter);
        Assert.Equal(Rank.Medium, dialog.Chosen);
    }

    [Fact]
    public void g_and_a_still_work_with_the_ranks_showing()
    {
        var opened = 0;
        var approved = 0;
        using var dialog = Ranking(onGitHub: () => opened++, onApprove: () => approved++);

        Assert.True(dialog.NewKeyDownEvent(new Key('g')));
        Assert.True(dialog.NewKeyDownEvent(new Key('a')));

        Assert.Equal(1, opened);
        Assert.Equal(1, approved);
        Assert.Null(dialog.Chosen);
    }

    [Fact]
    public void The_ranks_sit_in_a_band_of_their_own_between_the_panes_and_the_hints()
    {
        using var dialog = Ranking();

        Assert.Equal(LogSchemes.Form, dialog.Band!.SchemeName);
        Assert.Equal(new Rectangle(0, dialog.Hints.Frame.Y - 3, dialog.Viewport.Width, 3), dialog.Band.Frame);
        Assert.Equal(dialog.Band.Frame.Y, dialog.Body.SuperView!.Frame.Bottom);
        Assert.Equal(1, dialog.Ranks!.Frame.Y);
        Assert.Equal(dialog.Band.Viewport.Width - dialog.Ranks.Frame.Right, dialog.Ranks.Frame.X);
    }

    [Fact]
    public void A_body_that_would_not_read_still_lets_you_rank_and_says_why()
    {
        using var dialog = Ranking(read: new IssueBody(Failure: "board.sh: can't read #180"));

        Assert.Equal("board.sh: can't read #180", dialog.Message.Says);
        dialog.NewKeyDownEvent(new Key('l'));
        dialog.NewKeyDownEvent(Key.Enter);

        Assert.Equal(Rank.Low, dialog.Chosen);
    }

    private static ReaderDialog Replying(
        int width = 140, Func<string, Task<string?>>? post = null, Func<bool>? confirmDiscard = null,
        Action? onApprove = null, ReaderCommand? accept = null, string? body = null, int height = 20)
    {
        var dialog = new ReaderDialog(Item, new IssueBody(body ?? Long(), History: Recorded), () => { }, onApprove,
            accept, Commenting(post), width: width, confirmDiscard: confirmDiscard);
        dialog.Layout(new Size(width, height));
        return dialog;
    }

    private static void Open(ReaderDialog dialog, int width = 140, int height = 20)
    {
        dialog.NewKeyDownEvent(new Key('c'));
        dialog.Layout(new Size(width, height));
    }

    [Fact]
    public void c_opens_the_comment_on_the_left_of_the_body_with_the_cursor_in_it()
    {
        using var dialog = Replying();

        Open(dialog);

        Assert.True(dialog.CommentShown);
        Assert.True(dialog.Field.HasFocus);
        Assert.True(dialog.Field.WordWrap);
        Assert.Equal(new Rectangle(0, 0, ReaderPanes.CommentWidth, dialog.Body.SuperView!.Frame.Height),
            dialog.Field.SuperView!.Frame);
        Assert.Equal(ReaderPanes.CommentWidth, dialog.Body.SuperView.Frame.X);
        Assert.True(dialog.HistoryShown);
        Assert.Equal(dialog.Viewport.Width - ReaderPanes.HistoryWidth, dialog.Body.SuperView.Frame.Right);
    }

    [Fact]
    public void Opening_the_comment_leaves_a_body_that_now_wraps_where_you_were_reading()
    {
        var body = string.Join('\n', Enumerable.Range(1, 12).Select(line => $"{line} {new string('x', 70)}"));
        using var dialog = Replying(body: body);

        Open(dialog);

        Assert.Equal(0, dialog.Body.Top);
    }

    [Fact]
    public void Tab_cycles_comment_body_and_History_and_Shift_Tab_goes_back()
    {
        using var dialog = Replying();
        Open(dialog);

        dialog.NewKeyDownEvent(Key.Tab);
        Assert.True(dialog.Body.HasFocus);
        dialog.NewKeyDownEvent(Key.Tab);
        Assert.True(dialog.HistoryLog.HasFocus);
        dialog.NewKeyDownEvent(Key.Tab);
        Assert.True(dialog.Field.HasFocus);
        dialog.NewKeyDownEvent(Key.Tab.WithShift);
        Assert.True(dialog.HistoryLog.HasFocus);
        dialog.NewKeyDownEvent(Key.Tab.WithShift);
        Assert.True(dialog.Body.HasFocus);
    }

    [Fact]
    public void Without_History_Tab_moves_between_the_comment_and_the_body()
    {
        using var dialog = Replying();
        dialog.NewKeyDownEvent(new Key('h'));
        Open(dialog);

        dialog.NewKeyDownEvent(Key.Tab);
        Assert.True(dialog.Body.HasFocus);
        dialog.NewKeyDownEvent(Key.Tab);
        Assert.True(dialog.Field.HasFocus);
    }

    [Fact]
    public void Esc_in_the_comment_goes_back_to_the_pane_you_came_from_and_c_returns_to_your_draft()
    {
        using var dialog = Replying();
        dialog.NewKeyDownEvent(Key.Tab);
        Open(dialog);
        dialog.Field.Text = "half a thought";

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));
        Assert.True(dialog.HistoryLog.HasFocus);
        Assert.True(dialog.CommentShown);

        dialog.NewKeyDownEvent(new Key('c'));
        Assert.True(dialog.Field.HasFocus);
        Assert.Equal("half a thought", dialog.Field.Text);
    }

    [Fact]
    public void The_caret_starts_on_the_top_line_in_view_and_the_arrows_move_it()
    {
        using var dialog = Replying();
        dialog.NewKeyDownEvent(Key.CursorDown);
        dialog.NewKeyDownEvent(Key.CursorDown);
        Open(dialog);
        Assert.False(dialog.Body.ShowsCaret);

        dialog.NewKeyDownEvent(Key.Tab);
        Assert.True(dialog.Body.ShowsCaret);
        Assert.Equal((2, 0), dialog.Body.Caret);

        dialog.NewKeyDownEvent(Key.CursorRight);
        dialog.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal((3, 1), dialog.Body.Caret);
        dialog.NewKeyDownEvent(Key.End);
        Assert.Equal((3, 6), dialog.Body.Caret);
        dialog.NewKeyDownEvent(Key.Home);
        dialog.NewKeyDownEvent(Key.CursorLeft);
        Assert.Equal((2, 6), dialog.Body.Caret);
    }

    [Fact]
    public void Up_and_Down_keep_the_caret_s_column_across_a_shorter_line()
    {
        using var dialog = Replying(body: "a longer line\nab\nanother line");
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(Key.End);
        dialog.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal((1, 2), dialog.Body.Caret);
        dialog.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal((2, 12), dialog.Body.Caret);
    }

    [Fact]
    public void In_a_wrapped_line_the_caret_moves_a_row_at_a_time()
    {
        using var dialog = Replying(body: string.Join(' ', Enumerable.Repeat("word", 60)));
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(Key.CursorDown);
        var (line, offset) = dialog.Body.Caret!.Value;
        dialog.NewKeyDownEvent(Key.End);
        dialog.NewKeyDownEvent(Key.Home);

        Assert.Equal(0, line);
        Assert.True(offset > 0);
        Assert.Equal((0, offset), dialog.Body.Caret);
    }

    [Fact]
    public void The_pane_scrolls_to_keep_the_caret_in_view()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);

        for (var i = 0; i < 30; i++)
            dialog.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal((30, 0), dialog.Body.Caret);
        Assert.InRange(30, dialog.Body.Top, dialog.Body.Top + dialog.Body.Viewport.Height - 1);
        dialog.NewKeyDownEvent(Key.Home.WithCtrl);
        Assert.Equal(0, dialog.Body.Top);
    }

    [Fact]
    public void Shift_and_the_arrows_select_part_of_a_line_or_across_lines()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);
        for (var i = 0; i < 5; i++)
            dialog.NewKeyDownEvent(Key.CursorRight);

        dialog.NewKeyDownEvent(Key.End.WithShift);
        Assert.Equal(["1"], dialog.Body.MarkedText());
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        Assert.Equal(["1", "line 2"], dialog.Body.MarkedText());
        dialog.NewKeyDownEvent(Key.CursorLeft.WithShift);
        Assert.Equal(["1", "line "], dialog.Body.MarkedText());
        dialog.NewKeyDownEvent(Key.CursorUp.WithShift);
        Assert.Equal(0, dialog.Body.Marked);
        dialog.NewKeyDownEvent(Key.Home.WithShift);
        Assert.Equal(["line "], dialog.Body.MarkedText());
    }

    [Fact]
    public void Shift_Down_from_the_start_of_a_line_selects_that_line_alone()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        Assert.Equal(["line 1"], dialog.Body.MarkedText());
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        Assert.Equal(["line 1", "line 2"], dialog.Body.MarkedText());
        Assert.Equal(2, dialog.Body.Marked);
    }

    [Fact]
    public void Moving_without_Shift_drops_the_selection()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);

        dialog.NewKeyDownEvent(Key.CursorRight);

        Assert.Equal(0, dialog.Body.Marked);
    }

    [Fact]
    public void History_has_a_caret_of_its_own()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);
        dialog.NewKeyDownEvent(Key.CursorDown);
        dialog.NewKeyDownEvent(Key.Tab);

        Assert.False(dialog.Body.ShowsCaret);
        Assert.True(dialog.HistoryLog.ShowsCaret);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);

        Assert.Equal(dialog.HistoryLog.Lines.Take(2).Select(line => line.Text), dialog.HistoryLog.MarkedText());
        Assert.Equal(0, dialog.Body.Marked);
        Assert.Equal((1, 0), dialog.Body.Caret);
    }

    [Fact]
    public void Back_in_the_comment_neither_pane_shows_its_caret()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(new Key('c'));

        Assert.False(dialog.Body.ShowsCaret);
        Assert.False(dialog.HistoryLog.ShowsCaret);
    }

    [Fact]
    public void The_hints_count_the_selected_lines_and_Esc_clears_them_first()
    {
        var asked = 0;
        using var dialog = Replying(confirmDiscard: () => ++asked > 0);
        Open(dialog);
        Assert.Equal(
            "Tab switch pane · Shift+arrows select · Ctrl+Enter post · g on GitHub · Esc back",
            dialog.Hints.Says);
        dialog.Field.Text = "draft";
        dialog.NewKeyDownEvent(Key.Tab);
        Assert.EndsWith("Shift+arrows select · Ctrl+Enter post · g on GitHub · Esc close", dialog.Hints.Says);

        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        Assert.Contains("Tab switch pane · Ctrl+C copy · q quote 1 line · Ctrl+Enter post", dialog.Hints.Says);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        Assert.Contains("q quote 2 lines", dialog.Hints.Says);
        Assert.EndsWith("Esc clear", dialog.Hints.Says);

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));
        Assert.Equal(0, dialog.Body.Marked);
        Assert.DoesNotContain("quote", dialog.Hints.Says);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void q_quotes_the_selection_into_the_comment_and_takes_the_keyboard_below_it()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);

        Assert.True(dialog.NewKeyDownEvent(new Key('q')));

        Assert.Equal("> line 1\n> line 2\n\n", dialog.Field.Text);
        Assert.True(dialog.Field.HasFocus);
        dialog.Field.InsertText("Yes.");
        Assert.Equal("> line 1\n> line 2\n\nYes.", dialog.Field.Text);
        Assert.Equal(0, dialog.Body.Marked);
        Assert.DoesNotContain("quote", dialog.Hints.Says);

        dialog.NewKeyDownEvent(Key.Esc);
        Assert.True(dialog.Body.HasFocus);
    }

    [Fact]
    public void Quotes_go_in_where_the_comment_s_cursor_is_as_often_as_you_like()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.Field.Text = "Agreed.";
        dialog.Field.MoveEnd();
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        dialog.NewKeyDownEvent(new Key('q'));
        dialog.NewKeyDownEvent(Key.Esc);
        dialog.NewKeyDownEvent(Key.CursorDown);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        dialog.NewKeyDownEvent(new Key('q'));
        dialog.Field.InsertText("But not this.");

        Assert.Equal("Agreed.\n> line 1\n\n> line 3\n\nBut not this.", dialog.Field.Text);
    }

    [Fact]
    public void A_quote_at_the_start_of_the_draft_goes_before_it()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.Field.Text = "Agreed.";
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        dialog.NewKeyDownEvent(new Key('q'));

        Assert.Equal("> line 1\n\nAgreed.", dialog.Field.Text);
    }

    [Fact]
    public void A_quote_in_the_middle_of_a_line_starts_on_a_line_of_its_own()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.Field.Text = "Agreed, but";
        dialog.Field.InsertionPoint = new Point(7, 0);
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        dialog.NewKeyDownEvent(new Key('q'));

        Assert.Equal("Agreed,\n> line 1\n\n but", dialog.Field.Text);
    }

    [Fact]
    public void A_quote_keeps_blank_lines_and_quotes_inside_it()
    {
        Assert.Equal("> ## Assumed\n>\n> > as written\n\n", ReaderDialog.Quote(["## Assumed", "", "> as written"], midLine: false));
        Assert.Equal("\n> one\n\n", ReaderDialog.Quote(["one"], midLine: true));
    }

    [Fact]
    public void Without_the_comment_open_q_opens_it_quoting_part_of_a_line()
    {
        using var dialog = Replying();
        dialog.NewKeyDownEvent(Key.CursorRight);
        dialog.NewKeyDownEvent(Key.CursorRight);

        dialog.NewKeyDownEvent(Key.End.WithShift);
        Assert.Contains("Ctrl+C copy · q quote 1 line", dialog.Hints.Says);
        Assert.True(dialog.NewKeyDownEvent(new Key('q')));

        Assert.True(dialog.CommentShown);
        Assert.True(dialog.Field.HasFocus);
        Assert.Equal("> ne 1\n\n", dialog.Field.Text);
    }

    [Fact]
    public void Without_a_selection_q_does_nothing()
    {
        using var dialog = Replying();

        Assert.False(dialog.NewKeyDownEvent(new Key('q')));

        Assert.False(dialog.CommentShown);
        Assert.DoesNotContain("copy", dialog.Hints.Says);
    }

    private static ReaderDialog Copying(FakeClipboard clipboard, string body, int width = 60)
    {
        var dialog = new ReaderDialog(Item, new IssueBody(body), () => { }, clipboard: clipboard);
        dialog.Layout(new Size(width, 20));
        return dialog;
    }

    [Fact]
    public void Ctrl_C_copies_part_of_a_line_and_says_how_many_characters()
    {
        var clipboard = new FakeClipboard();
        using var dialog = Copying(clipboard, "See #388 for why.");
        for (var i = 0; i < 4; i++)
            dialog.NewKeyDownEvent(Key.CursorRight);
        for (var i = 0; i < 4; i++)
            dialog.NewKeyDownEvent(Key.CursorRight.WithShift);

        Assert.True(dialog.NewKeyDownEvent(Key.C.WithCtrl));

        Assert.Equal("#388", clipboard.GetClipboardData());
        Assert.Equal("copied 4 characters", dialog.Message.Says);
    }

    [Fact]
    public void Ctrl_C_copies_across_lines()
    {
        var clipboard = new FakeClipboard();
        using var dialog = Copying(clipboard, "one\ntwo\nthree");
        dialog.NewKeyDownEvent(Key.CursorRight);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);

        dialog.NewKeyDownEvent(Key.C.WithCtrl);

        Assert.Equal("ne\ntwo\nt", clipboard.GetClipboardData());
        Assert.Equal("copied 8 characters", dialog.Message.Says);
    }

    [Fact]
    public void Ctrl_C_copies_a_wrapped_line_as_it_was_written()
    {
        var clipboard = new FakeClipboard();
        var line = string.Join(' ', Enumerable.Range(1, 30).Select(word => $"word{word}"));
        using var dialog = Copying(clipboard, line, width: 30);
        Assert.True(dialog.Body.Rows(dialog.Body.Viewport.Width).Count > 2);

        dialog.NewKeyDownEvent(Key.End.WithCtrl.WithShift);
        dialog.NewKeyDownEvent(Key.C.WithCtrl);

        Assert.Equal(line, clipboard.GetClipboardData());
    }

    [Fact]
    public void Ctrl_C_copies_from_History_too()
    {
        var clipboard = new FakeClipboard();
        using var dialog = new ReaderDialog(Item, new IssueBody("body", History: Recorded), () => { }, width: 140,
            clipboard: clipboard);
        dialog.Layout(new Size(140, 20));
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(Key.End.WithShift);
        dialog.NewKeyDownEvent(Key.C.WithCtrl);

        Assert.Equal(dialog.HistoryLog.Lines[0].Text, clipboard.GetClipboardData());
    }

    [Fact]
    public void Without_a_clipboard_copying_says_so()
    {
        using var dialog = Copying(new FakeClipboard(isSupportedAlwaysFalse: true), "text");
        dialog.NewKeyDownEvent(Key.End.WithShift);

        dialog.NewKeyDownEvent(Key.C.WithCtrl);

        Assert.Equal("there's no clipboard to copy to", dialog.Message.Says);
    }

    [Theory]
    [InlineData("x", "copied 1 character")]
    [InlineData("ab\nc", "copied 4 characters")]
    public void The_copy_message_counts_characters(string text, string said) =>
        Assert.Equal(said, ReaderDialog.Copied(text));

    [Fact]
    public void The_reader_s_letters_reach_its_commands_from_either_pane_and_leave_the_text_alone()
    {
        var opened = 0;
        var approved = 0;
        var dialog = new ReaderDialog(Item, new IssueBody(Long(), History: Recorded), () => opened++, () => approved++,
            comment: Commenting(), width: 140);
        dialog.Layout(new Size(140, 20));
        var body = dialog.Body.Lines;
        var history = dialog.HistoryLog.Lines;

        dialog.NewKeyDownEvent(Key.Tab);
        Assert.True(dialog.HistoryLog.HasFocus);
        Assert.True(dialog.NewKeyDownEvent(new Key('g')));
        Assert.True(dialog.NewKeyDownEvent(new Key('h')));
        Assert.False(dialog.HistoryShown);
        Assert.True(dialog.Body.HasFocus);
        dialog.NewKeyDownEvent(new Key('x'));
        Assert.True(dialog.NewKeyDownEvent(new Key('c')));
        Assert.True(dialog.CommentShown);
        dialog.NewKeyDownEvent(Key.Esc);
        Assert.True(dialog.NewKeyDownEvent(new Key('a')));

        Assert.Equal(1, opened);
        Assert.Equal(1, approved);
        Assert.Same(body, dialog.Body.Lines);
        Assert.Same(history, dialog.HistoryLog.Lines);
        dialog.Dispose();
    }

    [Fact]
    public void Ctrl_Enter_posts_from_the_body_and_closes_the_pane()
    {
        var posted = new List<string>();
        using var dialog = Replying(post: body =>
        {
            posted.Add(body);
            return Task.FromResult<string?>(null);
        });
        Open(dialog);
        dialog.Field.Text = "Ship it.";
        dialog.NewKeyDownEvent(Key.Tab);

        Assert.True(dialog.NewKeyDownEvent(Key.Enter.WithCtrl));

        Assert.Equal(["Ship it."], posted);
        Assert.False(dialog.CommentShown);
        Assert.Equal("", dialog.Field.Text);
        Assert.True(dialog.Body.HasFocus);
        Assert.Equal("commented on #180", dialog.Message.Says);
        Assert.Equal("Ship it.", dialog.Body.Lines[^1].Text);
        Assert.True(dialog.Body.ShowsCaret);
        Assert.EndsWith("you   commented", dialog.HistoryLog.Lines[0].Text);
    }

    [Fact]
    public void A_comment_that_fails_to_post_stays_in_the_pane_saying_why()
    {
        using var dialog = Replying(post: _ => Task.FromResult<string?>("gh: HTTP 502"));
        Open(dialog);
        dialog.Field.Text = "Ship it.";

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.True(dialog.CommentShown);
        Assert.Equal("Ship it.", dialog.Field.Text);
        Assert.Equal("gh: HTTP 502", dialog.Message.Says);
    }

    [Fact]
    public void An_empty_comment_is_not_posted()
    {
        var posted = 0;
        using var dialog = Replying(post: _ => Task.FromResult<string?>(++posted < 0 ? "" : null));
        Open(dialog);

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Equal(0, posted);
        Assert.Equal("Nothing to post: the comment is empty", dialog.Message.Says);
    }

    [Fact]
    public void Leaving_with_a_draft_asks_first_and_staying_keeps_it()
    {
        var asked = 0;
        var approved = 0;
        var accepted = 0;
        using var dialog = Replying(confirmDiscard: () => ++asked < 0, onApprove: () => approved++,
            accept: new ReaderCommand(new Key('m'), "accept", () => ++accepted > 0));
        Open(dialog);
        dialog.Field.Text = "half a thought";
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(Key.Esc);
        dialog.NewKeyDownEvent(new Key('a'));
        dialog.NewKeyDownEvent(new Key('m'));

        Assert.Equal(3, asked);
        Assert.Equal(0, approved);
        Assert.Equal(0, accepted);
        Assert.Equal("half a thought", dialog.Field.Text);
    }

    [Fact]
    public void Leaving_with_a_draft_goes_ahead_once_you_discard_it()
    {
        var approved = 0;
        using var dialog = Replying(confirmDiscard: () => true, onApprove: () => approved++);
        Open(dialog);
        dialog.Field.Text = "half a thought";
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(new Key('a'));

        Assert.Equal(1, approved);
    }

    [Fact]
    public void Leaving_with_an_empty_comment_asks_nothing()
    {
        var asked = 0;
        var approved = 0;
        using var dialog = Replying(confirmDiscard: () => ++asked < 0, onApprove: () => approved++);
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(new Key('a'));

        Assert.Equal(0, asked);
        Assert.Equal(1, approved);
    }

    [Fact]
    public void Too_narrow_for_three_panes_History_hides_while_you_comment_and_h_still_shows_it()
    {
        var panes = new ReaderPanes();
        using var dialog = new ReaderDialog(Item, new IssueBody(Long(), History: Recorded), () => { },
            comment: Commenting(), panes: panes, width: ReaderPanes.NarrowestThree - 1);
        Assert.True(dialog.HistoryShown);

        dialog.NewKeyDownEvent(new Key('c'));
        Assert.False(dialog.HistoryShown);
        Assert.True(panes.HistoryShown);

        dialog.NewKeyDownEvent(Key.Tab);
        dialog.NewKeyDownEvent(new Key('h'));
        Assert.True(dialog.HistoryShown);
    }

    [Fact]
    public void Typing_in_the_comment_with_the_ranks_showing_writes_rather_than_ranks()
    {
        using var dialog = Ranking(Long(), Rank.Low, comment: Commenting());

        dialog.NewKeyDownEvent(new Key('c'));
        dialog.NewKeyDownEvent(new Key('h'));
        dialog.NewKeyDownEvent(Key.Enter);
        dialog.NewKeyDownEvent(new Key('m'));

        Assert.Equal("h\nm", dialog.Field.Text.ReplaceLineEndings("\n"));
        Assert.Equal(Rank.Low, dialog.Ranks!.Value);
        Assert.Null(dialog.Chosen);

        dialog.NewKeyDownEvent(Key.Esc);
        Assert.True(dialog.Ranks.HasFocus);
    }

    private static ReaderDialog Declining(
        Func<string, Task<string?>>? decline = null, bool open = false, WaitingItem? item = null, int width = 140)
    {
        var dialog = new ReaderDialog(item ?? Item, new IssueBody(Long(), History: Recorded), () => { },
            comment: Commenting(decline: decline ?? (_ => Task.FromResult<string?>(null)), open: open), width: width);
        dialog.Layout(new Size(width, 20));
        return dialog;
    }

    [Fact]
    public void An_Idea_or_pitch_puts_decline_after_comment()
    {
        using var dialog = Declining();

        Assert.Equal(
            "Tab switch pane · h hide history · Shift+arrows select · c comment · x decline · g on GitHub · Esc close",
            dialog.Hints.Says);
    }

    [Fact]
    public void Without_anything_to_decline_x_is_neither_hinted_nor_handled()
    {
        using var dialog = Replying();

        Assert.DoesNotContain("decline", dialog.Hints.Says);
        dialog.NewKeyDownEvent(new Key('x'));
        Assert.False(dialog.CommentShown);
    }

    [Fact]
    public void x_opens_the_comment_pane_to_decline_with_the_cursor_in_it()
    {
        using var dialog = Declining();

        Assert.True(dialog.NewKeyDownEvent(new Key('x')));

        Assert.True(dialog.CommentShown);
        Assert.True(dialog.Declining);
        Assert.True(dialog.Field.HasFocus);
        Assert.Equal("Decline #180: why?", dialog.CommentTitle);
        Assert.Equal("Tab switch pane · Shift+arrows select · Ctrl+Enter decline · g on GitHub · Esc cancel", dialog.Hints.Says);
    }

    [Fact]
    public void Opened_to_decline_it_starts_in_the_reason()
    {
        using var dialog = Declining(open: true);

        Assert.True(dialog.Declining);
        Assert.True(dialog.Field.HasFocus);
        Assert.Contains("Ctrl+Enter decline", dialog.Hints.Says);
    }

    [Fact]
    public void x_keeps_a_comment_already_started_as_the_start_of_the_reason()
    {
        using var dialog = Declining();
        Open(dialog);
        dialog.Field.Text = "Covered by #46";
        dialog.NewKeyDownEvent(Key.Esc);

        dialog.NewKeyDownEvent(new Key('x'));

        Assert.True(dialog.Declining);
        Assert.Equal("Covered by #46", dialog.Field.Text);
    }

    [Fact]
    public void Lines_quoted_while_declining_go_into_the_reason()
    {
        using var dialog = Declining();
        dialog.NewKeyDownEvent(new Key('x'));
        dialog.NewKeyDownEvent(Key.Tab);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);

        dialog.NewKeyDownEvent(new Key('q'));

        Assert.Equal("> line 1\n\n", dialog.Field.Text);
        Assert.True(dialog.Declining);
    }

    [Fact]
    public void An_empty_reason_declines_nothing_and_says_one_is_needed()
    {
        var declined = 0;
        using var dialog = Declining(_ => Task.FromResult<string?>(++declined < 0 ? "" : null));
        dialog.NewKeyDownEvent(new Key('x'));
        dialog.Field.Text = "  ";

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Equal(0, declined);
        Assert.Equal("A reason is needed to decline #180", dialog.Message.Says);
        Assert.True(dialog.CommentShown);
        Assert.False(dialog.Declined);
    }

    [Fact]
    public void Ctrl_Enter_declines_with_the_reason_rather_than_posting_it()
    {
        var reasons = new List<string>();
        var posted = 0;
        using var dialog = new ReaderDialog(Item, new IssueBody(Long(), History: Recorded), () => { },
            comment: Commenting(_ => Task.FromResult<string?>(++posted < 0 ? "" : null),
                reason => { reasons.Add(reason); return Task.FromResult<string?>(null); }), width: 140);
        dialog.NewKeyDownEvent(new Key('x'));
        dialog.Field.Text = "Already covered by #46.";

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Equal(["Already covered by #46."], reasons);
        Assert.Equal(0, posted);
        Assert.True(dialog.Declined);
    }

    [Fact]
    public void A_decline_that_fails_stays_in_the_pane_saying_why()
    {
        using var dialog = Declining(_ => Task.FromResult<string?>("board.sh: can't close #180 (gh: HTTP 403)"));
        dialog.NewKeyDownEvent(new Key('x'));
        dialog.Field.Text = "No.";

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.False(dialog.Declined);
        Assert.True(dialog.Declining);
        Assert.Equal("No.", dialog.Field.Text);
        Assert.Equal("board.sh: can't close #180 (gh: HTTP 403)", dialog.Message.Says);
    }

    [Fact]
    public void Esc_turns_the_reason_back_into_a_comment_keeping_what_you_wrote()
    {
        var declined = 0;
        using var dialog = Declining(_ => Task.FromResult<string?>(++declined < 0 ? "" : null));
        dialog.NewKeyDownEvent(new Key('x'));
        dialog.Field.Text = "Not now";

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.False(dialog.Declining);
        Assert.True(dialog.CommentShown);
        Assert.Equal("Comment", dialog.CommentTitle);
        Assert.Equal("Not now", dialog.Field.Text);
        Assert.Contains("Ctrl+Enter post", dialog.Hints.Says);
        Assert.Contains("x decline", dialog.Hints.Says);
        Assert.Equal(0, declined);
        Assert.False(dialog.Declined);
    }

    private sealed class Epoch : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }

    private static ReaderComment Commenting(
        Func<string, Task<string?>>? post = null, Func<string, Task<string?>>? decline = null, bool open = false) =>
        new(new Key('c'), "comment", post ?? (_ => Task.FromResult<string?>(null)), new Epoch(),
            decline is null ? null : new ReaderDecline(new Key('x'), decline, open));

    private static void Say(ReaderDialog dialog, string text)
    {
        dialog.NewKeyDownEvent(new Key('c'));
        dialog.Field.Text = text;
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);
    }

    private static string Long() =>
        string.Join('\n', Enumerable.Range(1, 60).Select(line => $"line {line}"));
}
