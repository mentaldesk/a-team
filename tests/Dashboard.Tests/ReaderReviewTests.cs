using System.Drawing;
using MentalDesk.Tui.Layout;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

/// <summary>The reader's Review tab: the Reviewer's comment on a reviewed task's PR, beside History.</summary>
public class ReaderReviewTests
{
    private const string Review = "## Needs changing\n\nNothing needs changing.\n\n## Worth considering\n\n- Rename it.\n- Match the branch too.";

    private static readonly DateTimeOffset At = new(2026, 11, 15, 9, 12, 0, TimeSpan.Zero);

    private static readonly WaitingItem Task =
        new(829, "Ctrl+F in Overseer filters every lane", "In review", "https://github.com/x/829", "a-team", "you",
            Pr: 830, Reviewed: true, Consider: 2);

    private static IssueBody Body(params Remark[] remarks) =>
        new IssueBody("## Context\n\nFinding a card means reading every lane.", History: new History([]))
            .With(new Conversation(remarks));

    private static IssueBody Reviewed() => Body(
        new Remark("dev", At, "## Worth considering\n\n- The Dev's own.", 830),
        new Remark("reviewer", At, Review, 830),
        new Remark("you", At, "Looks good.", 830));

    private static ReaderDialog Open(
        WaitingItem item, IssueBody body, ReaderPanes? panes = null, ReaderComment? comment = null, int width = 140)
    {
        var dialog = new ReaderDialog(item, body, () => null, comment: comment, panes: panes, width: width);
        dialog.Layout(new Size(width, 30));
        return dialog;
    }

    private static ReaderComment Commenting(ReaderCommand? fix = null, ReaderReview? review = null, ReaderTabs? tabs = null) =>
        new(new Key('c'), "comment", _ => System.Threading.Tasks.Task.FromResult<string?>(null), Fix: fix, Review: review, Tabs: tabs);

    private static string Text(LogView log) => string.Join('\n', log.Lines.Select(line => line.Text));

    [Fact]
    public void A_reviewed_task_shows_History_and_Review_tabs_with_the_review_as_written()
    {
        using var dialog = Open(Task, Reviewed());

        Assert.NotNull(dialog.SideTabs);
        Assert.Equal(["History", "Review · 2"], dialog.SideTabs!.SubViews.Select(tab => tab.Title).Order());
        var review = Text(dialog.ReviewLog);
        Assert.Contains("reviewer", review);
        Assert.True(review.IndexOf("Needs changing", StringComparison.Ordinal) < review.IndexOf("Worth considering", StringComparison.Ordinal));
        Assert.DoesNotContain("The Dev's own.", review);
        Assert.DoesNotContain("Looks good.", review);
        Assert.Contains("v Review", dialog.ButtonLabels);
    }

    [Fact]
    public void A_card_the_Reviewer_has_not_reviewed_shows_History_alone()
    {
        using var unreviewed = Open(Task with { Reviewed = false, Consider = 0 }, Body(new Remark("dev", At, "Marked ready.", 830)));
        using var pitch = Open(Task with { Pr = 0, Reviewed = false, Consider = 0, Pitch = true, Status = "Pitched" }, Body());

        foreach (var dialog in new[] { unreviewed, pitch })
        {
            Assert.Null(dialog.SideTabs);
            Assert.True(dialog.HistoryShown);
            Assert.DoesNotContain("v Review", dialog.ButtonLabels);
            Assert.False(dialog.NewKeyDownEvent(new Key('v')));
        }
    }

    [Fact]
    public void Only_the_Reviewer_s_comment_on_the_task_s_own_PR_is_the_review()
    {
        var body = Body(
            new Remark("reviewer", At, "On another PR.", 12),
            new Remark("dev", At, "The Dev's.", 830),
            new Remark("you", At, "Yours.", 830),
            new Remark("reviewer", At, "This one.", 830));

        Assert.Equal("This one.", body.Review(830)?.Body);
        Assert.Null(body.Review(831));
        Assert.Null(body.Review(0));
    }

    [Theory]
    [InlineData("- Rename it.\n- Split it.", "Review · 2")]
    [InlineData("- [x] Rename it.\n- [ ] Split it.", "Review · 1")]
    [InlineData("- [x] Rename it.\n- [X] Split it.", "Review")]
    public void The_tab_counts_the_points_the_Dev_has_not_ticked(string points, string title)
    {
        using var dialog = Open(Task, Body(new Remark("reviewer", At, $"## Worth considering\n\n{points}", 830)));

        Assert.Equal(title, dialog.ReviewTabTitle);
    }

