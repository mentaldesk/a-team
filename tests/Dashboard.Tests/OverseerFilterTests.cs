using System.Drawing;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

/// <summary>Ctrl+F in Overseer: every lane narrowed to the cards matching a number or title.</summary>
[Collection("StaticConfiguration")]
public class OverseerFilterTests : IDisposable
{
    private const string Docker = "Dispatched runs have no /usr/local/bin on PATH, so a team's try can't find docker";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");
    private readonly PlatformKeyBinding _quit = Application.DefaultKeyBindings![Command.Quit];

    public void Dispose()
    {
        Application.SetDefaultKeyBinding(Command.Quit, _quit);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string Config => Path.Combine(_root, "config");

    private static BoardCard Card(int number, string status, string title, string team = "a") =>
        new(team, number, title, $"https://github.com/o/{team}/issues/{number}", status, "", CardKind.Task, null);

    [Theory]
    [InlineData("docker")]
    [InlineData("dpath")]
    [InlineData("DPATH")]
    [InlineData("823")]
    [InlineData("#823")]
    [InlineData("Dispatched")]
    [InlineData("usr")]
    public void A_card_matches_on_its_number_or_whole_title_by_camel_humps_or_contains_in_any_case(string query) =>
        Assert.True(OverseerBoard.Matches(query, Card(823, "Ready", Docker)));

    [Theory]
    [InlineData("docks")]
    [InlineData("824")]
    [InlineData("#24x")]
    public void A_card_the_letters_miss_doesnt_match(string query) =>
        Assert.False(OverseerBoard.Matches(query, Card(823, "Ready", Docker)));

    [Fact]
    public void A_match_only_in_the_part_of_the_title_the_chip_cuts_off_still_shows_the_card()
    {
        var board = new OverseerBoard(["a"]);
        board.Show([Card(823, "Ready", Docker), Card(824, "Ready", "Something else")]);

        board.FilterBy("docker");

        Assert.Equal([823], board.Column("a", Array.IndexOf(OverseerBoard.Columns, "Ready")).Select(card => card.Number));
    }

    [Fact]
    public void While_filtering_no_lane_is_folded_and_a_lane_with_no_matches_says_so()
    {
        var board = new OverseerBoard(["a", "b"]);
        board.Show([.. Enumerable.Range(1, 9).Select(number => Card(number, "Idea", $"Fix the thing {number}")), Card(20, "Ready", "Unrelated", "b")]);
        var idea = Array.IndexOf(OverseerBoard.Columns, "Idea");
        Assert.Contains(board.Chips("a", idea), chip => chip.IsMore);

        board.FilterBy("fix");

        Assert.Equal(9, board.Chips("a", idea).Count);
        Assert.DoesNotContain(board.Chips("a", idea), chip => chip.IsMore);
        Assert.False(board.NoMatches("a"));
        Assert.True(board.NoMatches("b"));
        Assert.Equal("9 cards", board.MatchCount);

        board.FilterBy("");

        Assert.Contains(board.Chips("a", idea), chip => chip.IsMore);
        Assert.False(board.NoMatches("b"));
    }

    [Theory]
    [InlineData("unrelated", "1 card")]
    [InlineData("thing", "2 cards")]
    [InlineData("zzz", "No results")]
    public void The_count_is_of_the_matches_across_every_team(string query, string count)
    {
        var board = new OverseerBoard(["a", "b"]);
        board.Show([Card(1, "Idea", "A thing"), Card(2, "Ready", "Another thing", "b"), Card(3, "In review", "Unrelated", "b")]);

        board.FilterBy(query);

        Assert.Equal(count, board.MatchCount);
    }

    [Fact]
    public void Changing_the_filter_selects_the_first_match()
    {
        var board = new OverseerBoard(["a", "b"]);
        board.Show([Card(1, "Idea", "Alpha"), Card(2, "Ready", "Beta", "b"), Card(3, "In review", "Beta again", "b")]);
        board.Select("a", 1);

        board.FilterBy("beta");

        Assert.Equal(new ChipPlace("b", Array.IndexOf(OverseerBoard.Columns, "Ready"), 2), board.Selected);
    }

    private DashboardWindow Open(Func<string, Task<Reading>>? readBoard = null)
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
            Opening.Opened,
            (_, _, _, _, _, _, _, _, _, _, _, _) => null,
            Area.Overseer,
            IconStyle.Unicode,
            new DashboardOptions { ReadBoard = readBoard ?? (team => Task.FromResult(new Reading(Cards(team), null))) });
        window.Frame = new Rectangle(0, 0, 160, 40);
        window.Layout(new Size(160, 40));
        window.Refresh();
        return window;
    }

    private static string Chip(int number, string status, string title, string team = "team0") =>
        $$"""{"number": {{number}}, "title": "{{title}}", "url": "https://github.com/o/r/issues/{{number}}", "status": "{{status}}", "team": "{{team}}", "kind": "task"}""";

    private static string Cards(string team, params string[] more) => team == "team0"
        ? "[" + string.Join(",", [Chip(823, "Ready", Docker), Chip(824, "Idea", "Overseer filters lanes"), Chip(825, "In review", "Idle role lines"), .. more]) + "]"
        : "[" + Chip(900, "Ready", "Another team's docker card", "team1") + "]";

    private static void Type(DashboardWindow window, string text)
    {
        foreach (var letter in text)
            window.NewKeyDownEvent(new Key(letter));
        window.Refresh();
    }

    [Fact]
    public void Ctrl_f_opens_the_bar_with_focus_in_its_empty_field_and_the_whole_board_showing()
    {
        using var window = Open();

        Assert.True(window.NewKeyDownEvent(Key.F.WithCtrl));

        Assert.True(window.Overseer.FilterOpen);
        Assert.True(window.Overseer.InFilter);
        Assert.Equal("", window.Overseer.FilterBar.Query);
        Assert.Equal("Type to filter", window.Overseer.FilterBar.Field.Placeholder);
        Assert.Equal("", window.Overseer.FilterBar.Count);
        Assert.False(window.Overseer.Board.Filtering);
        Assert.Equal(4, window.Overseer.Board.Matching);
    }

    [Fact]
    public void Letters_typed_into_the_bar_filter_rather_than_run_their_commands()
    {
        using var window = Open();
        window.NewKeyDownEvent(Key.F.WithCtrl);

        Type(window, "dock");

        Assert.Equal("dock", window.Overseer.FilterBar.Query);
        Assert.Equal(Area.Overseer, window.CurrentArea);
        Assert.Equal("2 cards", window.Overseer.FilterBar.Count);
        Assert.Equal(new ChipPlace("team0", Array.IndexOf(OverseerBoard.Columns, "Ready"), 823), window.Overseer.Board.Selected);
        Assert.True(window.Overseer.InFilter);
    }

    [Theory]
    [InlineData(KeyCode.CursorDown)]
    [InlineData(KeyCode.Tab)]
    public void Down_or_tab_moves_from_the_bar_to_the_first_match_and_the_arrows_work_on_the_narrowed_board(KeyCode key)
    {
        using var window = Open();
        window.NewKeyDownEvent(Key.F.WithCtrl);
        Type(window, "docker");

        window.NewKeyDownEvent(new Key(key));

        Assert.False(window.Overseer.InFilter);
        Assert.Equal(823, window.Overseer.Board.SelectedCard?.Number);
        window.NewKeyDownEvent(Key.Tab);
        Assert.Equal(("team1", 900), (window.Overseer.Board.SelectedCard?.Team, window.Overseer.Board.SelectedCard?.Number));
    }

    [Fact]
    public void Ctrl_f_from_the_cards_goes_back_to_the_bar_with_its_text_selected()
    {
        using var window = Open();
        window.NewKeyDownEvent(Key.F.WithCtrl);
        Type(window, "docker");
        window.NewKeyDownEvent(Key.CursorDown);

        window.NewKeyDownEvent(Key.F.WithCtrl);

        Assert.True(window.Overseer.InFilter);
        Assert.Equal("docker", window.Overseer.FilterBar.Field.SelectedText);
    }

    [Fact]
    public void Esc_from_the_bar_closes_it_and_brings_back_the_card_selected_before()
    {
        using var window = Open();
        Assert.True(window.Overseer.Select("team0", 825));
        window.NewKeyDownEvent(Key.F.WithCtrl);
        Type(window, "docker");

        window.NewKeyDownEvent(Key.Esc);

        Assert.False(window.Overseer.FilterOpen);
        Assert.False(window.Overseer.Board.Filtering);
        Assert.Equal(825, window.Overseer.Board.SelectedCard?.Number);
    }

    [Fact]
    public void Esc_from_a_card_picked_on_the_narrowed_board_keeps_that_card_selected()
    {
        using var window = Open();
        Assert.True(window.Overseer.Select("team0", 825));
        window.NewKeyDownEvent(Key.F.WithCtrl);
        Type(window, "docker");
        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(Key.Tab);

        window.NewKeyDownEvent(Key.Esc);

        Assert.False(window.Overseer.FilterOpen);
        Assert.Equal(("team1", 900), (window.Overseer.Board.SelectedCard?.Team, window.Overseer.Board.SelectedCard?.Number));
    }

    [Fact]
    public void Esc_from_a_card_picked_on_the_narrowed_board_keeps_it_selected_when_the_whole_board_folds_it_away()
    {
        var ideas = Enumerable.Range(830, 30).Select(number => Chip(number, "Idea", $"Idea {number}")).Append(Chip(870, "Idea", "Quokka habitats"));
        using var window = Open(readBoard: team => Task.FromResult(new Reading(Cards(team, [.. ideas]), null)));
        Assert.DoesNotContain(window.Overseer.Board.Chips("team0", 0), chip => chip.Card?.Number == 870);
        window.NewKeyDownEvent(Key.F.WithCtrl);
        Type(window, "quokka");
        window.NewKeyDownEvent(Key.CursorDown);

        window.NewKeyDownEvent(Key.Esc);

        Assert.Equal(870, window.Overseer.Board.SelectedCard?.Number);
        Assert.True(window.Overseer.Board.Unfolded("team0"));
    }

    [Fact]
    public void A_board_read_while_the_bar_is_open_keeps_the_filter_and_narrows_the_new_cards()
    {
        var extra = Array.Empty<string>();
        using var window = Open(readBoard: team => Task.FromResult(new Reading(Cards(team, extra), null)));
        window.NewKeyDownEvent(Key.F.WithCtrl);
        Type(window, "docker");
        extra = [Chip(826, "Building", "Docker on the runner"), Chip(827, "Idea", "Nothing to see")];

        window.Commands.Execute("overseer.refresh");
        window.Refresh();

        Assert.True(window.Overseer.FilterOpen);
        Assert.Equal("docker", window.Overseer.Board.Filter);
        Assert.Contains(window.Overseer.Board.Cards, card => card.Number == 827);
        Assert.Equal([826], window.Overseer.Board.Column("team0", Array.IndexOf(OverseerBoard.Columns, "Building")).Select(card => card.Number));
        Assert.Empty(window.Overseer.Board.Column("team0", Array.IndexOf(OverseerBoard.Columns, "Idea")));
        Assert.Equal("3 cards", window.Overseer.FilterBar.Count);
    }

    [Fact]
    public void The_menu_and_the_keys_sheet_list_ctrl_f_in_overseer_and_nothing_else_there_uses_it()
    {
        using var window = Open();

        Assert.Contains("overseer.filter", AppMenu.Layout.Single(menu => menu.Title == "_View").Ids);
        var rows = window.Keys.Here!.Groups.SelectMany(group => group.Rows).Select(row => (row.Keys, row.Label)).ToList();
        Assert.Equal("Overseer", window.Keys.Here!.Heading);
        Assert.Contains(("Ctrl+F", "Filter cards"), rows);
        Assert.Equal(["overseer.filter"],
            window.Commands.Keys.Bindings.Where(binding => binding.Display == "Ctrl+F"
                && (binding.Scope == Regions.Overseer || binding.Scope == Regions.Filter || binding.Scope == MentalDesk.Tui.Commands.CommandScope.Global))
                .Select(binding => binding.CommandId).Distinct());
    }
}
