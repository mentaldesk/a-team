using MentalDesk.Tui.Keys;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public class TeamsDialogTests : IDisposable
{
    private readonly string _configRoot = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_configRoot))
            Directory.Delete(_configRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void It_lists_each_team_with_its_repo_and_whether_it_is_working()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        WriteTeam("beta", """{"repo": "mentaldesk/beta-long", "dispatch": {"enabled": false}}""");
        WriteTeam("gamma", "{\n  \"repo\": \"mentaldesk/gamma\",\n}\n");
        using var dialog = Open();

        Assert.Equal(
            [
                "alpha  mentaldesk/alpha      working",
                "beta   mentaldesk/beta-long  paused",
                "gamma                        can't read this file",
            ],
            TeamRows(dialog));
        Assert.Equal(0, dialog.Teams.Value);
        Assert.True(dialog.Teams.HasFocus);
    }

    [Fact]
    public void Its_actions_are_buttons_with_their_keys()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        using var dialog = Open();

        Assert.Equal(["p Pause", "Enter Edit", "x Remove", "n New team", "Esc Close"], dialog.ButtonLabels);
    }

    [Fact]
    public void It_heads_each_column_over_the_list()
    {
        WriteTeam("ai", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        using var dialog = Open(check: _ => Task.FromResult(new TeamHealth([])));

        Assert.Equal("Team  Repo              Status   Health", dialog.TeamHeader);
        Assert.Equal(["ai    mentaldesk/alpha  working  ok"], TeamRows(dialog));
    }

    [Fact]
    public void Today_shows_what_each_team_has_spent_and_a_team_at_its_budget_reads_budget_reached()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        WriteTeam("beta", """{"repo": "mentaldesk/beta", "dispatch": {"enabled": true, "budget": 40}}""");
        WriteTeam("gamma", """{"repo": "mentaldesk/gamma", "dispatch": {"enabled": false, "budget": 1}}""");
        var spent = new Dictionary<string, string>
        {
            ["alpha"] = """{"cost":18.4,"budget":0,"reached":false}""",
            ["beta"] = """{"cost":41.37,"budget":40,"reached":true}""",
            ["gamma"] = """{"cost":0,"budget":1,"reached":false}""",
        };
        using var dialog = Open(today: team => Task.FromResult(new Reading(spent[team], null)));

        Assert.Equal("Team   Repo              Status           Today", dialog.TeamHeader);
        Assert.Equal(
            [
                "alpha  mentaldesk/alpha  working         $18.40",
                "beta   mentaldesk/beta   budget reached  $41.37",
                "gamma  mentaldesk/gamma  paused           $0.00",
            ],
            TeamRows(dialog));
    }

    [Fact]
    public void Today_stays_blank_for_a_team_whose_cost_cannot_be_read()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        using var dialog = Open(today: _ => Task.FromResult(new Reading("", "board.sh: no config")));

        Assert.Equal(["alpha  mentaldesk/alpha  working"], TeamRows(dialog));
    }

    [Fact]
    public void Selecting_a_team_whose_file_is_broken_says_which_line_and_why()
    {
        WriteTeam("alpha", """{"dispatch": {"enabled": true}}""");
        WriteTeam("gamma", "{\n  \"repo\": \"mentaldesk/gamma\",\n}\n");
        using var dialog = Open();
        Assert.Equal("", dialog.Message.Says);

        dialog.Teams.Value = 1;

        Assert.StartsWith("gamma.json, line 3: ", dialog.Message.Says);
        dialog.Teams.Value = 0;
        Assert.Equal("", dialog.Message.Says);
    }

    [Fact]
    public void P_pauses_a_working_team_and_starts_a_paused_one_straight_away_and_says_so()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        using var dialog = Open();

        Assert.True(dialog.Teams.NewKeyDownEvent(new Key('p')));

        Assert.Equal(["alpha  mentaldesk/alpha  paused"], TeamRows(dialog));
        Assert.True(new TeamConfigs(_configRoot).IsPaused("alpha"));
        Assert.Equal("alpha is paused.", dialog.Message.Says);
        Assert.Equal("p Resume", dialog.ButtonLabels[0]);

        Button(dialog, "p Resume").InvokeCommand(Command.Accept);

        Assert.Equal(["alpha  mentaldesk/alpha  working"], TeamRows(dialog));
        Assert.False(new TeamConfigs(_configRoot).IsPaused("alpha"));
        Assert.Equal("alpha is working.", dialog.Message.Says);
        Assert.Equal("p Pause", dialog.ButtonLabels[0]);
    }

    [Fact]
    public void P_on_a_team_whose_file_is_broken_leaves_it_alone_and_says_why()
    {
        const string broken = "{\"dispatch\": {\"enabled\": true,}}";
        WriteTeam("gamma", broken);
        using var dialog = Open();

        dialog.Teams.NewKeyDownEvent(new Key('p'));

        Assert.Equal(broken, File.ReadAllText(Path.Combine(_configRoot, "teams", "gamma.json")));
        Assert.Equal("Fix gamma.json before starting or pausing gamma: a-team can't read it.", dialog.Message.Says);
    }

    [Fact]
    public void It_says_checking_until_each_team_s_check_answers()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        WriteTeam("goose", """{"repo": "aaif-goose/goose"}""");
        var goose = new TaskCompletionSource<TeamHealth>();
        using var dialog = Open(check: team => team == "goose"
            ? goose.Task
            : Task.FromResult(new TeamHealth([])));

        Assert.Equal(["alpha  mentaldesk/alpha  working  ok", "goose  aaif-goose/goose  paused   checking…"], TeamRows(dialog));

        goose.SetResult(new TeamHealth([new TeamProblem("project", "no field"), new TeamProblem("labels", "1 of 6 missing")]));
        Assert.Equal(["alpha  mentaldesk/alpha  working  ok", "goose  aaif-goose/goose  paused   2 problems"], TeamRows(dialog));
    }

    [Fact]
    public async Task It_shows_the_check_the_panes_already_ran_and_a_repair_reaches_them()
    {
        WriteTeam("goose", """{"repo": "aaif-goose/goose"}""");
        var ran = 0;
        var shared = new TeamChecks(
            _ => Task.FromResult(++ran == 1 ? new TeamHealth([new TeamProblem("status", "missing")]) : new TeamHealth([])),
            new TeamConfigs(_configRoot).Stamp);
        shared.Follow(["goose"]);
        using var dialog = Open(checks: shared);

        Assert.Equal(["goose  aaif-goose/goose  paused  1 problem"], TeamRows(dialog));
        Assert.Equal(1, ran);

        dialog.RepairTeam = _ => true;
        await dialog.Repaired("goose")!;

        Assert.Null(shared.Fatal("goose"));
    }

    [Fact]
    public void A_file_that_can_t_be_read_is_one_problem_without_running_a_check()
    {
        WriteTeam("gamma", "{\"repo\": ");
        var ran = false;
        using var dialog = Open(check: _ =>
        {
            ran = true;
            return Task.FromResult(new TeamHealth([]));
        });

        Assert.Equal(["gamma        can't read this file  1 problem"], TeamRows(dialog));
        Assert.False(ran);
    }

    [Fact]
    public async Task After_a_repair_the_team_is_checked_again_and_its_row_follows()
    {
        WriteTeam("goose", """{"repo": "aaif-goose/goose"}""");
        var checks = 0;
        using var dialog = Open(check: _ => Task.FromResult(++checks == 1
            ? new TeamHealth([new TeamProblem("labels", "no 'blocked' label, so the Dev can't mark a task that waits on your answer")])
            : new TeamHealth([])));
        Assert.Equal(["goose  aaif-goose/goose  paused  1 problem"], TeamRows(dialog));
        dialog.RepairTeam = _ => true;

        var after = dialog.Repaired("goose");

        Assert.Empty((await after!).Problems);
        Assert.Equal(["goose  aaif-goose/goose  paused  ok"], TeamRows(dialog));
    }

    [Fact]
    public void A_repair_left_undone_checks_nothing_again()
    {
        WriteTeam("goose", """{"repo": "aaif-goose/goose"}""");
        var checks = 0;
        using var dialog = Open(check: _ =>
        {
            checks++;
            return Task.FromResult(new TeamHealth([new TeamProblem("labels", "no 'blocked' label, so the Dev can't mark a task that waits on your answer")]));
        });
        dialog.RepairTeam = _ => false;

        Assert.Null(dialog.Repaired("goose"));
        Assert.Equal(1, checks);
    }

    [Fact]
    public void Enter_on_a_team_opens_its_form_and_saving_it_updates_the_file_and_the_row()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "app": {"id": 1}, "dispatch": {"enabled": true, "hold": ["dev"]}}""");
        using var dialog = Open();
        string? opened = null;
        dialog.EditTeam = (team, settings, save) =>
        {
            opened = team;
            var after = settings with { Repo = "mentaldesk/beta", Working = false };
            return save(after) is null ? after : null;
        };

        dialog.Teams.NewKeyDownEvent(Key.Enter);

        Assert.Equal("alpha", opened);
        Assert.Equal(["alpha  mentaldesk/beta  paused"], TeamRows(dialog));
        Assert.Equal(
            """{"repo": "mentaldesk/beta", "app": {"id": 1}, "dispatch": {"enabled": false, "hold": ["dev"]}}""",
            File.ReadAllText(Path.Combine(_configRoot, "teams", "alpha.json")));
        Assert.Null(dialog.Result);
    }

    [Fact]
    public void A_save_that_leaves_the_workdir_missing_warns()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "workdir": "/nowhere/alpha"}""");
        using var dialog = Open();
        dialog.EditTeam = (_, settings, _) => settings;

        dialog.Teams.NewKeyDownEvent(Key.Enter);

        Assert.Equal("/nowhere/alpha isn't there, so the agents would have nothing to work in.", dialog.Message.Says);
    }

    [Fact]
    public void A_save_with_a_docs_page_not_written_yet_says_where_the_Customer_lead_will_propose_them()
    {
        var workdir = Path.Combine(_configRoot, "work");
        Directory.CreateDirectory(Path.Combine(workdir, "main", "docs"));
        File.WriteAllText(Path.Combine(workdir, "main", "docs", "vision.md"), "");
        WriteTeam("alpha", $$"""{"repo": "mentaldesk/alpha", "workdir": "{{workdir}}", "vision": "docs/vision.md"}""");
        using var dialog = Open(today: _ => Task.FromResult(new Reading("", null)));
        dialog.EditTeam = (_, settings, save) =>
        {
            var after = settings with { Customer = true, Docs = "docs/guide/index.md" };
            return save(after) is null ? after : null;
        };

        dialog.Teams.NewKeyDownEvent(Key.Enter);

        Assert.Equal("The Customer lead will propose the docs at docs/guide/index.md.", dialog.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Base), dialog.Message.SchemeName);
    }

    [Fact]
    public void Enter_on_a_team_whose_file_is_broken_opens_nothing_and_says_why()
    {
        WriteTeam("gamma", "{\n  \"repo\": \"o/r\",\n}");
        using var dialog = Open();
        var opened = false;
        dialog.EditTeam = (_, _, _) =>
        {
            opened = true;
            return null;
        };

        dialog.Teams.NewKeyDownEvent(Key.Enter);

        Assert.False(opened);
        Assert.StartsWith("gamma.json, line 3: ", dialog.Message.Says);
    }

    [Fact]
    public void A_save_to_a_read_only_file_leaves_the_form_open_and_filled_in()
    {
        if (OperatingSystem.IsWindows())
            return;
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "project": {"owner": "mentaldesk", "number": 1}, "vision": "v", "workdir": "w"}""");
        var path = Path.Combine(_configRoot, "teams", "alpha.json");
        File.SetUnixFileMode(path, UnixFileMode.UserRead);
        using var dialog = Open();
        TeamForm? shown = null;
        dialog.EditTeam = (team, settings, save) =>
        {
            shown = new TeamForm(team, settings, save);
            shown.Repo.Text = "mentaldesk/beta";
            shown.Save();
            return shown.Saved;
        };

        dialog.Teams.NewKeyDownEvent(Key.Enter);

        using var form = shown!;
        Assert.Null(form.Saved);
        Assert.StartsWith("Couldn't save alpha: ", form.Message.Says);
        Assert.Equal("mentaldesk/beta", form.Repo.Text);
        Assert.Equal(["alpha  mentaldesk/alpha  paused"], TeamRows(dialog));
    }

    [Fact]
    public void The_Edit_button_opens_the_form()
    {
        WriteTeam("alpha", """{"dispatch": {"enabled": true}}""");
        using var dialog = Open();
        var opened = false;
        dialog.EditTeam = (_, _, _) =>
        {
            opened = true;
            return null;
        };

        Button(dialog, "Enter Edit").InvokeCommand(Command.Accept);

        Assert.True(opened);
    }

    [Fact]
    public void N_creates_a_team_selects_it_and_says_where_it_stands()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        using var dialog = Open();
        dialog.CreateTeam = (_, create) =>
        {
            WriteTeam("fretty", """{"repo": "mentaldesk/fretty"}""");
            return "fretty";
        };
        string? followed = null;
        dialog.FollowTeam = team =>
        {
            followed = team;
            return ($"{team} is paused. Press p when you want it to start.", Schemes.Base);
        };

        Assert.True(dialog.Teams.NewKeyDownEvent(new Key('n')));

        Assert.Equal("fretty", followed);
        Assert.Equal(["alpha   mentaldesk/alpha   working", "fretty  mentaldesk/fretty  paused"], TeamRows(dialog));
        Assert.Equal(1, dialog.Teams.Value);
        Assert.Equal("fretty is paused. Press p when you want it to start.", dialog.Message.Says);
    }

    [Fact]
    public void X_asks_first_and_Esc_leaves_the_team_as_it_was()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        using var dialog = Open();
        (string, string, string)? asked = null;
        dialog.ConfirmRemove = (team, repo, kept) =>
        {
            asked = (team, repo, kept);
            return false;
        };

        Assert.True(dialog.Teams.NewKeyDownEvent(new Key('x')));

        Assert.Equal(("alpha", "mentaldesk/alpha", "alpha.json.removed"), asked);
        Assert.True(File.Exists(Path.Combine(_configRoot, "teams", "alpha.json")));
        Assert.Equal(["alpha  mentaldesk/alpha  working"], TeamRows(dialog));
        Assert.Empty(dialog.RemovedTeams);
    }

    [Fact]
    public void Removing_a_team_takes_it_off_straight_away_keeps_its_file_as_removed_and_says_so()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha"}""");
        WriteTeam("beta", """{"repo": "mentaldesk/beta"}""");
        using var dialog = Open();
        dialog.ConfirmRemove = (_, _, _) => true;
        dialog.Teams.Value = 1;

        Button(dialog, "x Remove").InvokeCommand(Command.Accept);

        Assert.Equal(["alpha  mentaldesk/alpha  paused"], TeamRows(dialog));
        Assert.Equal(0, dialog.Teams.Value);
        Assert.Equal(["beta"], dialog.RemovedTeams);
        Assert.True(File.Exists(Path.Combine(_configRoot, "teams", "beta.json.removed")));
        Assert.Equal("Removed beta. Rename beta.json.removed back to beta.json to bring it back.", dialog.Message.Says);
    }

    [Fact]
    public void A_new_team_form_that_was_cancelled_changes_nothing()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha"}""");
        using var dialog = Open();
        dialog.CreateTeam = (_, _) => null;
        var followed = false;
        dialog.FollowTeam = _ =>
        {
            followed = true;
            return null;
        };

        Button(dialog, "n New team").InvokeCommand(Command.Accept);

        Assert.False(followed);
        Assert.Equal(["alpha  mentaldesk/alpha  paused"], TeamRows(dialog));
    }

    [Fact]
    public void Back_past_the_first_step_reopens_the_form_on_the_team_and_follows_it_again()
    {
        using var dialog = Open();
        List<string?> opened = [];
        dialog.CreateTeam = (again, create) =>
        {
            opened.Add(again);
            if (again is null)
                WriteTeam("fretty", """{"repo": "mentaldesk/fretty"}""");
            return "fretty";
        };
        var follows = 0;
        dialog.FollowTeam = team => ++follows == 1 ? null : ($"{team} is working.", Schemes.Base);

        dialog.NewTeam();

        Assert.Equal([null, "fretty"], opened);
        Assert.Equal("fretty is working.", dialog.Message.Says);
    }

    [Fact]
    public void Cancelling_the_form_Back_reopened_cancels_the_new_team()
    {
        using var dialog = Open();
        dialog.CreateTeam = (again, create) =>
        {
            if (again is not null)
                return null;
            WriteTeam("fretty", """{"repo": "mentaldesk/fretty"}""");
            return "fretty";
        };
        dialog.FollowTeam = _ => null;

        dialog.NewTeam();

        Assert.Empty(TeamRows(dialog));
        Assert.Equal("fretty is cancelled. Its clone, App and project are still there.", dialog.Message.Says);
    }

    [Fact]
    public void Removing_the_last_team_leaves_only_New_team_and_Close()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        using var dialog = Open();
        dialog.ConfirmRemove = (_, _, _) => true;

        dialog.Teams.NewKeyDownEvent(new Key('x'));

        Assert.Empty(TeamRows(dialog));
        Assert.Null(dialog.Teams.Value);
        Assert.Equal(["n New team", "Esc Close"], dialog.ButtonLabels);
    }

    [Fact]
    public void F1_lists_its_keys_and_Ctrl_G_opens_the_guide()
    {
        WriteTeam("alpha", """{"repo": "mentaldesk/alpha", "dispatch": {"enabled": true}}""");
        var opened = new List<string>();
        using var dialog = Open(showGuide: opened.Add);

        Assert.True(dialog.Teams.NewKeyDownEvent(Key.G.WithCtrl));

        Assert.Equal([GuideDialog.Teams], opened);
        Assert.Equal(
            [("p", "Pause"), ("Enter", "Edit"), ("x", "Remove"), ("n", "New team"), ("Ctrl+G", "Guide"), ("Up/Down", "Select a team"), ("Esc", "Close")],
            DialogTests.Rows(dialog.Sheet));
    }

    private void WriteTeam(string team, string config)
    {
        var teams = Path.Combine(_configRoot, "teams");
        Directory.CreateDirectory(teams);
        File.WriteAllText(Path.Combine(teams, $"{team}.json"), config);
    }

    private static IReadOnlyList<string> TeamRows(TeamsDialog dialog) =>
        [.. Enumerable.Range(0, dialog.Teams.Source?.Count ?? 0).Select(i => dialog.Teams.Source!.ToList()[i]?.ToString() ?? "")];

    private static Button Button(TeamsDialog dialog, string label) =>
        dialog.Foot.Shown.Single(button => button.Text.Trim() == label);

    private TeamsDialog Open(
        Func<string, Task<TeamHealth>>? check = null,
        Action<string>? showGuide = null,
        TeamChecks? checks = null,
        Func<string, Task<Reading>>? today = null)
    {
        var dialog = new TeamsDialog(new TeamConfigs(_configRoot), check: check, showGuide: showGuide, checks: checks, today: today);
        dialog.SetFocus();
        return dialog;
    }
}
