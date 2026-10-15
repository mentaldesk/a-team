using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public sealed class TeamFormPathTests : IDisposable
{
    private static readonly TeamSettings Settings =
        new("mentaldesk/a-team", "mentaldesk", 3, "docs/vision.md", "~/code/a-team", "./bin/a-team dashboard", true,
            2, 1, 3, 4, 4, 2, "~/code/a-team/main", ["jamescrosswell"], ["a-team"]);

    private static readonly TeamSettings Blank =
        new("", "", null, "docs/vision.md", "", "", false, 3, 2, 3, 6, 6, 3, null, ["jamescrosswell"], []);

    private static readonly RepoFiles Files = RepoFiles.Parse("mentaldesk/a-team", "main", "docs/vision.md\ndocs/guide/index.md\n");

    private readonly string _home = Directory.CreateTempSubdirectory("a-team-home").FullName;

    public TeamFormPathTests()
    {
        Directory.CreateDirectory(Path.Combine(_home, "code/a-team/main"));
        Directory.CreateDirectory(Path.Combine(_home, "code/empty"));
    }

    public void Dispose() => Directory.Delete(_home, recursive: true);

    [Fact]
    public void Workdir_Vision_and_Docs_offer_a_picker_and_still_take_typing()
    {
        using var form = new TeamForm("a-team", Settings, _ => null);

        Assert.Equal(5, form.SubViews.OfType<Label>().Count(label => label.Text == "Enter ▸"));
        Assert.Contains(form.DocsRowsView.SubViews.OfType<Label>(), label => label.Text == "Enter ▸");
        form.Workdir.Text = "~/src/a-team";
        Assert.Equal("~/src/a-team", form.Current().Workdir);
    }

    [Fact]
    public void Enter_on_Workdir_opens_the_folder_browser_from_what_is_typed_and_puts_back_what_it_picked()
    {
        using var form = new TeamForm("a-team", Settings, _ => null);
        (string Title, string Typed)? opened = null;
        form.PickFolder = (title, typed) =>
        {
            opened = (title, typed);
            return "~/code/elsewhere";
        };
        form.SetFocus();
        form.Workdir.SetFocus();

        Assert.True(form.Workdir.NewKeyDownEvent(Key.Enter));

        Assert.Equal(("Workdir for a-team", "~/code/a-team"), opened);
        Assert.Equal("~/code/elsewhere", form.Workdir.Text);
        Assert.Null(form.Saved);
    }

    [Fact]
    public void Esc_in_the_folder_browser_leaves_Workdir_as_it_was()
    {
        using var form = new TeamForm("a-team", Settings, _ => null) { PickFolder = (_, _) => null };

        form.PickWorkdir();

        Assert.Equal("~/code/a-team", form.Workdir.Text);
    }

    [Fact]
    public void Enter_on_Vision_opens_the_repo_s_Markdown_files_and_puts_back_the_one_picked()
    {
        using var form = new TeamForm("a-team", Settings, _ => null);
        string? asked = null;
        form.FindRepoFiles = repo =>
        {
            asked = repo;
            return Task.FromResult(Files);
        };
        (string Title, RepoFiles Files)? opened = null;
        form.PickRepoFile = (title, files) =>
        {
            opened = (title, files);
            return "docs/guide/index.md";
        };
        form.SetFocus();
        form.Vision.SetFocus();

        Assert.True(form.Vision.NewKeyDownEvent(Key.Enter));

        Assert.Equal("mentaldesk/a-team", asked);
        Assert.Equal(("Vision for a-team", Files), opened);
        Assert.Equal("docs/guide/index.md", form.Vision.Text);
        Assert.Equal(TeamForm.VisionCaption, form.Hints.Message.Says);
    }

    [Fact]
    public void A_new_path_picked_says_in_the_caption_who_will_draft_it()
    {
        using var form = new TeamForm("a-team", Settings with { Customer = true, Docs = "" }, _ => null)
        {
            FindRepoFiles = _ => Task.FromResult(Files),
            PickRepoFile = (_, _) => "docs/user-guide.md",
        };
        form.SetFocus();
        form.UserDocs.Value = DocsChoice.Existing;

        form.PickDocs();
        Assert.Equal("docs/user-guide.md isn't in mentaldesk/a-team yet: the Customer lead will draft the docs for you to approve.",
            form.Hints.Message.Says);

        form.PickRepoFile = (_, _) => "VISION.md";
        form.PickVision();
        Assert.Equal("VISION.md isn't in mentaldesk/a-team yet: the Lead will draft it for you to approve.", form.Hints.Message.Says);

        form.Try.SetFocus();
        form.Vision.SetFocus();
        Assert.Equal(TeamForm.VisionMissing("VISION.md", "mentaldesk/a-team"), form.Hints.Message.Says);
    }

    [Fact]
    public void The_repo_s_files_are_read_once_and_again_only_after_a_failure()
    {
        using var form = new TeamForm("a-team", Settings, _ => null) { PickRepoFile = (_, _) => null };
        var reads = 0;
        form.FindRepoFiles = repo =>
        {
            reads++;
            return Task.FromResult(reads == 1 ? RepoFiles.Unread(repo, "offline") : Files);
        };

        form.PickVision();
        form.PickDocs();
        form.PickVision();

        Assert.Equal(2, reads);
    }

    [Fact]
    public void A_failed_read_reaches_the_picker_as_a_line_saying_so()
    {
        using var form = new TeamForm("a-team", Settings, _ => null)
        {
            FindRepoFiles = _ => Task.FromException<RepoFiles>(new InvalidOperationException("gh isn't installed")),
        };
        RepoFiles? shown = null;
        form.PickRepoFile = (_, files) =>
        {
            shown = files;
            return null;
        };

        form.PickVision();

        Assert.Equal("Couldn't read mentaldesk/a-team: gh isn't installed", shown?.Failure);
        Assert.Equal("docs/vision.md", form.Vision.Text);
    }

    [Fact]
    public void A_new_team_s_Workdir_follows_its_Name_until_a_folder_is_picked()
    {
        using var form = TeamForm.New(Blank, [], "~/.config/a-team/teams", (_, _) => null);
        form.NameField!.Text = "fretty";
        Assert.Equal("~/code/fretty", form.Workdir.Text);

        form.PickFolder = (_, _) => "~/code/fretty";
        form.PickWorkdir();
        form.NameField.Text = "frets";

        Assert.Equal("~/code/fretty", form.Workdir.Text);
    }

    [Fact]
    public void A_Workdir_with_no_main_checkout_shows_under_Problems_as_check_words_it()
    {
        using var form = new TeamForm("a-team", Settings with { Checkout = null }, _ => null) { Home = _home };
        form.Watch(Task.FromResult(new TeamHealth([new TeamProblem("checkout", "~/code/a-team/main isn't there: old news")])));

        form.Workdir.Text = "~/code/empty";
        Assert.Equal(["checkout  ~/code/empty/main isn't there: gh repo clone mentaldesk/a-team ~/code/empty/main"], ProblemRows(form));

        form.Workdir.Text = "~/code/nowhere";
        Assert.Equal(["workdir   ~/code/nowhere isn't there, so the agents would have nothing to work in"], ProblemRows(form));

        form.Workdir.Text = "~/code/a-team/";
        Assert.Empty(ProblemRows(form));
        Assert.False(form.Problems.Visible);
    }

    [Fact]
    public void A_Vision_path_not_in_the_repo_is_not_a_problem()
    {
        using var form = new TeamForm("a-team", Settings, _ => null)
        {
            Home = _home, FindRepoFiles = _ => Task.FromResult(Files), PickRepoFile = (_, _) => "VISION.md",
        };
        form.Workdir.Text = "~/code/a-team";

        form.PickVision();

        Assert.Empty(ProblemRows(form));
    }

    private static IReadOnlyList<string> ProblemRows(TeamForm form) =>
        [.. Enumerable.Range(0, form.Problems.Source?.Count ?? 0).Select(i => form.Problems.Source!.ToList()[i]?.ToString()?.TrimEnd() ?? "")];
}
