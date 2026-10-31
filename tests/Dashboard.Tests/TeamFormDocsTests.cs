using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public sealed class TeamFormDocsTests : IDisposable
{
    private static readonly RepoFiles Files = RepoFiles.Parse("mentaldesk/a-team", "main", "docs/vision.md\ndocs/guide/index.md\n");

    private readonly string _checkout = Directory.CreateTempSubdirectory("a-team-checkout").FullName;

    public TeamFormDocsTests()
    {
        Directory.CreateDirectory(Path.Combine(_checkout, "docs", "guide"));
        File.WriteAllText(Path.Combine(_checkout, "docs", "guide", "index.md"), "");
    }

    public void Dispose() => Directory.Delete(_checkout, recursive: true);

    private TeamSettings Settings =>
        new("mentaldesk/a-team", "mentaldesk", 3, "docs/vision.md", "~/code/a-team", "./bin/a-team dashboard", true,
            2, 1, 3, 4, 4, 2, _checkout, ["jamescrosswell"], ["a-team"]);

    private static readonly TeamSettings Blank =
        new("", "", null, "docs/vision.md", "", "", false, 3, 2, 3, 6, 6, 3, null, ["jamescrosswell"], []);

    [Fact]
    public void With_the_Customer_lead_off_there_are_no_docs_controls_and_ticking_it_shows_them()
    {
        using var form = new TeamForm("a-team", Settings, _ => null);
        Assert.False(form.DocsRowsView.Visible);

        form.CustomerLead.Value = CheckState.Checked;
        Assert.True(form.DocsRowsView.Visible);
        Assert.Equal(["User docs", "First page", "Enter ▸"], form.DocsRowsView.SubViews.OfType<Label>().Select(label => label.Text));

        form.CustomerLead.Value = CheckState.UnChecked;
        Assert.False(form.DocsRowsView.Visible);
    }

    [Fact]
    public void A_new_team_s_form_shows_the_docs_controls_only_with_the_Customer_lead_ticked()
    {
        using var form = TeamForm.New(Blank, [], "~/.config/a-team/teams", (_, _) => null);
        Assert.False(form.DocsRowsView.Visible);

        form.CustomerLead.Value = CheckState.Checked;

        Assert.True(form.DocsRowsView.Visible);
        Assert.Equal(DocsChoice.WriteNew, form.UserDocs.Value);
        Assert.Equal(TeamForm.SuggestedDocs, form.Docs.Text);
    }

    [Fact]
    public void The_rows_below_move_down_to_make_room_for_the_docs_controls()
    {
        using var form = new TeamForm("a-team", Settings, _ => null);
        var limits = form.SubViews.OfType<Label>().Single(label => label.Text == "Limits");
        form.Layout();
        var off = limits.Frame.Y;

        form.CustomerLead.Value = CheckState.Checked;
        form.Layout();

        Assert.Equal(off + 2, limits.Frame.Y);
        Assert.Equal(off, form.DocsRowsView.Frame.Y);
    }

    [Fact]
    public void I_already_have_docs_is_chosen_first_when_the_docs_page_is_in_the_checkout()
    {
        using var there = new TeamForm("a-team", Settings with { Customer = true, Docs = "docs/guide/index.md" }, _ => null);
        using var missing = new TeamForm("a-team", Settings with { Customer = true, Docs = "docs/user/index.md" }, _ => null);
        using var none = new TeamForm("a-team", Settings with { Customer = true }, _ => null);

        Assert.Equal(DocsChoice.Existing, there.UserDocs.Value);
        Assert.Equal(DocsChoice.WriteNew, missing.UserDocs.Value);
        Assert.Equal(DocsChoice.WriteNew, none.UserDocs.Value);
    }

    [Fact]
    public void The_suggested_first_page_fills_in_only_where_the_team_has_no_docs_page()
    {
        using var empty = new TeamForm("a-team", Settings, _ => null);
        using var chosen = new TeamForm("a-team", Settings with { Docs = "manual/README.md" }, _ => null);

        Assert.Equal("docs/guide/index.md", empty.Docs.Text);
        Assert.Equal("manual/README.md", chosen.Docs.Text);
    }

    [Fact]
    public void Each_choice_captions_First_page_in_its_own_words()
    {
        using var form = new TeamForm("a-team", Settings with { Customer = true }, _ => null);
        form.SetFocus();

        form.Docs.SetFocus();
        Assert.Equal(TeamForm.SuggestedDocsCaption, form.Hints.Message.Says);

        form.Docs.Text = "manual/index.md";
        form.Try.SetFocus();
        form.Docs.SetFocus();
        Assert.Equal(TeamForm.NewDocsCaption, form.Hints.Message.Says);

        form.UserDocs.Value = DocsChoice.Existing;
        form.Try.SetFocus();
        form.Docs.SetFocus();
        Assert.Equal("The user docs' first page. The Customer lead keeps every page right.", form.Hints.Message.Says);
    }

    [Fact]
    public void Unticking_keeps_the_first_page_chosen_and_ticking_again_shows_it()
    {
        using var form = new TeamForm("a-team", Settings, _ => null);
        form.CustomerLead.Value = CheckState.Checked;
        form.Docs.Text = "manual/index.md";

        form.CustomerLead.Value = CheckState.UnChecked;
        Assert.Equal("manual/index.md", form.Current().Docs);

        form.CustomerLead.Value = CheckState.Checked;
        Assert.Equal("manual/index.md", form.Docs.Text);
    }

    [Fact]
    public void A_saved_first_page_is_there_when_the_form_opens_again_with_the_Customer_lead_off()
    {
        using var form = new TeamForm("a-team", Settings with { Docs = "manual/index.md" }, _ => null);

        form.CustomerLead.Value = CheckState.Checked;

        Assert.Equal("manual/index.md", form.Docs.Text);
        Assert.Equal("manual/index.md", form.Current().Docs);
    }

    [Fact]
    public void Ticking_and_unticking_the_Customer_lead_saves_no_docs_page_the_team_did_not_have()
    {
        var settings = Settings;
        using var form = new TeamForm("a-team", settings, _ => null);

        form.CustomerLead.Value = CheckState.Checked;
        form.CustomerLead.Value = CheckState.UnChecked;

        Assert.Equal(settings, form.Current());
    }

    [Fact]
    public void Saving_with_the_Customer_lead_on_and_First_page_cleared_saves_the_suggestion()
    {
        using var form = new TeamForm("a-team", Settings, _ => null);
        form.CustomerLead.Value = CheckState.Checked;
        form.Docs.Text = "";

        form.Save();

        Assert.Equal(TeamForm.SuggestedDocs, form.Saved?.Docs);
    }

    [Fact]
    public void Enter_on_First_page_opens_the_repo_s_Markdown_files_and_puts_back_the_one_picked()
    {
        using var form = new TeamForm("a-team", Settings with { Customer = true }, _ => null)
        {
            FindRepoFiles = _ => Task.FromResult(Files),
        };
        string? opened = null;
        form.RunFilePicker = picker =>
        {
            opened = picker.Title;
            return "docs/vision.md";
        };
        form.SetFocus();
        form.UserDocs.Value = DocsChoice.Existing;
        form.Docs.SetFocus();

        Assert.True(form.Docs.NewKeyDownEvent(Key.Enter));

        Assert.Equal("First page of the docs for a-team", opened);
        Assert.Equal("docs/vision.md", form.Docs.Text);
    }
}