    [Fact]
    public void Each_point_shows_a_box_ticked_once_the_Dev_has_fixed_it()
    {
        const string review = "## Needs changing\n- [x] a.sh:1 off by one.\n\n## Worth considering\n- [ ] Rename it.\n- Split it.\n\n## Notes\n- Not a point.";
        using var dialog = Open(Task, Body(new Remark("reviewer", At, review, 830)));

        var lines = Text(dialog.ReviewLog).Split('\n');
        Assert.Contains("[x] a.sh:1 off by one.", lines);
        Assert.Contains("[ ] Rename it.", lines);
        Assert.Contains("[ ] Split it.", lines);
        Assert.Contains("- Not a point.", lines);
        Assert.Equal("Review · 2", dialog.ReviewTabTitle);
    }

    [Fact]
    public void v_brings_Review_forward_with_focus_from_the_body_and_from_History()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var dialog = Open(Task, Reviewed());
        app.Begin(dialog);
        dialog.Layout(new Size(140, 30));
        Assert.True(dialog.Body.HasFocus);
        Assert.False(dialog.ReviewInFront);

        Assert.True(dialog.NewKeyDownEvent(new Key('v')));
        Assert.True(dialog.ReviewInFront);
        Assert.True(dialog.ReviewLog.HasFocus);

