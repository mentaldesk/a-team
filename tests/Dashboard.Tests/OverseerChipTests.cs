using System.Drawing;
using MentalDesk.Tui.Links;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

/// <summary>Going to a chip's card in Work, and reading or deciding it from Overseer.</summary>
[Collection("StaticConfiguration")]
public class OverseerChipTests : IDisposable
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

    private sealed record Offered(
        WaitingItem Item, IssueBody Body, Func<bool>? Approve, ReaderCommand? Accept, ReaderComment? Comment, ReaderTry? Try, ReaderEdit? Edit, ReaderIdea? Idea, Rank? Rank)
    {
        /// <summary>Which of the reader's buttons were offered, as a comparison between two readers can see them.</summary>
        internal string[] Buttons =>
        [
            .. Approve is null ? [] : new[] { "Approve" }, .. Accept is null ? [] : new[] { $"Accept {Accept.Key}" },
            .. Comment is null ? [] : new[] { $"Comment {Comment.Key}" }, .. Comment?.Decline is null ? [] : new[] { $"Decline {Comment.Decline.Key}" },
            .. Comment?.Shelve is null ? [] : new[] { $"Not now {Comment.Shelve.Key}" }, .. Comment?.Release is null ? [] : new[] { "Release" },
            .. Try is null ? [] : new[] { $"Try {Try.Key}" }, .. Edit is null ? [] : new[] { "Edit" }, .. Idea is null ? [] : new[] { "Idea" },
            .. Rank is null ? [] : new[] { "Ranks" },
        ];
    }

    private const string Pitched = """{"number": 10, "title": "Ten", "url": "https://github.com/o/r/issues/10", "status": "Pitched", "team": "team0", "pitch": true, "priority": "High"}""";
    private const string Reviewing = """{"number": 11, "title": "Eleven", "url": "https://github.com/o/r/issues/11", "status": "In review", "team": "team0", "pr": 80, "prUrl": "https://github.com/o/r/pull/80", "priority": "High"}""";
    private const string Triage = """{"number": 12, "title": "Twelve", "url": "https://github.com/o/r/issues/12", "status": "Idea", "team": "team0"}""";
    private const string Held = """{"number": 13, "title": "Thirteen", "url": "https://github.com/o/r/issues/13", "status": "Ready", "team": "team0", "question": "Which?", "heldBy": "dev"}""";
    private const string WithReviewer = """{"number": 14, "title": "Fourteen", "url": "https://github.com/o/r/issues/14", "status": "In review", "team": "team0", "pr": 81, "unready": "with the Reviewer", "turn": "reviewer"}""";
    private const string Theirs = """{"number": 15, "title": "Fifteen", "url": "https://github.com/o/r/issues/15", "status": "In review", "team": "team0", "pr": 82, "turn": "dev"}""";

    private static string Chip(int number, string status, string kind = "task", string team = "team0") =>
        $$"""{"number": {{number}}, "title": "Chip {{number}}", "url": "https://github.com/o/r/issues/{{number}}", "status": "{{status}}", "since": "2026-10-01T00:00:00Z", "team": "{{team}}", "kind": "{{kind}}"}""";

    private static readonly string Board = "[" + string.Join(",",
        Chip(10, "Pitched", "pitch"), Chip(11, "In review"), Chip(12, "Idea", "yours"), Chip(13, "Ready"), Chip(14, "In review"), Chip(15, "In review"), Chip(16, "In progress"), Chip(17, "Building", "pitch")) + "]";

    private DashboardWindow Open(
        out List<Offered> shown, string waiting = "[" + Pitched + "," + Reviewing + "," + Triage + "," + Held + "," + WithReviewer + "," + Theirs + "]",
        Func<string, Task<Reading>>? readBoard = null, Func<string[], Task<string?>>? run = null, Func<WaitingItem, bool>? confirmAccept = null,
        int width = 160, int height = 30, Action<Handover>? handOver = null, Handover? resume = null, Func<string, LinkOutcome>? openUrl = null,
        bool onlyMine = false, Action<Offered>? reading = null, Func<WaitingItem, Task<Reading>>? readConversation = null,
        Func<WaitingItem, Task<Reading>>? readHistory = null, bool newIdea = false)
    {
        Directory.CreateDirectory(_root);
        var settings = new DashboardSettings(Config);
        settings.WriteOnlyMine(onlyMine);
        var offered = new List<Offered>();
        shown = offered;
        var window = new DashboardWindow(
            [("team0", "lead"), ("team0", "dev"), ("team1", "lead"), ("team1", "dev")],
            _root,
            settings,
            new TeamConfigs(Config),
            run ?? (_ => Task.FromResult<string?>(null)),
            team => Task.FromResult(new Reading(team == "team0" ? waiting : "[]", null)),
            _ => Task.FromResult(new Reading("{\"body\": \"The card's own words\"}", null)),
            openUrl ?? Opening.Opened,
            (item, body, _, approve, accept, comment, tryIt, edit, _, idea, _, rank) =>
            {
                var shownNow = new Offered(item, body, approve, accept, comment, tryIt, edit, idea, rank);
                offered.Add(shownNow);
                reading?.Invoke(shownNow);
                return null;
            },
            resume?.Area ?? Area.Overseer,
            IconStyle.Unicode,
            new DashboardOptions
            {
                ReadBoard = readBoard ?? (team => Task.FromResult(new Reading(team == "team0" ? Board : "[]", null))),
                ConfirmAccept = confirmAccept,
                ReadConversation = readConversation,
                ReadHistory = readHistory,
                HandOver = handOver,
                Resume = resume,
                NewIdea = newIdea ? (_, _, _, _) => null : null,
            });
        window.Frame = new Rectangle(0, 0, width, height);
        window.Layout(new Size(width, height));
        window.Refresh();
        return window;
    }

    private static void Select(DashboardWindow window, int number, string team = "team0")
    {
        Assert.True(window.Overseer.Select(team, number));
        window.Refresh();
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, true)]
    [InlineData(12, true)]
    [InlineData(13, true)]
    [InlineData(14, false)]
    [InlineData(16, false)]
    [InlineData(17, false)]
    public void W_works_on_the_cards_a_column_of_Work_shows(int number, bool enabled)
    {
        using var window = Open(out _);

        Select(window, number);

        Assert.Equal(enabled, window.Commands.IsEnabled("overseer.work"));
    }

    [Fact]
    public void W_opens_Work_on_the_cards_team_with_the_card_selected()
    {
        const string other = """[{"number": 30, "title": "Thirty", "url": "u", "status": "In review", "team": "team0", "pr": 90}, {"number": 31, "title": "Thirty-one", "url": "u", "status": "In review", "team": "team0", "pr": 91}]""";
        using var window = Open(out _, waiting: other, readBoard: team => Task.FromResult(new Reading(team == "team0" ? "[" + Chip(30, "In review") + "," + Chip(31, "In review") + "]" : "[]", null)));
        Select(window, 31);

        Assert.True(window.NewKeyDownEvent(new Key('w')));

        Assert.Equal(Area.Work, window.CurrentArea);
        Assert.Equal("team0", window.Work.Current!.Team);
        Assert.Equal(31, window.Work.Selected!.Number);
    }

    [Fact]
    public void W_goes_to_a_card_on_a_team_other_than_the_one_in_front_and_shows_a_card_scrolled_out_of_view()
    {
        var rows = string.Join(",", Enumerable.Range(100, 40).Select(number =>
            $$"""{"number": {{number}}, "title": "Row {{number}}", "url": "u", "status": "In review", "team": "team1", "pr": {{number + 1000}}}"""));
        var chips = "[" + Chip(139, "In review", team: "team1") + "]";
        Directory.CreateDirectory(_root);
        using var window = new DashboardWindow(
            [("team0", "lead"), ("team1", "lead")], _root, new DashboardSettings(Config), new TeamConfigs(Config),
            _ => Task.FromResult<string?>(null),
            team => Task.FromResult(new Reading(team == "team1" ? "[" + rows + "]" : "[]", null)),
            _ => Task.FromResult(new Reading("{\"body\": \"x\"}", null)), Opening.Opened,
            (_, _, _, _, _, _, _, _, _, _, _, _) => null, Area.Overseer, IconStyle.Unicode,
            new DashboardOptions { ReadBoard = team => Task.FromResult(new Reading(team == "team1" ? chips : "[]", null)) });
        window.Frame = new Rectangle(0, 0, 160, 20);
        window.Layout(new Size(160, 20));
        window.Refresh();
        Select(window, 139, "team1");

        window.Commands.Execute("overseer.work");
        window.Layout(new Size(160, 20));

        Assert.Equal("team1", window.Work.Current!.Team);
        Assert.Equal(139, window.Work.Selected!.Number);
        var lane = window.Work.Current;
        var column = lane.Columns.Single(each => each.Count > 0);
        Assert.InRange(column.Row, lane.Scrolled, lane.Scrolled + lane.Viewport.Height);
        Assert.True(lane.Scrolled > 0);
    }

    [Fact]
    public void On_a_card_Work_doesnt_show_w_is_off_the_panel_dimmed_in_the_menu_and_says_why_from_Commands()
    {
        using var window = Open(out _);
        Select(window, 16);

        Assert.DoesNotContain("Work", window.Card.Hint, StringComparison.Ordinal);
        Assert.True(window.MenuItems.Single(item => item.Id == "overseer.work").Item.Dimmed);
        Assert.Contains(window.InCommands, command => command.Id == "overseer.work");

        window.Commands.Execute("overseer.work");

        Assert.Equal("#16 isn't in Work: nothing's waiting on you for it", window.Says());
        Assert.Equal(Area.Overseer, window.CurrentArea);
    }

    [Fact]
    public void A_card_only_mine_hides_is_one_Work_doesnt_show_but_keeps_its_gates_in_the_reader()
    {
        using var window = Open(out var shown, onlyMine: true);
        Select(window, 15);

        Assert.False(window.Commands.IsEnabled("overseer.work"));

        window.NewKeyDownEvent(new Key('o'));
        window.Refresh();

        Assert.NotNull(Assert.Single(shown).Accept);
    }

    [Theory]
    [InlineData(16, "In progress")]
    [InlineData(17, "Building")]
    [InlineData(14, "In review")]
    public void O_on_a_card_that_isnt_waiting_offers_only_comment_and_github(int number, string status)
    {
        using var window = Open(out var shown);
        Select(window, number);

        Assert.True(window.NewKeyDownEvent(new Key('o')));
        window.Refresh();

        var offered = Assert.Single(shown);
        Assert.Equal(number, offered.Item.Number);
        Assert.Equal(status, offered.Item.Status);
        Assert.Equal(["Comment c"], offered.Buttons);
    }

    [Fact]
    public void O_opens_the_reader_with_the_cards_body_history_and_comments()
    {
        var at = new DateTimeOffset(2026, 9, 29, 4, 31, 0, TimeSpan.Zero);
        using var window = Open(out var shown,
            readConversation: _ => Task.FromResult(new Reading($$"""[{"who": "dev", "at": "{{at:O}}", "body": "Done."}]""", null)),
            readHistory: _ => Task.FromResult(new Reading($$"""{"events": [{"at": "{{at:O}}", "who": "dev", "what": "Ready → In progress"}]}""", null)));
        Select(window, 16);

        window.NewKeyDownEvent(new Key('o'));
        window.Refresh();

        var body = Assert.Single(shown).Body;
        Assert.Contains("The card's own words", body.Text, StringComparison.Ordinal);
        Assert.Contains(body.Remarks, said => said.Remark.Body == "Done.");
        Assert.Contains(body.History!.Events, each => each.What == "Ready → In progress");
    }

    [Fact]
    public async Task A_comment_from_a_chip_posts_on_the_issue()
    {
        var calls = new List<string[]>();
        using var window = Open(out var shown, run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        Select(window, 16);
        window.Commands.Execute("overseer.read");
        window.Refresh();

        Assert.Null(await Assert.Single(shown).Comment!.Post("A note"));

        Assert.Equal(["board", "team0", "comment", "you", "16"], Assert.Single(calls)[..5]);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void The_readers_buttons_from_a_chip_are_Works_for_the_same_card(int number)
    {
        using var overseer = Open(out var fromChip);
        Select(overseer, number);
        overseer.Commands.Execute("overseer.read");
        overseer.Refresh();

        using var work = Open(out var fromWork);
        Select(work, number);
        work.Commands.Execute("overseer.work");
        work.Commands.Execute("work.read");
        work.Refresh();

        Assert.Equal(Assert.Single(fromWork).Buttons, Assert.Single(fromChip).Buttons);
        Assert.NotEqual(["Comment c"], fromChip[0].Buttons);
    }

    [Fact]
    public void A_gate_used_from_a_chip_moves_the_chip_once_the_board_has_taken_it()
    {
        var accepted = false;
        var reads = 0;
        using var window = Open(out var shown,
            readBoard: team =>
            {
                if (team == "team0")
                    reads++;
                return Task.FromResult(new Reading(team != "team0" ? "[]" : accepted ? "[" + Chip(11, "Ready") + "]" : Board, null));
            },
            run: arguments =>
            {
                accepted = arguments[2] == "accept";
                return Task.FromResult<string?>(null);
            },
            confirmAccept: _ => true, reading: reader => Assert.True(reader.Accept!.Run()));
        Select(window, 11);
        var before = reads;
        window.Commands.Execute("overseer.read");
        window.Refresh();
        window.Refresh();
        window.Refresh();

        Assert.Single(shown);
        Assert.True(reads > before);
        Assert.Equal("Ready", window.Overseer.Board.Cards.Single(card => card.Number == 11).Status);
    }

    [Fact]
    public void A_gate_moves_the_chip_even_when_a_read_from_before_it_is_still_running()
    {
        var accepted = false;
        TaskCompletionSource<Reading>? running = null;
        using var window = Open(out var shown,
            readBoard: team =>
            {
                if (team != "team0")
                    return Task.FromResult(new Reading("[]", null));
                if (accepted)
                    return Task.FromResult(new Reading("[" + Chip(11, "Ready") + "]", null));
                return running?.Task ?? Task.FromResult(new Reading(Board, null));
            },
            run: arguments =>
            {
                accepted = arguments[2] == "accept";
                return Task.FromResult<string?>(null);
            },
            confirmAccept: _ => true, reading: reader => Assert.True(reader.Accept!.Run()));
        Select(window, 11);
        running = new TaskCompletionSource<Reading>();
        window.Commands.Execute("overseer.refresh");

        window.Commands.Execute("overseer.read");
        window.Refresh();
        window.Refresh();
        running.SetResult(new Reading(Board, null));
        window.Refresh();
        window.Refresh();

        Assert.Single(shown);
        Assert.Equal("Ready", window.Overseer.Board.Cards.Single(card => card.Number == 11).Status);
    }

    [Fact]
    public void The_comment_only_reader_offers_no_new_idea_and_the_gated_reader_does()
    {
        DashboardWindow? opened = null;
        var offered = new List<(int Number, bool OnKeys, bool InCommands)>();
        using var window = Open(out _, newIdea: true, reading: reader =>
            offered.Add((reader.Item.Number,
                (opened!.Keys.Here?.Groups ?? []).Concat(opened.Keys.Everywhere?.Groups ?? []).SelectMany(group => group.Rows).Any(row => row.Label == "New idea"),
                opened.InCommands.Any(command => command.Id == "work.new"))));
        opened = window;

        Select(window, 16);
        window.Commands.Execute("overseer.read");
        window.Refresh();
        Select(window, 11);
        window.Commands.Execute("overseer.read");
        window.Refresh();

        Assert.Equal([(16, false, false), (11, true, true)], offered);
    }

    [Fact]
    public void A_refused_gate_leaves_the_chip_where_it_was()
    {
        var reads = 0;
        using var window = Open(out var shown,
            readBoard: team =>
            {
                if (team == "team0")
                    reads++;
                return Task.FromResult(new Reading(team == "team0" ? Board : "[]", null));
            },
            run: _ => Task.FromResult<string?>("no"),
            confirmAccept: _ => true, reading: reader => Assert.True(reader.Accept!.Run()));
        Select(window, 11);
        var before = reads;
        window.Commands.Execute("overseer.read");
        window.Refresh();
        window.Refresh();

        Assert.Single(shown);
        Assert.Equal(before, reads);
    }

    [Fact]
    public void A_try_from_a_chip_comes_back_to_Overseer_on_the_chip_with_the_reader_open()
    {
        var handed = new List<Handover>();
        using (var window = Open(out _, handOver: handed.Add, reading: reader => reader.Try!.Run(new IssueBody("Words"), 3)))
        {
            Select(window, 11);
            window.Commands.Execute("overseer.read");
            window.Refresh();
        }

        var tried = Assert.IsType<TryHandover>(Assert.Single(handed));
        Assert.Equal(Area.Overseer, tried.Area);

        using var back = Open(out var reopened, resume: tried);
        back.ReopenReader();
        back.Refresh();

        Assert.Equal(Area.Overseer, back.CurrentArea);
        Assert.Equal(11, Assert.Single(reopened).Item.Number);
        Assert.Equal(11, back.Overseer.Board.SelectedCard!.Number);
    }

    [Fact]
    public void The_panel_lists_o_Read_and_w_Work_as_theyre_bound_now()
    {
        using var window = Open(out _);
        Select(window, 11);

        Assert.Equal("o Read · w Work · g GitHub", window.Card.Hint);

        window.Commands.Keys.Unbind([new Key('o')], Regions.Overseer);
        window.Commands.Keys.Unbind([new Key('w')], Regions.Overseer);
        window.Commands.Keys.Bind("z", "overseer.read", Regions.Overseer);
        window.Commands.Keys.Bind("y", "overseer.work", Regions.Overseer);
        window.Refresh();

        Assert.Equal("z Read · y Work · g GitHub", window.Card.Hint);
    }

    [Fact]
    public void Both_are_in_Commands_and_on_the_keys_sheet_under_Overseer()
    {
        using var window = Open(out _);
        Select(window, 11);

        Assert.Contains(window.InCommands, command => command.Id == "overseer.read");
        Assert.Contains(window.InCommands, command => command.Id == "overseer.work");
        var here = window.Keys.Here!;
        Assert.Equal("Overseer", here.Heading);
        var rows = here.Groups.SelectMany(group => group.Rows).Select(row => (row.Keys, row.Label)).ToList();
        Assert.Contains(("O", "Read the card"), rows);
        Assert.Contains(("W", "Go to the card in Work"), rows);
    }

    [Fact]
    public void Enter_g_and_ctrl_g_w_do_what_they_did()
    {
        var opened = new List<string>();
        using var window = Open(out var shown, openUrl: url =>
        {
            opened.Add(url);
            return Opening.Opened(url);
        });
        window.Commands.Execute("view.work");
        window.Work.Focus(window.Work.Items.Single(item => item.Number == 10), false);
        window.Commands.Execute("view.overseer");
        window.Refresh();
        Select(window, 11);

        window.NewKeyDownEvent(new Key('g'));
        Assert.Equal("https://github.com/o/r/issues/11", Assert.Single(opened));
        Assert.Empty(shown);

        window.Press("Ctrl+G W");
        Assert.Equal(Area.Work, window.CurrentArea);
        Assert.Equal(10, window.Work.Selected!.Number);

        window.Press("Ctrl+G O");
        window.Refresh();
        Select(window, 16);
        Directory.CreateDirectory(Path.Combine(_root, "team0", "dev", "runs", "16"));
        var dir = Path.Combine(_root, "team0", "dev", "runs", "16");
        File.WriteAllText(Path.Combine(dir, "pid"), int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        File.WriteAllText(Path.Combine(dir, "last-start"), "100");
        File.WriteAllText(Path.Combine(dir, "last-reasons"), "claimed #16");
        window.Refresh();
        window.Commands.Execute("overseer.details");
        Assert.True(window.SessionFull);
        Assert.Empty(shown);
    }

    private string Config => Path.Combine(_root, "config");
}
