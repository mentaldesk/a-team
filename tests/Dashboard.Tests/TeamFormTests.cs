using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public class TeamFormTests
{
    private static readonly TeamSettings Settings =
        new("mentaldesk/a-team", "mentaldesk", 3, "docs/vision.md", "~/code/a-team", "./bin/a-team dashboard", true,
            2, 3, 4, 4, 2, "~/code/a-team/main", ["jamescrosswell"], ["a-team"]);

    [Fact]
    public void The_form_shows_the_team_s_current_values_under_its_name()
    {
        using var form = new TeamForm("a-team", Settings, _ => null);

        Assert.Equal("Team: a-team", form.Title);
        Assert.Equal("mentaldesk/a-team", form.Repo.Text);
        Assert.Equal("mentaldesk #3", form.Project.Text);
        Assert.Equal("docs/vision.md", form.Vision.Text);
        Assert.Equal("~/code/a-team", form.Workdir.Text);
        Assert.Equal("./bin/a-team dashboard", form.Try.Text);
        Assert.Equal(TeamStatus.Working, form.Status!.Value);
        Assert.Equal(2, form.Worktrees.Value);
        Assert.Equal(Settings, form.Current());
    }

    [Fact]
    public void Focus_starts_on_Repo_with_its_caption()
    {
        using var form = new TeamForm("a-team", Settings, _ => null);
        form.SetFocus();

        Assert.True(form.Repo.HasFocus);
        Assert.Equal(TeamForm.RepoCaption, form.Message.Says);
    }

    [Fact]
    public void Each_field_says_what_it_changes_as_it_takes_focus()
    {
        using var form = new TeamForm("a-team", Settings, _ => null);
        form.SetFocus();

        form.Workdir.SetFocus();
        Assert.Equal("Where the agents work. They can't write outside it.", form.Message.Says);
        form.Vision.SetFocus();
        Assert.Equal("The Lead's yardstick, in the repo. Missing? It drafts one for you to approve.", form.Message.Says);
        form.Try.SetFocus();
        Assert.Equal("What a-team try runs to let you try a change.", form.Message.Says);
        form.Worktrees.SetFocus();
        Assert.Equal(TeamForm.WorktreesCaption, form.Message.Says);
    }

    [Fact]
    public void The_hints_read_Enter_save_and_Esc_cancel()
    {
        using var form = new TeamForm("a-team", Settings, _ => null);

        Assert.Equal("Enter save · Esc cancel", form.Hints.Says);
    }

    [Fact]
    public void Only_a_checkout_other_than_workdir_main_gets_a_read_only_row()
    {
        using var usual = new TeamForm("a-team", Settings, _ => null);
        using var other = new TeamForm("a-team", Settings with { Checkout = "~/src/a-team" }, _ => null);

        Assert.DoesNotContain(Fields(usual), field => field.Text == "~/code/a-team/main");
        var checkout = Assert.Single(Fields(other), field => field.Text == "~/src/a-team");
        Assert.True(checkout.ReadOnly);
    }

    [Fact]
    public void The_project_list_shows_the_owner_s_projects_on_the_team_s_own()
    {
        using var form = new TeamForm("a-team", Settings, _ => null);

        form.ShowProjects([new ProjectChoice("mentaldesk", 4, "TuiCode"), new ProjectChoice("mentaldesk", 3, "a-team")]);

        Assert.Equal("a-team (mentaldesk #3)", form.Project.Text);
        Assert.Equal(["TuiCode (mentaldesk #4)", "a-team (mentaldesk #3)"], Choices(form));
        form.Project.Text = "TuiCode (mentaldesk #4)";
        Assert.Equal(("mentaldesk", 4), (form.Current().ProjectOwner, form.Current().ProjectNumber));
    }

    [Fact]
    public void A_project_the_list_has_not_got_stays_at_the_top()
    {
        using var form = new TeamForm("a-team", Settings with { ProjectOwner = "someone" }, _ => null);

        form.ShowProjects([new ProjectChoice("mentaldesk", 4, "TuiCode")]);

        Assert.Equal(["someone #3", "TuiCode (mentaldesk #4)"], Choices(form));
        Assert.Equal("someone #3", form.Project.Text);
    }

    [Fact]
    public void A_project_list_that_cannot_be_read_becomes_owner_and_number_fields()
    {
        using var form = new TeamForm("a-team", Settings, _ => null);

        form.ShowProjects(null);

        Assert.False(form.Project.Visible);
        Assert.True(form.Owner.Visible);
        Assert.Equal("mentaldesk", form.Owner.Text);
        Assert.Equal("3", form.Number.Text);
        form.Number.Text = "5";
        Assert.Equal(5, form.Current().ProjectNumber);
    }

    [Fact]
    public void Enter_saves_what_the_form_holds_and_closes_it()
    {
        TeamSettings? written = null;
        using var form = new TeamForm("a-team", Settings, now =>
        {
            written = now;
            return null;
        });
        form.SetFocus();
        form.Repo.Text = "mentaldesk/other";
        form.Status!.Value = TeamStatus.Paused;

        form.Repo.NewKeyDownEvent(Key.Enter);

        var expected = Settings with { Repo = "mentaldesk/other", Working = false };
        Assert.Equal(expected, written);
        Assert.Equal(expected, form.Saved);
    }

    [Fact]
    public void Enter_in_the_open_project_list_picks_rather_than_saves()
    {
        var saved = false;
        using var form = new TeamForm("a-team", Settings, _ =>
        {
            saved = true;
            return null;
        });
        using var popover = new ListView();

        form.InvokeCommand(Command.Accept, new CommandContext(Command.Accept, new WeakReference<View>(popover), null));

        Assert.False(saved);
        Assert.Null(form.Saved);
    }

    [Fact]
    public void Esc_closes_the_form_without_saving()
    {
        var saved = false;
        using var form = new TeamForm("a-team", Settings, _ =>
        {
            saved = true;
            return null;
        });
        form.Repo.Text = "mentaldesk/other";

        Assert.True(form.NewKeyDownEvent(Key.Esc));

        Assert.False(saved);
        Assert.Null(form.Saved);
    }

    [Fact]
    public void A_refused_save_says_why_and_keeps_what_was_typed()
    {
        var saved = false;
        using var form = new TeamForm("a-team", Settings, _ =>
        {
            saved = true;
            return null;
        });
        form.Repo.Text = "not-a-repo";

        form.Save();

        Assert.False(saved);
        Assert.Null(form.Saved);
        Assert.Equal("Repo must be owner/repo, like mentaldesk/a-team, not not-a-repo.", form.Message.Says);
        Assert.Equal("not-a-repo", form.Repo.Text);
    }

    [Fact]
    public void A_save_that_fails_says_why_and_keeps_the_form_filled_in()
    {
        using var form = new TeamForm("a-team", Settings, _ => "Couldn't save a-team: Access denied.");
        form.Workdir.Text = "~/code/elsewhere";

        form.Save();

        Assert.Null(form.Saved);
        Assert.Equal("Couldn't save a-team: Access denied.", form.Message.Says);
        Assert.Equal("~/code/elsewhere", form.Workdir.Text);
    }

    [Fact]
    public void Stakeholders_sit_between_Repo_and_Project_and_Skills_between_Workdir_and_Try()
    {
        using var form = new TeamForm("a-team", Settings with { Skills = [] }, _ => null);

        Assert.Equal("jamescrosswell", form.StakeholdersRow.Text);
        Assert.Equal("none", form.SkillsRow.Text);
        Assert.Equal(
            ["Repo", "Stakeholders", "Project", "Vision", "Workdir", "Skills", "Try", "Status", "Limits"],
            form.SubViews.OfType<Label>().Where(label => label.Visible && label.X.ToString() == Pos.Absolute(1).ToString())
                .OrderBy(label => label.Frame.Y).Select(label => label.Text));
    }

    [Fact]
    public void The_list_rows_say_what_they_change_as_they_take_focus()
    {
        using var form = new TeamForm("a-team", Settings, _ => null);
        form.SetFocus();

        form.StakeholdersRow.SetFocus();
        Assert.Equal("Whose comments the team acts on. You, unless you add others.", form.Message.Says);
        form.SkillsRow.SetFocus();
        Assert.Equal("Skills the agents load. Must be installed on this machine.", form.Message.Says);
    }

    [Fact]
    public void Enter_on_Stakeholders_opens_its_picker_and_Enter_there_brings_the_row_back_updated()
    {
        using var form = new TeamForm("a-team", Settings, _ => null);
        form.FindStakeholders = _ => Task.FromResult<IReadOnlyList<string>?>(["jamescrosswell", "octocat"]);
        Picker? opened = null;
        form.RunPicker = picker =>
        {
            opened = picker;
            picker.List.Value = 1;
            picker.Toggle();
            return picker.Done() ? picker.Picked : null;
        };
        form.SetFocus();
        form.StakeholdersRow.SetFocus();

        Assert.True(form.StakeholdersRow.NewKeyDownEvent(Key.Enter));

        Assert.Equal("Stakeholders for a-team", opened?.Title);
        Assert.Equal("2 can push to mentaldesk/a-team. Type a login to add one that isn't listed.", opened?.Message.Says);
        Assert.Equal("jamescrosswell, octocat", form.StakeholdersRow.Text);
        Assert.Equal(["jamescrosswell", "octocat"], form.Current().Stakeholders);
        Assert.Null(form.Saved);
    }

    [Fact]
    public void Esc_in_a_picker_leaves_the_row_as_it_was()
    {
        using var form = new TeamForm("a-team", Settings, _ => null);
        form.FindSkills = () => new SkillsFound(["a-team", "tuicode"], [SkillsFound.Installed]);
        form.RunPicker = picker =>
        {
            picker.List.Value = 1;
            picker.Toggle();
            picker.NewKeyDownEvent(Key.Esc);
            return picker.Picked;
        };

        form.PickSkills();

        Assert.Equal("a-team", form.SkillsRow.Text);
        Assert.Equal(["a-team"], form.Current().Skills);
    }

    [Fact]
    public void The_stakeholders_picker_says_when_GitHub_can_t_be_reached_and_typing_still_works()
    {
        using var form = new TeamForm("a-team", Settings, _ => null);
        form.FindStakeholders = _ => Task.FromResult<IReadOnlyList<string>?>(null);
        string? said = null;
        form.RunPicker = picker =>
        {
            said = picker.Message.Says;
            picker.Filter.Text = "octocat";
            picker.Done();
            return picker.Picked;
        };

        form.PickStakeholders();

        Assert.Equal("Couldn't reach GitHub to see who can push to mentaldesk/a-team. Type a login to add one.", said);
        Assert.Equal(["jamescrosswell", "octocat"], form.Current().Stakeholders);
    }

    [Fact]
    public void A_chosen_skill_this_machine_has_not_got_stays_at_the_top_of_the_picker_ticked()
    {
        using var form = new TeamForm("a-team", Settings with { Skills = ["tuicode"] }, _ => null);
        form.FindSkills = () => new SkillsFound(["a-team", "structural-analysis"], [SkillsFound.Installed]);
        IReadOnlyList<(string, bool)>? rows = null;
        form.RunPicker = picker =>
        {
            rows = picker.Rows;
            return null;
        };

        form.PickSkills();

        Assert.Equal([("tuicode (not installed)", true), ("a-team", false), ("structural-analysis", false)], rows);
    }

    private static readonly TeamSettings Blank =
        new("", "", null, "docs/vision.md", "", "", false, 3, 3, 6, 6, 3, null, ["jamescrosswell"], []);

    [Fact]
    public void A_new_team_s_form_starts_on_Name_with_no_Status_and_offers_Enter_create()
    {
        using var form = TeamForm.New(Blank, ["a-team"], "~/.config/a-team/teams", (_, _) => null);
        form.SetFocus();

        Assert.Equal("New team", form.Title);
        Assert.True(form.NameField!.HasFocus);
        Assert.Null(form.Status);
        Assert.Equal("Enter create · Esc cancel", form.Hints.Says);
        Assert.Equal(
            ["Name", "Repo", "Stakeholders", "Project", "Vision", "Workdir", "Skills", "Try", "Limits"],
            form.SubViews.OfType<Label>().Where(label => label.Visible && label.X.ToString() == Pos.Absolute(1).ToString())
                .OrderBy(label => label.Frame.Y).Select(label => label.Text));
    }

    [Fact]
    public void Naming_a_new_team_says_which_file_it_becomes_and_fills_in_its_workdir()
    {
        using var form = TeamForm.New(Blank, [], "~/.config/a-team/teams", (_, _) => null);
        form.SetFocus();

        form.NameField!.Text = "fretty";

        Assert.Equal("Becomes ~/.config/a-team/teams/fretty.json", form.Message.Says);
        Assert.Equal("~/code/fretty", form.Workdir.Text);
        form.Workdir.Text = "~/src/fretty";
        form.NameField.Text = "frets";
        Assert.Equal("~/src/fretty", form.Workdir.Text);
    }

    [Theory]
    [InlineData("a-team", "There's already a team named a-team.")]
    [InlineData("my team", "my team can't be a file name: use letters, digits, '.', '_' and '-'.")]
    public void A_name_that_can_t_be_used_refuses_and_leaves_the_form_filled_in(string name, string refusal)
    {
        var created = false;
        using var form = TeamForm.New(Blank, ["a-team"], "~/.config/a-team/teams", (_, _) =>
        {
            created = true;
            return null;
        });
        form.NameField!.Text = name;
        form.Repo.Text = "mentaldesk/fretty";

        form.Save();

        Assert.False(created);
        Assert.Null(form.Saved);
        Assert.Equal(refusal, form.Message.Says);
        Assert.Equal("mentaldesk/fretty", form.Repo.Text);
    }

    [Fact]
    public void Enter_creates_the_team_under_its_name()
    {
        string? named = null;
        using var form = TeamForm.New(Blank, [], "~/.config/a-team/teams", (name, _) =>
        {
            named = name;
            return null;
        });
        form.NameField!.Text = "fretty";
        form.Repo.Text = "mentaldesk/fretty";
        form.ShowProjects(null);
        form.Owner.Text = "mentaldesk";
        form.Number.Text = "7";

        form.Save();

        Assert.Equal("fretty", named);
        Assert.Equal("fretty", form.SavedName);
        Assert.Equal(("mentaldesk/fretty", "~/code/fretty", false), (form.Saved?.Repo, form.Saved?.Workdir, form.Saved?.Working));
    }

    [Fact]
    public void A_new_team_can_leave_its_project_to_be_created_for_it()
    {
        TeamSettings? created = null;
        using var form = TeamForm.New(Blank, [], "~/.config/a-team/teams", (_, settings) =>
        {
            created = settings;
            return null;
        });
        form.NameField!.Text = "fretty";
        form.Repo.Text = "mentaldesk/fretty";
        form.ShowProjects([new ProjectChoice("mentaldesk", 4, "TuiCode")]);

        Assert.Equal([TeamForm.NewProject, "TuiCode (mentaldesk #4)"], Choices(form));
        Assert.Equal(TeamForm.NewProject, form.Project.Text);
        form.Project.Text = "TuiCode (mentaldesk #4)";
        form.Project.Text = TeamForm.NewProject;
        form.Save();

        Assert.Equal(("", (int?)null), (created?.ProjectOwner, created?.ProjectNumber));
    }

    [Fact]
    public void The_project_list_is_read_again_each_time_it_takes_focus()
    {
        var reads = 0;
        using var form = TeamForm.New(Blank, [], "~/.config/a-team/teams", (_, _) => null);
        form.Repo.Text = "mentaldesk/fretty";
        form.LoadProjects(_ =>
        {
            reads++;
            return Task.FromResult(new Reading("{\"projects\": []}", null));
        });

        form.Project.SetFocus();

        Assert.Equal(2, reads);
    }

    [Fact]
    public void Who_s_signed_in_becomes_the_stakeholder_unless_one_is_picked_already()
    {
        using var form = TeamForm.New(Blank with { Stakeholders = [] }, [], "~/.config/a-team/teams", (_, _) => null);

        form.ShowMe("jamescrosswell");
        form.ShowMe("someone-else");

        Assert.Equal(["jamescrosswell"], form.Current().Stakeholders);
    }

    [Fact]
    public void A_healthy_team_shows_no_Problems_list()
    {
        using var form = new TeamForm("a-team", Settings, _ => null);

        form.Watch(Task.FromResult(new TeamHealth([])));

        Assert.False(form.Problems.Visible);
        Assert.Equal("Enter save · Esc cancel", form.Hints.Says);
    }

    [Fact]
    public void A_team_with_problems_lists_them_at_the_foot_of_the_form_and_offers_r_repair_where_setup_can()
    {
        using var form = new TeamForm("goose", Settings, _ => null) { RepairBoard = () => null };

        form.Watch(Task.FromResult(new TeamHealth(
        [
            new TeamProblem("project", "no single-select field 'Status' on aaif-goose project 2"),
            new TeamProblem("labels", "3 of 6 missing: pitch, a-team:idea, a-team:skipped"),
        ])));

        Assert.True(form.Problems.Visible);
        Assert.Equal(
            ["project   no single-select field 'Status' on aaif-goose project 2", "labels    3 of 6 missing: pitch, a-team:idea, a-team:skipped"],
            ProblemRows(form));
        Assert.Equal("r repair · Enter save · Esc cancel", form.Hints.Says);
    }

    [Fact]
    public void Problems_setup_can_t_put_right_offer_no_repair()
    {
        var repaired = false;
        using var form = new TeamForm("goose", Settings, _ => null)
        {
            RepairBoard = () =>
            {
                repaired = true;
                return null;
            },
        };

        form.Watch(Task.FromResult(new TeamHealth([new TeamProblem("app", "goose has no GitHub App")])));
        form.Problems.NewKeyDownEvent(new Key('r'));

        Assert.Equal("Enter save · Esc cancel", form.Hints.Says);
        Assert.False(repaired);
    }

    [Fact]
    public void The_list_says_checking_until_the_check_answers()
    {
        using var form = new TeamForm("goose", Settings, _ => null);
        var health = new TaskCompletionSource<TeamHealth>();

        form.Watch(health.Task);
        Assert.Equal([TeamHealth.Checking], ProblemRows(form));

        health.SetResult(new TeamHealth([new TeamProblem("status", "2 of 9 options missing from 'Status': Exploring, Pitched")]));
        Assert.Equal(["status    2 of 9 options missing from 'Status': Exploring, Pitched"], ProblemRows(form));
    }

    [Fact]
    public void R_on_the_Problems_list_repairs_and_the_list_follows_the_check_after_it()
    {
        var repairs = 0;
        using var form = new TeamForm("goose", Settings, _ => null)
        {
            RepairBoard = () =>
            {
                repairs++;
                return Task.FromResult(new TeamHealth([]));
            },
        };
        form.Watch(Task.FromResult(new TeamHealth([new TeamProblem("labels", "1 of 6 missing: blocked")])));

        Assert.True(form.Problems.NewKeyDownEvent(new Key('r')));

        Assert.Equal(1, repairs);
        Assert.False(form.Problems.Visible);
        Assert.Equal("Enter save · Esc cancel", form.Hints.Says);
    }

    [Fact]
    public void A_repair_left_undone_keeps_the_problems()
    {
        using var form = new TeamForm("goose", Settings, _ => null) { RepairBoard = () => null };
        form.Watch(Task.FromResult(new TeamHealth([new TeamProblem("labels", "1 of 6 missing: blocked")])));

        form.Hints.Hints.Single(hint => hint.Text == "r repair").InvokeCommand(Command.Accept);

        Assert.Equal(["labels    1 of 6 missing: blocked"], ProblemRows(form));
    }

    private static IReadOnlyList<string> ProblemRows(TeamForm form) =>
        [.. Enumerable.Range(0, form.Problems.Source?.Count ?? 0).Select(i => form.Problems.Source!.ToList()[i]?.ToString() ?? "")];

    private static IReadOnlyList<TextField> Fields(TeamForm form) =>
        [.. form.SubViews.OfType<TextField>().Where(field => field is not DropDownList)];

    private static IReadOnlyList<string> Choices(TeamForm form) =>
        [.. Enumerable.Range(0, form.Project.Source?.Count ?? 0).Select(i => form.Project.Source!.ToList()[i]?.ToString() ?? "")];
}
