using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

public class ChecklistTests
{
    private const string Printed = """
        [
          {"line":6,"depth":0,"ticked":false,"text":"Mine, not done","index":1,"how":null,"picture":null},
          {"line":7,"depth":0,"ticked":true,"text":"Mine, done","index":2,"how":null,"picture":null},
          {"line":8,"depth":0,"ticked":true,"text":"Seen working","index":3,"how":"screen","picture":{"alt":"Seen working","url":"https://github.com/x/y/blob/main/shot.png"}},
          {"line":9,"depth":1,"ticked":true,"text":"Proved","index":4,"how":"test","picture":null}
        ]
        """;

    private const string OnePixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAEBgIApD5fRAAAAABJRU5ErkJggg==";

    private static readonly WaitingItem Task =
        new(49, "A task", "In review", "https://github.com/x/49", "a-team", Pr: 122, Base: "main", Branch: "feat/a");

    private static Checklist List => Checklist.Parse(Printed)!;

    private static IssueBody Body(string text = "## Context\n\nWhy.") =>
        new IssueBody(text).With(List).Headed("PR #122 · feat/a → main");

    private static ReaderDialog Reader(
        IssueBody body, List<(int, bool)>? ticks = null, Task<string?>? outcome = null, ReaderTry? tryIt = null,
        Action<Picture, string>? viewImage = null, Pictures? pictures = null, List<int>? unticked = null, WaitingItem? item = null)
    {
        var dialog = new ReaderDialog(item ?? Task, body, () => null,
            comment: new ReaderComment(new Key('c'), "comment", _ => System.Threading.Tasks.Task.FromResult<string?>(null),
                Tick: new ReaderTick((criterion, ticked) =>
                {
                    ticks?.Add((criterion.Index, ticked));
                    return outcome ?? System.Threading.Tasks.Task.FromResult<string?>(null);
                }, Changed: count => unticked?.Add(count))),
            tryIt: tryIt, viewImage: viewImage, pictures: pictures);
        dialog.Layout(new System.Drawing.Size(100, 40));
        return dialog;
    }

    private static int LineOf(ReaderDialog dialog, string text) =>
        dialog.Body.Lines.Select((line, at) => (line, at)).First(each => each.line.Text.Contains(text)).at;

    [Fact]
    public void It_reads_what_the_board_prints()
    {
        Assert.Equal(["Mine, not done", "Mine, done"], List.Left.Select(each => each.Text));
        Assert.Equal(["Seen working", "Proved"], List.Team.Select(each => each.Text));
        Assert.Equal("screen", List.Criteria[2].How);
        Assert.Equal("https://github.com/x/y/blob/main/shot.png", List.Criteria[2].Picture?.Url);
        Assert.Equal(1, List.Unticked);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("""[{"index":1}]""")]
    public void Nothing_to_list_is_no_list(string printed) => Assert.Null(Checklist.Parse(printed));

    [Fact]
    public void It_lists_whats_left_for_you_first_under_a_count_then_the_teams_above_the_issues_text()
    {
        var text = Body().Text.Split('\n');

        Assert.Equal(
        [
            "PR #122 · feat/a → main", "",
            "## Criteria · 1 of 2 left for you ticked · 2 checked by the team", "",
            "### Left for you", "[ ] Mine, not done", "[x] Mine, done", "",
            "### Checked by the team", "[x] Seen working  screen", "  [x] Proved  test", "",
            "---", "",
            "## Context", "", "Why.",
        ], text);
    }

    [Theory]
    [InlineData("""[{"depth":0,"ticked":true,"text":"Mine","index":1}]""", "## Criteria · 1 of 1 left for you ticked")]
    [InlineData("""[{"depth":0,"ticked":true,"text":"Team","index":1,"how":"test"}]""", "## Criteria · 1 checked by the team")]
    public void The_count_leaves_out_a_side_with_none(string printed, string heading) =>
        Assert.Equal(heading, Checklist.Parse(printed)!.Lines[0]);

