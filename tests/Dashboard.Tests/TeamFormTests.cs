using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public class TeamFormTests
{
    private static readonly TeamSettings Settings =
        new("mentaldesk/a-team", "mentaldesk", 3, "docs/vision.md", "~/code/a-team", "./bin/a-team dashboard", true,
            2, 3, 4, 4, 2, "~/code/a-team/main");

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
        Assert.Equal(TeamStatus.Working, form.Status.Value);
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
        form.Status.Value = TeamStatus.Paused;

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

    private static IReadOnlyList<TextField> Fields(TeamForm form) =>
        [.. form.SubViews.OfType<TextField>().Where(field => field is not DropDownList)];

    private static IReadOnlyList<string> Choices(TeamForm form) =>
        [.. Enumerable.Range(0, form.Project.Source?.Count ?? 0).Select(i => form.Project.Source!.ToList()[i]?.ToString() ?? "")];
}
