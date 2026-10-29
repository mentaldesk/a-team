using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public class DecisionsTests
{
    private const string Register = """
        [{"id": 5, "team": "a-team", "scope": "team", "decision": "Ctrl+PgDn/PgUp switch teams, not Tab", "why": "", "who": "you",
          "kind": "rule", "item": 356, "link": "https://github.com/mentaldesk/a-team/issues/356#issuecomment-9",
          "said": "2026-10-09T00:00:00Z", "recorded": "2026-10-09T01:00:00Z", "confirmed": "2026-10-09T00:00:00Z",
          "confirmedOn": null, "replacedBy": null, "retired": null, "yours": null},
         {"id": 2, "team": "a-team", "scope": "all", "decision": "Card commands go in the Cards menu, not the hint bar",
          "why": "The menu and the palette are advertising enough.\nThe hint bar can lose them.", "who": "you", "kind": "lean",
          "item": 34, "link": "https://github.com/mentaldesk/a-team/issues/34", "said": "2026-09-26T00:00:00Z",
          "recorded": "2026-09-27T00:00:00Z", "confirmed": "2026-10-12T00:00:00Z", "confirmedOn": 142, "replacedBy": null,
          "retired": null, "yours": null},
         {"id": 4, "team": "a-team", "scope": "team", "decision": "A reading pane shows markdown as written", "why": "",
          "who": "lead", "kind": "lean", "item": 180, "link": "https://github.com/mentaldesk/a-team/issues/180",
          "said": "2026-09-24T00:00:00Z", "recorded": "2026-09-24T00:00:00Z", "confirmed": "2026-09-24T00:00:00Z",
          "confirmedOn": null, "replacedBy": null, "retired": "2026-10-13T00:00:00Z", "yours": "2026-10-13T00:00:00Z"},
         {"id": 1, "team": "tuicode", "scope": "all", "decision": "Card commands show in the hint bar", "why": "", "who": "you",
          "kind": "lean", "item": 21, "link": "https://github.com/mentaldesk/tuicode/issues/21", "said": "2026-09-12T00:00:00Z",
          "recorded": "2026-09-12T00:00:00Z", "confirmed": "2026-09-12T00:00:00Z", "confirmedOn": null, "replacedBy": 2,
          "retired": null, "yours": null}]
        """;

    private static DecisionRegister Read(string json = Register) => DecisionRegister.Of(new Reading(json, null));

    private static List<int> Ids(IEnumerable<Decision> entries) => [.. entries.Select(entry => entry.Id)];

    [Fact]
    public void It_lists_what_stands_newest_confirmed_first()
    {
        Assert.Equal([2, 5], Ids(Read().Shown("", allTeams: true, replaced: false)));
    }

    [Fact]
    public void Show_replaced_adds_replaced_and_retired_entries_marked_as_such()
    {
        var register = Read();
        var shown = register.Shown("", allTeams: true, replaced: true);

        Assert.Equal([2, 5, 4, 1], Ids(shown));
        Assert.Equal("(retired) A reading pane shows markdown as written", DecisionRegister.Row(shown[2])[4]);
        Assert.Equal("(replaced) Card commands show in the hint bar", DecisionRegister.Row(shown[3])[4]);
    }

    [Fact]
    public void Unticking_all_teams_leaves_only_the_team_s_own()
    {
        Assert.Equal([5], Ids(Read().Shown("", allTeams: false, replaced: false)));
    }

    [Fact]
    public void Search_keeps_entries_holding_every_word_in_the_decision_or_why()
    {
        var register = Read();

        Assert.Equal([2], Ids(register.Shown("HINT card", allTeams: true, replaced: false)));
        Assert.Equal([2], Ids(register.Shown("palette", allTeams: true, replaced: false)));
        Assert.Empty(register.Shown("hint tab", allTeams: true, replaced: false));
    }

    [Fact]
    public void A_row_has_each_column()
    {
        var entry = Read().Entries.Single(entry => entry.Id == 4);

        Assert.Equal(["Confirmed", "Who", "Kind", "Scope", "Decision", "From"], DecisionRegister.Columns);
        Assert.Equal(["Lead", "lean", "a-team", "(retired) A reading pane shows markdown as written", "#180"], DecisionRegister.Row(entry)[1..]);
        Assert.Equal("all teams", Read().Entries.Single(entry => entry.Id == 2).Scope);
    }

    [Fact]
    public void The_detail_says_why_where_when_and_what_it_replaced()
    {
        var register = Read();

        var lines = register.Detail(register.Entries.Single(entry => entry.Id == 2));

        Assert.Equal("Card commands go in the Cards menu, not the hint bar", lines[0]);
        Assert.Equal(["The menu and the palette are advertising enough.", "The hint bar can lose them."], lines.Skip(1).Take(2));
        Assert.Matches(@"^Said on #34 · \d\d Sep · reconfirmed on #142 · \d\d Oct · recorded by the Lead$", lines[3]);
        Assert.Matches("^Replaces: \"Card commands show in the hint bar\" \\(#21 · \\d\\d Sep\\)$", lines[4]);
    }

    [Fact]
    public void The_detail_says_what_replaced_it_and_that_you_retired_it()
    {
        var register = Read();

        Assert.StartsWith("Replaced by: \"Card commands go in the Cards menu", register.Detail(register.Entries.Single(entry => entry.Id == 1))[^1]);
        Assert.StartsWith("Retired by you · ", register.Detail(register.Entries.Single(entry => entry.Id == 4))[^1]);
    }

    [Fact]
    public void A_register_that_can_t_be_read_says_why()
    {
        Assert.Equal("couldn't read the register", Read("not json").Failure);
        Assert.Equal("board.sh: no such team", DecisionRegister.Of(new Reading("", "board.sh: no such team")).Failure);
    }

    [Fact]
    public void Each_correction_is_the_board_s_decision_command()
    {
        Assert.Equal(["board", "a-team", "decision", "you", "5", "kind"], DecisionChange.Kind(5).Arguments("a-team"));
        Assert.Equal(["board", "a-team", "decision", "you", "5", "widen"], DecisionChange.Widen(5).Arguments("a-team"));
        Assert.Equal(["board", "a-team", "decision", "you", "5", "retire"], DecisionChange.Retire(5).Arguments("a-team"));
        Assert.Equal(["board", "a-team", "decision", "you", "5", "edit", "/tmp/x"], DecisionChange.Edit(5, " New ").Arguments("a-team", "/tmp/x"));
    }

    [Fact]
    public async Task An_edit_sends_its_wording_in_a_file_that_s_gone_afterwards()
    {
        string[]? sent = null;
        string? wording = null;

        var failure = await DecisionChange.Edit(5, "  New wording ").Run("a-team", arguments =>
        {
            sent = arguments;
            wording = File.ReadAllText(arguments[^1]);
            return Task.FromResult(new Reading("", null));
        });

        Assert.Null(failure);
        Assert.Equal("New wording\n", wording);
        Assert.False(File.Exists(sent![^1]));
    }

    [Fact]
    public async Task A_refused_correction_says_the_first_line_of_why()
    {
        var failure = await DecisionChange.Kind(5).Run("a-team", _ => Task.FromResult(new Reading("", "board.sh: no decision 5\nmore")));

        Assert.Equal("board.sh: no decision 5", failure);
    }

    private static DecisionsDialog Open(string json, List<DecisionChange>? changes = null, List<string>? opened = null) => new(
        "a-team",
        () => Task.FromResult(new Reading(json, null)),
        change =>
        {
            changes?.Add(change);
            return Task.FromResult<string?>(null);
        },
        url =>
        {
            opened?.Add(url);
            return Opening.Opened(url);
        });

    [Fact]
    public void The_dialog_is_titled_for_the_team_and_selects_the_newest()
    {
        using var dialog = Open(Register);

        Assert.Equal("Decisions · a-team", dialog.Title);
        Assert.Equal([2, 5], Ids(dialog.Shown));
        Assert.Equal(2, dialog.Selected?.Id);
        Assert.Equal("Card commands go in the Cards menu, not the hint bar", dialog.Detail.Lines[0].Text);
        Assert.True(dialog.Table.Visible);
        Assert.False(dialog.Empty.Visible);
    }

    [Fact]
    public void An_empty_register_says_so_instead_of_an_empty_table()
    {
        using var dialog = Open("[]");

        Assert.False(dialog.Table.Visible);
        Assert.True(dialog.Empty.Visible);
        Assert.Equal(DecisionRegister.Empty, dialog.Empty.Text);
    }

    [Fact]
    public void Typing_a_search_and_the_checkboxes_narrow_the_table()
    {
        using var dialog = Open(Register);

        dialog.Search.Text = "tab";
        Assert.Equal([5], Ids(dialog.Shown));
        dialog.Search.Text = "nothing like it";
        Assert.Empty(dialog.Shown);
        Assert.True(dialog.Empty.Visible);
        Assert.Equal("(no matches)", dialog.Empty.Text);
        dialog.Search.Text = "";
        dialog.AllTeams.Value = CheckState.UnChecked;
        Assert.Equal([5], Ids(dialog.Shown));
        dialog.Replaced.Value = CheckState.Checked;
        Assert.Equal([5, 4], Ids(dialog.Shown));
    }

    [Fact]
    public void Enter_opens_where_it_was_said()
    {
        var opened = new List<string>();
        using var dialog = Open(Register, opened: opened);

        dialog.Table.NewKeyDownEvent(Key.Enter);

        Assert.Equal(["https://github.com/mentaldesk/a-team/issues/34"], opened);
    }

    [Fact]
    public void k_w_and_r_send_their_corrections_for_the_selected_entry()
    {
        var changes = new List<DecisionChange>();
        using var dialog = Open(Register, changes);

        dialog.Table.NewKeyDownEvent(Key.K);
        dialog.Table.NewKeyDownEvent(Key.W);
        dialog.Table.NewKeyDownEvent(Key.R);

        Assert.Equal([DecisionChange.Kind(2), DecisionChange.Widen(2), DecisionChange.Retire(2)], changes);
        Assert.Equal(2, dialog.Selected?.Id);
    }

    [Fact]
    public void e_edits_the_wording_in_place_and_Enter_saves_it()
    {
        var changes = new List<DecisionChange>();
        using var dialog = Open(Register, changes);

        dialog.Table.NewKeyDownEvent(Key.E);
        Assert.True(dialog.Editor.Visible);
        Assert.Equal("Card commands go in the Cards menu, not the hint bar", dialog.Editor.Text);
        dialog.Editor.Text = "Card commands go in the Cards menu";
        dialog.Editor.NewKeyDownEvent(Key.Enter);

        Assert.False(dialog.Editor.Visible);
        Assert.Equal([DecisionChange.Edit(2, "Card commands go in the Cards menu")], changes);
    }

    [Fact]
    public void Esc_leaves_an_edit_unsaved()
    {
        var changes = new List<DecisionChange>();
        using var dialog = Open(Register, changes);

        dialog.StartEditing();
        dialog.Editor.Text = "Something else";
        dialog.Editor.NewKeyDownEvent(Key.Esc);

        Assert.False(dialog.Editor.Visible);
        Assert.Empty(changes);
    }

    [Fact]
    public void The_buttons_name_each_key_and_follow_the_selected_entry()
    {
        using var dialog = Open(Register);

        Assert.Equal(["Enter Open on GitHub", "e Edit", "k Rule/lean", "w This team only", "r Retire", "Esc Close"],
            dialog.ButtonLabels);
        dialog.AllTeams.Value = CheckState.UnChecked;
        dialog.Replaced.Value = CheckState.Checked;
        dialog.Table.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(4, dialog.Selected?.Id);
        Assert.Equal(["Enter Open on GitHub", "e Edit", "k Rule/lean", "w All teams", "r Bring back", "Esc Close"],
            dialog.ButtonLabels);
        dialog.StartEditing();
        Assert.Equal(["Save", "Esc Cancel"], dialog.ButtonLabels);
    }

    [Fact]
    public void F1_lists_every_key_the_buttons_name()
    {
        using var dialog = Open(Register);

        Assert.Equal(["Enter", "e", "k", "w", "r", "Up/Down", "Tab", "Esc"],
            DialogTests.Rows(DialogTests.F1(dialog)).Select(row => row.Keys));
    }
}