    [Fact]
    public void The_list_pushes_the_remarks_down_with_it()
    {
        var talked = new IssueBody("Why.").With(new Conversation([new Remark("dev", DateTimeOffset.UnixEpoch, "Said")]));
        var body = talked.With(List).Headed("PR");

        Assert.Equal(talked.Remarks[0].Line + List.Lines.Count + 2, body.Remarks[0].Line);
        Assert.Equal(talked.Lines[talked.Remarks[0].Line], body.Lines[body.Remarks[0].Line]);
    }

    [Fact]
    public void The_reader_starts_on_the_first_box_left_for_you_and_Space_ticks_it()
    {
        List<(int, bool)> ticks = [];
        List<int> unticked = [];
        using var dialog = Reader(Body(), ticks, unticked: unticked);

        Assert.Equal(LineOf(dialog, "Mine, not done"), dialog.Body.Caret?.Line);
        Assert.True(dialog.NewKeyDownEvent(Key.Space));

        Assert.Equal([(1, true)], ticks);
        Assert.Equal([0], unticked);
        Assert.Contains(dialog.Body.Lines, line => line.Text == "[x] Mine, not done");
        Assert.Contains(dialog.Body.Lines, line => line.Text.StartsWith("## Criteria · 2 of 2 left for you ticked", StringComparison.Ordinal));
        Assert.Equal(LineOf(dialog, "Mine, not done"), dialog.Body.Caret?.Line);
    }

    [Fact]
    public void Space_unticks_the_teams_too_and_it_stays_in_its_place()
    {
        List<(int, bool)> ticks = [];
        using var dialog = Reader(Body(), ticks);
        dialog.Body.PutCaret(LineOf(dialog, "Seen working"));

        dialog.NewKeyDownEvent(Key.Space);

        Assert.Equal([(3, false)], ticks);
        Assert.Contains(dialog.Body.Lines, line => line.Text == "[ ] Seen working  screen");
        Assert.True(LineOf(dialog, "### Checked by the team") < LineOf(dialog, "Seen working"));
    }

    [Fact]
    public void Space_off_the_list_does_nothing()
    {
        List<(int, bool)> ticks = [];
        using var dialog = Reader(Body(), ticks);
        dialog.Body.PutCaret(LineOf(dialog, "Why."));

        dialog.NewKeyDownEvent(Key.Space);

        Assert.Empty(ticks);
    }

    [Fact]
    public void History_shows_the_tick_once_its_saved()
    {
        using var dialog = Reader(Body() with { History = new History([]) });

        dialog.NewKeyDownEvent(Key.Space);

        Assert.Contains("you   ticked 4 of 4", dialog.HistoryLog.Lines[0].Text);
    }

    [Fact]
    public void One_the_board_refuses_goes_back_and_says_why()
    {
        using var dialog = Reader(Body() with { History = new History([]) },
            outcome: System.Threading.Tasks.Task.FromResult<string?>("#49's criteria have changed: open it again"));

        dialog.NewKeyDownEvent(Key.Space);

        Assert.Contains(dialog.Body.Lines, line => line.Text == "[ ] Mine, not done");
        Assert.Equal("#49's criteria have changed: open it again", dialog.Message.Text);
        Assert.DoesNotContain("ticked", dialog.HistoryLog.Lines[0].Text);
    }

    [Fact]
    public async Task Enter_on_a_screen_criterion_shows_its_picture_and_on_any_other_does_not()
    {
        List<string> viewed = [];
        var pictures = new Pictures(() => true, () => new System.Drawing.Size(10, 20), (_, file) =>
        {
            File.WriteAllBytes(file, Convert.FromBase64String(OnePixel));
            return System.Threading.Tasks.Task.FromResult(true);
        });
        pictures.For(List.Criteria[2].Picture!.Url);
        while (pictures.Reading)
            await System.Threading.Tasks.Task.Delay(5, TestContext.Current.CancellationToken);
        using var dialog = Reader(Body(), viewImage: (_, alt) => viewed.Add(alt), pictures: pictures);

        dialog.Body.PutCaret(LineOf(dialog, "Proved"));
        dialog.NewKeyDownEvent(Key.Enter);
        Assert.Empty(viewed);

        dialog.Body.PutCaret(LineOf(dialog, "Seen working"));
        Assert.True(dialog.NewKeyDownEvent(Key.Enter));
        Assert.Equal(["Seen working"], viewed);
    }