        dialog.SideTabs!.Value = dialog.SideTabs.SubViews.Single(tab => tab.Title == "History");
        Assert.True(dialog.HistoryLog.HasFocus);
        Assert.True(dialog.NewKeyDownEvent(new Key('v')));
        Assert.True(dialog.ReviewInFront);
        Assert.True(dialog.ReviewLog.HasFocus);
    }

    [Fact]
    public void v_shows_the_pane_h_had_hidden()
    {
        var panes = new ReaderPanes();
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var dialog = Open(Task, Reviewed(), panes);
        app.Begin(dialog);

        Assert.True(dialog.NewKeyDownEvent(new Key('h')));
        Assert.False(dialog.HistoryShown);
        Assert.True(dialog.NewKeyDownEvent(new Key('v')));

        Assert.True(dialog.HistoryShown);
        Assert.True(panes.HistoryShown);
        Assert.True(dialog.ReviewLog.HasFocus);
    }

    private static ReaderDialog Begun(IApplication app, WaitingItem item, IssueBody body, ReaderPanes? panes = null,
        ReaderComment? comment = null, Func<Key, bool>? window = null)
    {
        var dialog = new ReaderDialog(item, body, () => null, comment: comment, panes: panes, width: 140, window: window);
        app.Begin(dialog);
        dialog.Layout(new Size(140, 30));
        return dialog;
    }

    public static TheoryData<bool, bool, bool> FromEachTab => new()
    {
        { false, true, true }, { false, false, true }, { true, true, false }, { true, false, false },
        { false, true, false }, { false, false, false }, { true, true, true }, { true, false, true },
    };

    [Theory]
    [MemberData(nameof(FromEachTab))]
    public void Ctrl_PgDn_and_Ctrl_PgUp_bring_the_other_tab_forward_shown_and_focused(bool reviewFirst, bool hidden, bool down)
    {
        var panes = new ReaderPanes { ReviewInFront = reviewFirst, HistoryShown = !hidden };
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var dialog = Begun(app, Task, Reviewed(), panes);
        Assert.Equal(!hidden, dialog.HistoryShown);

        Assert.True(dialog.NewKeyDownEvent(down ? Key.PageDown.WithCtrl : Key.PageUp.WithCtrl));

        Assert.True(dialog.HistoryShown);
        Assert.Equal(!reviewFirst, dialog.ReviewInFront);
        Assert.True((reviewFirst ? dialog.HistoryLog : dialog.ReviewLog).HasFocus);
        Assert.Equal(!reviewFirst, panes.ReviewInFront);
    }

    [Fact]
    public void The_arrows_move_the_caret_in_the_tab_a_key_brought_forward()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var dialog = Begun(app, Task, Reviewed());

        dialog.NewKeyDownEvent(Key.PageDown.WithCtrl);
        dialog.NewKeyDownEvent(Key.CursorDown);
        dialog.NewKeyDownEvent(Key.CursorDown);

        Assert.True(dialog.ReviewLog.HasFocus);
        Assert.True(dialog.ReviewLog.Caret?.Line > 0);
    }

    [Fact]
    public void Without_a_review_the_tab_keys_say_there_s_only_History_and_change_nothing()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        foreach (var hidden in new[] { false, true })
        {
            using var dialog = Begun(app, Task with { Reviewed = false }, Body());
            if (hidden)
                dialog.NewKeyDownEvent(new Key('h'));

            foreach (var key in new[] { Key.PageDown.WithCtrl, Key.PageUp.WithCtrl })
            {
                Assert.True(dialog.NewKeyDownEvent(key));
                Assert.Equal(!hidden, dialog.HistoryShown);
                Assert.True(dialog.Body.HasFocus);
                Assert.Contains("only History", dialog.Message.Text);
            }
        }
    }

    [Fact]
    public void h_brings_History_forward_from_Review_then_hides_and_shows_the_tab_last_in_front()
    {
        var panes = new ReaderPanes();
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var dialog = Begun(app, Task, Reviewed(), panes);
        dialog.NewKeyDownEvent(new Key('v'));

        Assert.True(dialog.NewKeyDownEvent(new Key('h')));
        Assert.True(dialog.HistoryShown);
        Assert.False(dialog.ReviewInFront);
        Assert.True(dialog.HistoryLog.HasFocus);

        Assert.True(dialog.NewKeyDownEvent(new Key('h')));
        Assert.False(dialog.HistoryShown);
        Assert.True(dialog.NewKeyDownEvent(new Key('h')));
        Assert.True(dialog.HistoryShown);
        Assert.False(dialog.ReviewInFront);
    }

    [Fact]
    public void h_shows_the_hidden_pane_with_Review_where_it_was_last_in_front()
    {
        var panes = new ReaderPanes { HistoryShown = false, ReviewInFront = true };
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var dialog = Begun(app, Task, Reviewed(), panes);
        Assert.False(dialog.HistoryShown);

        Assert.True(dialog.NewKeyDownEvent(new Key('h')));

        Assert.True(dialog.HistoryShown);
        Assert.True(dialog.ReviewInFront);
    }

    [Fact]
    public void Without_a_review_h_hides_and_shows_History_as_before()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var dialog = Begun(app, Task with { Reviewed = false }, Body());

        Assert.True(dialog.NewKeyDownEvent(new Key('h')));
        Assert.False(dialog.HistoryShown);
        Assert.True(dialog.NewKeyDownEvent(new Key('h')));
        Assert.True(dialog.HistoryShown);
    }

    [Fact]
    public void The_reader_s_tab_keys_don_t_reach_the_window()
    {
        var reached = new List<Key>();
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        foreach (var reviewed in new[] { true, false })
        {
            using var dialog = Begun(app, Task with { Reviewed = reviewed }, reviewed ? Reviewed() : Body(), window: key =>
            {
                reached.Add(key);
                return true;
            });
            Assert.True(dialog.NewKeyDownEvent(Key.PageDown.WithCtrl));
            Assert.True(dialog.NewKeyDownEvent(Key.PageUp.WithCtrl));
        }

        Assert.Empty(reached);
    }

    [Fact]
    public void F1_lists_the_tab_keys_and_Commands_can_run_them()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        var tabs = new ReaderTabs(Key.PageDown.WithCtrl, Key.PageUp.WithCtrl);
        using var dialog = Begun(app, Task, Reviewed(), comment: Commenting(tabs: tabs));

        var rows = DialogTests.Rows(DialogTests.F1(dialog));
        Assert.Contains(rows, row => row is { Keys: "Ctrl+PgDn", Label: "Next tab" });
        Assert.Contains(rows, row => row is { Keys: "Ctrl+PgUp", Label: "Previous tab" });

        tabs.Turn!(+1);
        Assert.True(dialog.ReviewInFront);
        tabs.Turn!(-1);
        Assert.False(dialog.ReviewInFront);
    }

    [Fact]
    public void Tab_moves_between_the_body_and_the_tab_in_front()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var dialog = Open(Task, Reviewed());
        app.Begin(dialog);
        dialog.NewKeyDownEvent(new Key('v'));

        Assert.True(dialog.NewKeyDownEvent(Key.Tab));
        Assert.True(dialog.Body.HasFocus);
        Assert.True(dialog.NewKeyDownEvent(Key.Tab));
        Assert.True(dialog.ReviewLog.HasFocus);
    }

    [Fact]
    public void The_tab_last_in_front_is_the_one_the_next_reviewed_card_opens_with()
    {
        var panes = new ReaderPanes();
        using (var first = Open(Task, Reviewed(), panes))
            first.NewKeyDownEvent(new Key('v'));

        using (var unreviewed = Open(Task with { Reviewed = false }, Body(), panes))
            Assert.Null(unreviewed.SideTabs);

        using (var next = Open(Task with { Number = 840, Pr = 830 }, Reviewed(), panes))
        {
            Assert.True(next.ReviewInFront);
            next.SideTabs!.Value = next.SideTabs.SubViews.Single(tab => tab.Title == "History");
        }

        using var last = Open(Task, Reviewed(), panes);
        Assert.False(last.ReviewInFront);
    }

    [Fact]
    public void The_window_s_key_for_Show_review_is_the_one_the_reader_answers_and_Commands_can_run_it()
    {
        var review = new ReaderReview(new Key('w'));
        using var dialog = Open(Task, Reviewed(), comment: Commenting(review: review));

        Assert.False(dialog.NewKeyDownEvent(new Key('v')));
        Assert.Contains("w Review", dialog.ButtonLabels);
        Assert.True(dialog.NewKeyDownEvent(new Key('w')));
        Assert.True(dialog.ReviewInFront);

        dialog.SideTabs!.Value = dialog.SideTabs.SubViews.Single(tab => tab.Title == "History");
        review.Show!();
        Assert.True(dialog.ReviewInFront);
    }

    [Fact]
    public void Commands_can_t_show_a_review_on_a_card_without_one()
    {
        var review = new ReaderReview(new Key('v'));
        using var dialog = Open(Task with { Reviewed = false }, Body(), comment: Commenting(review: review));

        Assert.Null(review.Show);
    }

    [Fact]
    public void F1_lists_v_and_f_asks_the_Dev_to_fix_from_Review()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        var asked = 0;
        using var dialog = Open(Task, Reviewed(), comment: Commenting(new ReaderCommand(new Key('F'), "ask the Dev to fix", () =>
        {
            asked++;
            return false;
        })));
        app.Begin(dialog);
        dialog.NewKeyDownEvent(new Key('v'));

        Assert.Contains(DialogTests.Rows(DialogTests.F1(dialog)), row => row is { Keys: "v", Label: "Show review" });
        Assert.True(dialog.NewKeyDownEvent(new Key('f')));
        Assert.Equal(1, asked);
    }

    [Fact]
    public void A_selection_in_Review_copies_and_quotes_into_the_comment()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        var clipboard = new InstalledTerminal.Remembering();
        var dialog = new ReaderDialog(Task, Reviewed(), () => null, comment: Commenting(), width: 140, clipboard: clipboard);
        using var _ = dialog;
        app.Begin(dialog);
        dialog.Layout(new Size(140, 30));
        dialog.NewKeyDownEvent(new Key('v'));
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        Assert.True(dialog.ReviewLog.Marked > 0);

        Assert.True(dialog.NewKeyDownEvent(Key.C.WithCtrl));
        Assert.Contains("reviewer", clipboard.Text);
        Assert.True(dialog.NewKeyDownEvent(new Key('q')));
        Assert.True(dialog.CommentShown);
        Assert.StartsWith("> ", dialog.Field.Text);
    }

    [Fact]
    public void The_pane_keeps_its_width_when_switching_tabs()
    {
        using var dialog = Open(Task, Reviewed());
        dialog.NewKeyDownEvent(new Key('v'));
        Assert.True(dialog.Sides.CanResize);
        dialog.Sides.Widen();
        dialog.Layout(new Size(140, 30));
        var widened = dialog.SideTabs!.SuperView!.Frame.Width;
        Assert.Equal(ReaderPanes.HistoryWidth + SidePanes.Step, widened);

        dialog.SideTabs.Value = dialog.SideTabs.SubViews.Single(tab => tab.Title == "History");
        dialog.Layout(new Size(140, 30));

        Assert.Equal(widened, dialog.SideTabs.SuperView!.Frame.Width);
    }

    [Theory]
    [InlineData(124)]
    [InlineData(136)]
    public void The_pane_s_right_edge_is_drawn_whichever_tab_is_in_front(int columns)
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(columns, 41);
        using var dialog = new ReaderDialog(Task, Reviewed(), () => null, width: columns);
        app.Begin(dialog);

        string Edge()
        {
            app.LayoutAndDraw(true);
            var pane = dialog.SideTabs!.SuperView!.FrameToScreen();
            var below = pane.Y + 3;
            return string.Concat(Enumerable.Range(below, pane.Bottom - below - 1).Select(row => app.Driver.Contents![row, pane.Right - 1].Grapheme));
        }

        var history = Edge();
        dialog.NewKeyDownEvent(new Key('v'));

        Assert.Matches("^│+$", history);
        Assert.Equal(history, Edge());
    }
}
