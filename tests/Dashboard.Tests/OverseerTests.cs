using System.Drawing;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

/// <summary>Overseer: every team's board on one screen, how long each card has waited, and moving between them.</summary>
[Collection("StaticConfiguration")]
public class OverseerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");
    private readonly PlatformKeyBinding _quit = Application.DefaultKeyBindings![Command.Quit];

    public void Dispose()
    {
        Application.SetDefaultKeyBinding(Command.Quit, _quit);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(0, 0, 0, "0m")]
    [InlineData(0, 0, 30, "30m")]
    [InlineData(0, 0, 59, "59m")]
    [InlineData(0, 5, 10, "5h")]
    [InlineData(6, 2, 10, "6d")]
    public void A_chip_s_age_is_in_its_largest_unit(int days, int hours, int minutes, string shown) =>
        Assert.Equal(shown, Ages.Short(new TimeSpan(days, hours, minutes, 0)));

    [Theory]
    [InlineData(6, 2, 10, "6d 2h 10m")]
    [InlineData(1, 0, 5, "1d 0h 5m")]
    [InlineData(0, 3, 0, "3h 0m")]
    [InlineData(0, 0, 12, "12m")]
    public void The_details_give_the_age_in_full(int days, int hours, int minutes, string shown) =>
        Assert.Equal(shown, Ages.Full(new TimeSpan(days, hours, minutes, 0)));

    [Theory]
    [InlineData("30m", 0, 0, 30)]
    [InlineData("2h", 0, 2, 0)]
    [InlineData(" 3d ", 3, 0, 0)]
    [InlineData("14D", 14, 0, 0)]
    [InlineData("2w", 14, 0, 0)]
    public void A_limit_is_a_number_and_a_unit(string text, int days, int hours, int minutes)
    {
        Assert.True(ColumnLimits.TryParse(text, out var limit));
        Assert.Equal(new TimeSpan(days, hours, minutes, 0), limit);
    }

    [Fact]
    public void A_blank_limit_is_no_limit()
    {
        Assert.True(ColumnLimits.TryParse("  ", out var limit));
        Assert.Null(limit);
    }

    [Theory]
    [InlineData("3")]
    [InlineData("d")]
    [InlineData("3 days")]
    [InlineData("1.5d")]
    [InlineData("-2d")]
    [InlineData("0d")]
    [InlineData("3y")]
    public void A_limit_it_can_t_read_is_refused(string text) =>
        Assert.False(ColumnLimits.TryParse(text, out _));

    [Fact]
    public void A_card_is_over_only_past_its_own_column_s_limit()
    {
        var limits = new Dictionary<string, string> { ["Ready"] = "2d", ["Building"] = "" };

        Assert.True(ColumnLimits.IsOver(Card(1, "Ready", days: 3), limits, Now));
        Assert.False(ColumnLimits.IsOver(Card(2, "Ready", days: 1), limits, Now));
        Assert.False(ColumnLimits.IsOver(Card(3, "Building", days: 30), limits, Now));
        Assert.False(ColumnLimits.IsOver(Card(4, "Idea", days: 300), limits, Now));
    }

    [Fact]
    public void The_overview_reads_into_cards_with_their_kind_and_when_they_got_there()
    {
        var cards = BoardCard.Parse("""
            [{"number": 404, "title": "Overseer", "url": "https://github.com/o/r/issues/404", "status": "Building",
              "priority": "High", "since": "2026-10-03T10:00:00Z", "parent": null, "team": "a-team", "kind": "pitch"},
             {"number": 414, "title": "Every board", "url": "u", "status": "Ready", "priority": null,
              "since": "2026-10-08T12:00:00Z", "parent": 404, "team": "a-team", "kind": "task"},
             {"number": 415, "status": "In review", "team": "a-team", "kind": "docs"},
             {"number": 395, "status": "Idea", "team": "a-team", "kind": "yours"},
             {"title": "no number"}]
            """);

        Assert.Equal([404, 414, 415, 395], cards.Select(card => card.Number));
        Assert.Equal(["◆", "●", "✎", "○"], cards.Select(card => card.Mark(IconStyle.Unicode)));
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero), cards[0].Since);
        Assert.Equal(404, cards[1].Parent);
        Assert.Equal("", cards[1].Priority);
        Assert.Null(cards[2].Since);
        Assert.Empty(BoardCard.Parse("not json"));
    }

    [Fact]
    public void In_a_nerd_font_a_card_wears_the_icon_work_gives_it()
    {
        Assert.Equal(Icon.Pitch, Card(404, "Building", days: 6).Icon);
        Assert.Equal(Icon.Idea, Card(395, "Idea", days: 1).Icon);
        Assert.Equal(Icon.Task, (Card(414, "Ready", days: 1) with { Kind = CardKind.Task }).Icon);
        Assert.Equal(Icon.Docs, (Card(415, "In review", days: 1) with { Kind = CardKind.Docs }).Icon);
        Assert.Equal(Icons.Field(Icon.Pitch, IconStyle.NerdFont), Card(404, "Building", days: 6).Mark(IconStyle.NerdFont));
    }

    [Fact]
    public void A_chip_is_its_mark_number_and_age_or_an_animation_frame_while_an_agent_is_on_it()
    {
        var card = Card(404, "Building", days: 6);

        Assert.Equal("◆2     6d", OverseerView.ChipText(new Chip(Card(2, "Building", days: 6)), Now, null, IconStyle.Unicode));
        Assert.Equal("◆404   6d", OverseerView.ChipText(new Chip(card), Now, null, IconStyle.Unicode));
        Assert.Equal("◆404  ｱ#ﾘ", OverseerView.ChipText(new Chip(card), Now, "ｱ#ﾘ", IconStyle.Unicode));
        Assert.Equal("◆404    ✺", OverseerView.ChipText(new Chip(card), Now, "  ✺", IconStyle.Unicode));
        Assert.Equal("+7", OverseerView.ChipText(new Chip(null, 7), Now, null, IconStyle.Unicode));
    }

    [Fact]
    public void Columns_run_in_flow_order_with_no_Done() =>
        Assert.Equal(["Idea", "Exploring", "Pitched", "Approved", "Building", "Ready", "In progress", "In review"], OverseerBoard.Columns);

    [Fact]
    public void Within_a_column_chips_run_oldest_first()
    {
        var board = Board([Card(1, "Ready", days: 1), Card(2, "Ready", days: 5), Card(3, "Ready", days: 3)]);

        Assert.Equal([2, 3, 1], board.Chips("a", 5).Select(chip => chip.Card!.Number));
    }

    [Fact]
    public void Past_three_rows_a_column_folds_the_rest_into_a_count()
    {
        var board = Board([.. Enumerable.Range(1, 6).Select(n => Card(n, "Ready", days: 10 - n)), Card(9, "Idea", days: 1)]);

        var chips = board.Chips("a", 5);
        Assert.Equal([1, 2], chips.Take(2).Select(chip => chip.Card!.Number));
        Assert.True(chips[2].IsMore);
        Assert.Equal(4, chips[2].Hidden);
        Assert.Equal(3, board.Rows("a"));
    }

    [Fact]
    public void A_tall_screen_folds_deeper_so_every_lane_still_fits()
    {
        var board = Board([.. Enumerable.Range(1, 9).Select(n => Card(n, "Ready", days: 10 - n)), Card(20, "Idea", days: 1, team: "b")], ["a", "b"]);

        board.FitTo(height: 12, frame: 2);

        Assert.Equal(7, board.Rows("a"));
        Assert.True(board.Chips("a", 5)[^1].IsMore);
        Assert.Equal(3, board.Chips("a", 5)[^1].Hidden);
    }

    [Fact]
    public void A_screen_with_room_for_every_card_folds_nothing()
    {
        var board = Board([.. Enumerable.Range(1, 9).Select(n => Card(n, "Ready", days: 10 - n))]);

        board.FitTo(height: 40, frame: 2);

        Assert.Equal(9, board.Rows("a"));
        Assert.DoesNotContain(board.Chips("a", 5), chip => chip.IsMore);
    }

    [Fact]
    public void A_short_screen_still_folds_at_three_rows()
    {
        var board = Board([.. Enumerable.Range(1, 9).Select(n => Card(n, "Ready", days: 10 - n))]);

        board.FitTo(height: 2, frame: 2);

        Assert.Equal(3, board.Rows("a"));
    }

    [Fact]
    public void Folding_deeper_moves_the_selection_from_the_count_to_the_card_it_stood_for()
    {
        var board = Board([.. Enumerable.Range(1, 9).Select(n => Card(n, "Ready", days: 10 - n))]);
        board.SelectFirst();
        board.MoveRow(+1);
        board.MoveRow(+1);
        Assert.True(board.OnMore);

        board.FitTo(height: 7, frame: 2);

        Assert.Equal(3, board.SelectedCard?.Number);
    }

    [Fact]
    public void Three_cards_fit_without_folding()
    {
        var board = Board([Card(1, "Ready", days: 3), Card(2, "Ready", days: 2), Card(3, "Ready", days: 1)]);

        Assert.DoesNotContain(board.Chips("a", 5), chip => chip.IsMore);
    }

    [Fact]
    public void Enter_on_the_count_unfolds_the_lane_and_Esc_folds_it_again()
    {
        var board = Board([.. Enumerable.Range(1, 5).Select(n => Card(n, "Ready", days: 10 - n))]);
        board.SelectFirst();
        board.MoveRow(+1);
        board.MoveRow(+1);
        Assert.True(board.OnMore);

        board.Unfold();

        Assert.Equal(5, board.Chips("a", 5).Count);
        Assert.Equal(3, board.SelectedCard?.Number);
        board.MoveRow(+1);
        board.MoveRow(+1);
        Assert.Equal(5, board.SelectedCard?.Number);

        Assert.True(board.Fold());

        Assert.True(board.OnMore);
        Assert.False(board.Fold());
    }

    [Fact]
    public void Left_and_right_skip_empty_columns_and_stop_at_the_edges()
    {
        var board = Board([Card(1, "Idea", days: 1), Card(2, "Building", days: 1), Card(3, "In review", days: 1)]);
        board.SelectFirst();
        Assert.Equal(1, board.SelectedCard?.Number);

        board.MoveColumn(+1);
        Assert.Equal(2, board.SelectedCard?.Number);
        board.MoveColumn(+1);
        Assert.Equal(3, board.SelectedCard?.Number);
        board.MoveColumn(+1);
        Assert.Equal(3, board.SelectedCard?.Number);
        board.MoveColumn(-1);
        Assert.Equal(2, board.SelectedCard?.Number);
    }

    [Fact]
    public void Left_and_right_keep_the_row_or_take_the_column_s_last()
    {
        var board = Board([Card(1, "Idea", days: 3), Card(2, "Idea", days: 2), Card(3, "Idea", days: 1), Card(4, "Pitched", days: 1)]);
        board.SelectFirst();
        board.MoveRow(+1);
        board.MoveRow(+1);

        board.MoveColumn(+1);

        Assert.Equal(4, board.SelectedCard?.Number);
    }

    [Fact]
    public void Up_and_down_go_along_a_column_then_on_into_the_next_lane_with_cards()
    {
        var board = Board(
            [Card(1, "Ready", days: 2), Card(2, "Ready", days: 1), Card(5, "Building", days: 1, team: "c"), Card(6, "Ready", days: 1, team: "c")],
            ["a", "b", "c"]);
        board.Select("a", 1);

        board.MoveRow(+1);
        Assert.Equal(2, board.SelectedCard?.Number);
        board.MoveRow(+1);
        Assert.Equal(("c", 6), (board.Selected!.Value.Team, board.SelectedCard?.Number));
        board.MoveRow(+1);
        Assert.Equal(6, board.SelectedCard?.Number);
        board.MoveRow(-1);
        Assert.Equal(("a", 2), (board.Selected!.Value.Team, board.SelectedCard?.Number));
    }

    [Fact]
    public void Down_into_a_lane_without_that_column_lands_on_the_nearest_one_with_cards()
    {
        var board = Board([Card(1, "Ready", days: 1), Card(5, "In review", days: 1, team: "b"), Card(6, "Idea", days: 1, team: "b")], ["a", "b"]);
        board.Select("a", 1);

        board.MoveRow(+1);

        Assert.Equal(5, board.SelectedCard?.Number);
    }

    [Fact]
    public void Down_from_a_folded_column_s_count_goes_on_to_the_next_lane()
    {
        var board = Board([.. Enumerable.Range(1, 5).Select(n => Card(n, "Ready", days: 10 - n)), Card(9, "Ready", days: 1, team: "b")], ["a", "b"]);
        board.Select("a", 2);
        board.MoveRow(+1);
        Assert.True(board.OnMore);

        board.MoveRow(+1);

        Assert.Equal(9, board.SelectedCard?.Number);
        board.MoveRow(-1);
        Assert.True(board.OnMore);
    }

    [Fact]
    public void Tab_goes_to_the_next_lane_with_cards_and_round_past_the_end()
    {
        var board = Board(
            [Card(1, "Ready", days: 1), Card(2, "Ready", days: 2), Card(5, "Idea", days: 1, team: "c"), Card(6, "In review", days: 1, team: "c")],
            ["a", "b", "c"]);
        board.Select("a", 1);

        board.MoveLane(+1);
        Assert.Equal(("c", 6), (board.Selected!.Value.Team, board.SelectedCard?.Number));
        board.MoveLane(+1);
        Assert.Equal(("a", 2), (board.Selected!.Value.Team, board.SelectedCard?.Number));
        board.MoveLane(-1);
        Assert.Equal("c", board.Selected!.Value.Team);
    }

    [Fact]
    public void A_new_read_keeps_the_selection_on_its_card_wherever_it_went()
    {
        var board = Board([Card(1, "Ready", days: 1), Card(2, "Idea", days: 1)]);
        board.Select("a", 1);

        board.Show([Card(1, "In progress", days: 0), Card(2, "Idea", days: 1)]);

        Assert.Equal(1, board.SelectedCard?.Number);
        Assert.Equal(6, board.Selected!.Value.Column);
    }

    [Fact]
    public void A_lane_names_its_busy_roles_each_with_a_cell_for_its_animation_or_says_idle_and_counts_what_s_over()
    {
        var heading = OverseerView.Heading("a-team", ["lead", "dev"], 2);

        Assert.Equal("a-team · Lead   · Dev   · 2 over", heading.Title);
        Assert.Equal([new HeaderSlot(14, "lead"), new HeaderSlot(22, "dev")], heading.Slots);
        Assert.Equal("tui · idle", OverseerView.Heading("tui", [], 0).Title);
        Assert.Empty(OverseerView.Heading("tui", [], 0).Slots);
        Assert.Equal("docs · Customer lead  ", OverseerView.Heading("docs", ["customer"], 0).Title);
        Assert.Equal("ops · Reviewer", OverseerView.Heading("ops", ["reviewer"], 0).Title);
    }

    [Fact]
    public void A_pitch_s_details_list_its_open_tasks_and_its_column_s_limit()
    {
        var pitch = Card(46, "Building", days: 30) with { Priority = "High", Title = "Ranking" };
        var task = Card(387, "Ready", days: 2) with { Parent = 46, Kind = CardKind.Task, Title = "Triage in order" };
        var state = OverseerState.Empty with
        {
            Now = Now,
            Limits = new Dictionary<string, string> { ["Building"] = "14d" },
            Waiting = (_, number) => number == 46 ? new WaitingItem(46, "", "Building", "", "a", "lead", "drafting tasks") : null,
        };

        var lines = OverseerView.Describe(pitch, [pitch, task], state);

        Assert.Equal(
            ["Pitch · High · a · Building for 30d 0h 0m (limit 14d)", "Waiting: lead · drafting tasks", "Open tasks:", "  #387 Triage in order (Ready, 2d)"],
            lines);
    }

    [Fact]
    public void A_task_s_details_say_unranked_and_no_limit()
    {
        var task = Card(387, "Ready", days: 2) with { Kind = CardKind.Task };

        Assert.Equal(["Task · Unranked · a · Ready for 2d 0h 0m"], OverseerView.Describe(task, [task], OverseerState.Empty with { Now = Now }));
    }

    [Fact]
    public void Columns_share_the_width_so_a_120_column_terminal_never_scrolls_sideways()
    {
        var width = OverseerView.ColumnWidth(118);

        Assert.Equal(14, width);
        Assert.True(width * OverseerBoard.Columns.Length <= 118);
        Assert.True(OverseerView.ChipText(new Chip(Card(12345, "Ready", days: 99)), Now, null, IconStyle.NerdFont).Length < width);
    }

    [Fact]
    public void o_opens_Overseer_and_reads_every_team_s_board_once()
    {
        var read = new List<string>();
        using var window = Open(team =>
        {
            read.Add(team);
            return Task.FromResult(new Reading(Overview(team), null));
        });

        Assert.True(window.NewKeyDownEvent(new Key('o')));
        window.Refresh();

        Assert.Equal(Area.Overseer, window.CurrentArea);
        Assert.Equal(["team0", "team1"], read);
        Assert.Equal(4, window.Overseer.Board.Cards.Count);
        Assert.Equal(["team0 · idle", "team1 · idle"], window.Overseer.LaneTitles);
        Assert.True(window.NewKeyDownEvent(new Key('w')));
        Assert.Equal(Area.Work, window.CurrentArea);
        Assert.True(window.NewKeyDownEvent(new Key('d')));
        Assert.Equal(Area.Dashboard, window.CurrentArea);
    }

    [Fact]
    public void F5_reads_the_boards_again_and_the_bar_says_when()
    {
        var reads = 0;
        var clock = new Clock();
        using var window = Open(team =>
        {
            reads++;
            return Task.FromResult(new Reading(Overview(team), null));
        }, clock: clock);
        window.NewKeyDownEvent(new Key('o'));
        window.Refresh();
        clock.Now += TimeSpan.FromMinutes(3);
        window.Refresh();

        Assert.Equal("read 3m ago", window.Status.State.Text);
        Assert.True(window.NewKeyDownEvent(Key.F5));
        Assert.Equal(4, reads);
    }

    [Fact]
    public void Overseer_is_in_the_palette_and_the_View_menu_and_its_key_can_be_rebound()
    {
        Directory.CreateDirectory(Config);
        new DashboardSettings(Config).WriteKeys([("view.overseer", new Key('v'))]);
        using var window = Open();

        Assert.Contains(window.Commands.Registered, command => command is { Id: "view.overseer", Label: "Overseer" });
        Assert.Contains(window.MenuItems, item => item.Id == "view.overseer");
        Assert.True(window.NewKeyDownEvent(new Key('v')));
        Assert.Equal(Area.Overseer, window.CurrentArea);
    }

    [Fact]
    public void The_arrows_move_between_chips_and_g_opens_the_card()
    {
        var opened = new List<string>();
        using var window = Open(openUrl: opened.Add);
        window.NewKeyDownEvent(new Key('o'));
        window.Refresh();

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(new Key('g'));

        Assert.Equal(["https://github.com/o/team0/issues/12"], opened);
        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(("team1", 21), (window.Overseer.Board.Selected!.Value.Team, window.Overseer.Board.SelectedCard?.Number));
    }

    [Fact]
    public void Tab_and_Shift_Tab_move_between_lanes()
    {
        using var window = Open();
        window.NewKeyDownEvent(new Key('o'));
        window.Refresh();

        Assert.True(window.NewKeyDownEvent(Key.Tab));
        Assert.Equal("team1", window.Overseer.Board.Selected!.Value.Team);
        Assert.True(window.NewKeyDownEvent(Key.Tab.WithShift));
        Assert.Equal("team0", window.Overseer.Board.Selected!.Value.Team);
    }

    [Fact]
    public void PgDn_and_PgUp_scroll_lanes_that_don_t_fit()
    {
        using var window = Open(height: 8);
        window.NewKeyDownEvent(new Key('o'));
        window.Refresh();
        Assert.Equal(0, window.Overseer.ScrolledTo);

        Assert.True(window.NewKeyDownEvent(Key.PageDown));
        Assert.True(window.Overseer.ScrolledTo > 0);
        Assert.Equal("team1", window.Overseer.Board.Selected!.Value.Team);

        Assert.True(window.NewKeyDownEvent(Key.PageUp));
        Assert.Equal(0, window.Overseer.ScrolledTo);
        Assert.Equal("team0", window.Overseer.Board.Selected!.Value.Team);
    }

    [Fact]
    public void Enter_toggles_the_details_of_the_selected_card()
    {
        using var window = Open();
        window.NewKeyDownEvent(new Key('o'));
        window.Refresh();

        Assert.True(window.NewKeyDownEvent(Key.Enter));

        Assert.True(window.Overseer.DetailsShown);
        Assert.Equal("#11 Eleven", window.Overseer.DetailsTitle);
        Assert.StartsWith("Yours · Unranked · team0 · Idea for ", window.Overseer.Details, StringComparison.Ordinal);
        window.NewKeyDownEvent(Key.Enter);
        Assert.False(window.Overseer.DetailsShown);
    }

    [Fact]
    public void r_is_enabled_only_on_a_card_an_agent_is_on_and_takes_you_to_its_session()
    {
        WriteRun("team0", "dev", 12);
        using var window = Open();
        window.NewKeyDownEvent(new Key('o'));
        window.Refresh();

        Assert.False(window.Commands.IsEnabled("overseer.session"));
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(12, window.Overseer.Board.SelectedCard?.Number);
        Assert.True(window.Commands.IsEnabled("overseer.session"));
        Assert.Equal(["team0 · Dev  ", "team1 · idle"], window.Overseer.LaneTitles);

        Assert.True(window.NewKeyDownEvent(new Key('r')));

        Assert.Equal(Area.Dashboard, window.CurrentArea);
        Assert.Equal(1, window.ExpandedAgent);
        Assert.True(window.Panes[1].HasFocus);
    }

    [Fact]
    public void A_lead_run_started_for_one_card_is_on_that_card_and_a_run_across_the_board_on_none()
    {
        WriteRun("team0", "lead", card: 11);
        WriteRun("team1", "lead");
        using var window = Open();
        window.NewKeyDownEvent(new Key('o'));
        window.Refresh();

        Assert.Equal(11, window.Overseer.Board.SelectedCard?.Number);
        Assert.True(window.Commands.IsEnabled("overseer.session"));
        Assert.Equal(["team0 · Lead  ", "team1 · Lead  "], window.Overseer.LaneTitles);
        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal("team1", window.Overseer.Board.Selected!.Value.Team);
        Assert.False(window.Commands.IsEnabled("overseer.session"));
    }

    [Fact]
    public void A_limit_Settings_can_t_read_is_refused_naming_the_field()
    {
        using var dialog = new SettingsDialog(
            new ThemeSetting(BundledThemes.Midnight, _ => { }, _ => { }), new IconSetting(IconStyle.Unicode, _ => { }, _ => { }), false,
            new CommandRegistry(), new TeamConfigs(Config), () => { }, IconStyle.Unicode,
            limits: new Dictionary<string, string> { ["Ready"] = "2d" });

        Assert.All(dialog.LimitFields.Where(limit => limit.Column != "Ready"), limit => Assert.Equal("", limit.Field.Text));
        Assert.Equal("2d", dialog.LimitFields.Single(limit => limit.Column == "Ready").Field.Text);
        Assert.Null(dialog.UnreadableLimit());
        dialog.LimitFields.Single(limit => limit.Column == "Building").Field.Text = "two weeks";

        Assert.Equal("Building", dialog.UnreadableLimit());
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.False(dialog.Confirmed);
        Assert.Contains("Building", dialog.Message.Says, StringComparison.Ordinal);
    }

    [Fact]
    public void Limits_are_kept_per_column_and_blank_ones_left_out()
    {
        var settings = new DashboardSettings(Config);

        settings.WriteLimits(new Dictionary<string, string> { ["Ready"] = " 2d ", ["Building"] = "" });

        Assert.Equal(new Dictionary<string, string> { ["Ready"] = "2d" }, settings.ReadLimits());
    }

    private static BoardCard Card(int number, string status, int days, string team = "a") =>
        new(team, number, $"#{number}", $"https://github.com/o/{team}/issues/{number}", status, "", CardKind.Pitch,
            Now - TimeSpan.FromDays(days));

    private static OverseerBoard Board(IReadOnlyList<BoardCard> cards, IReadOnlyList<string>? teams = null)
    {
        var board = new OverseerBoard(teams ?? ["a"]);
        board.Show(cards);
        return board;
    }

    private static string Overview(string team) => team == "team0"
        ? """
          [{"number": 11, "title": "Eleven", "url": "https://github.com/o/team0/issues/11", "status": "Idea", "since": "2026-10-01T00:00:00Z", "team": "team0", "kind": "yours"},
           {"number": 12, "title": "Twelve", "url": "https://github.com/o/team0/issues/12", "status": "In progress", "since": "2026-10-01T00:00:00Z", "team": "team0", "kind": "task"}]
          """
        : """
          [{"number": 21, "title": "Twenty-one", "url": "https://github.com/o/team1/issues/21", "status": "Ready", "since": "2026-10-01T00:00:00Z", "team": "team1", "kind": "task"},
           {"number": 22, "title": "Twenty-two", "url": "https://github.com/o/team1/issues/22", "status": "Pitched", "since": "2026-10-01T00:00:00Z", "team": "team1", "kind": "pitch"}]
          """;

    private DashboardWindow Open(
        Func<string, Task<Reading>>? readBoard = null, Action<string>? openUrl = null, TimeProvider? clock = null, int height = 30)
    {
        Directory.CreateDirectory(_root);
        var window = new DashboardWindow(
            [("team0", "lead"), ("team0", "dev"), ("team1", "lead"), ("team1", "dev")],
            _root,
            new DashboardSettings(Config),
            new TeamConfigs(Config),
            _ => Task.FromResult<string?>(null),
            _ => Task.FromResult(new Reading("[]", null)),
            _ => Task.FromResult(new Reading("{\"body\": \"\"}", null)),
            openUrl ?? (_ => { }),
            (_, _, _, _, _, _, _, _, _) => null,
            Area.Dashboard,
            IconStyle.Unicode,
            clock: clock,
            readBoard: readBoard ?? (team => Task.FromResult(new Reading(Overview(team), null))));
        window.Frame = new Rectangle(0, 0, 120, height);
        window.Layout(new Size(120, height));
        window.Refresh();
        return window;
    }

    /// <summary>A live run on <paramref name="task"/> or <paramref name="card"/>, or on neither: this test's own process
    /// stands in for it.</summary>
    private void WriteRun(string team, string role, int? task = null, int? card = null)
    {
        var dir = Path.Combine(_root, team, role);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "pid"), Environment.ProcessId.ToString());
        File.WriteAllText(Path.Combine(dir, "last-start"), "100");
        if (task is not null)
            File.WriteAllText(Path.Combine(dir, "task"), $$"""{"number": {{task}}, "title": "a task"}""");
        if (card is not null)
            File.WriteAllText(Path.Combine(dir, "card"), $$"""{"number": {{card}}}""");
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = OverseerTests.Now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private string Config => Path.Combine(_root, "config");
}