    [Fact]
    public void Back_from_a_try_it_opens_on_the_list_not_where_you_were()
    {
        var body = new IssueBody(string.Join('\n', Enumerable.Repeat("Why.", 200))).With(List).Headed("PR #122");
        using var dialog = Reader(body, tryIt: new ReaderTry(new Key('t'), (_, _) => { }, Top: 150));

        Assert.Equal(LineOf(dialog, "## Criteria"), dialog.Body.TopLine);
        Assert.Equal(LineOf(dialog, "Mine, not done"), dialog.Body.Caret?.Line);
    }

    [Fact]
    public void Back_from_a_try_with_no_list_it_opens_where_you_were()
    {
        var body = new IssueBody(string.Join('\n', Enumerable.Repeat("Why.", 200)));
        using var dialog = Reader(body, tryIt: new ReaderTry(new Key('t'), (_, _) => { }, Top: 150));

        Assert.Equal(150, dialog.Body.Top);
    }

    [Fact]
    public void A_task_with_no_list_has_nothing_for_Space_to_tick()
    {
        List<(int, bool)> ticks = [];
        using var dialog = Reader(new IssueBody("## Context\n\n- [ ] a list"), ticks);

        dialog.NewKeyDownEvent(Key.Space);

        Assert.Empty(ticks);
        Assert.DoesNotContain(dialog.Body.Lines, line => line.Text.StartsWith("## Criteria", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("In review", false, 122, true)]
    [InlineData("In progress", false, 122, false)]
    [InlineData("In review", true, 0, false)]
    [InlineData("In review", false, 0, false)]
    [InlineData("In review", false, 49, false)]
    public void Only_a_task_with_its_PR_In_review_lists_criteria(string status, bool pitch, int pr, bool lists) =>
        Assert.Equal(lists, new WaitingItem(49, "A task", status, "", "a-team", Pitch: pitch, Pr: pr).HasCriteria);

    [Fact]
    public void Accepting_says_how_many_arent_ticked()
    {
        Assert.StartsWith("3 criteria not ticked.\n\nMerges PR #122", AcceptDialog.Says(Task with { Unticked = 3 }), StringComparison.Ordinal);
        Assert.StartsWith("1 criterion not ticked.\n\nMerges PR #122", AcceptDialog.Says(Task with { Unticked = 1 }), StringComparison.Ordinal);
        Assert.StartsWith("Merges PR #122", AcceptDialog.Says(Task), StringComparison.Ordinal);
    }

    [Fact]
    public void Waiting_carries_the_count() =>
        Assert.Equal(3, WaitingItem.Parse("""[{"number":1,"status":"In review","unticked":3}]""")[0].Unticked);

    [Fact]
    public void A_run_of_ticks_reads_as_the_newest()
    {
        var at = DateTimeOffset.UnixEpoch;
        var history = new History([new HistoryEvent(at, "you", "ticked 1 of 3"), new HistoryEvent(at, "dev", "commented")])
            .With(new HistoryEvent(at, "you", "ticked 2 of 3"));

        Assert.Equal(["ticked 2 of 3", "commented"], history.With(new HistoryEvent(at, "you", "commented"))
            .Events.Skip(1).Select(each => each.What));
    }

    [Fact]
    public void The_box_and_the_how_are_coloured_without_changing_a_character()
    {
        var spans = Markdown.Spans(new LogRow("[x] Seen working  screen", LogLineKind.Prose)).ToList();

        Assert.Contains(new MarkdownSpan(0, 3, LogLineKind.ListMarker), spans);
        Assert.Contains(new MarkdownSpan(18, 6, LogLineKind.InlineCode), spans);
    }
}
