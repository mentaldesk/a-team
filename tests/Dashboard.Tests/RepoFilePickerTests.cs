using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

public class RepoFilePickerTests
{
    private static readonly RepoFiles Files =
        RepoFiles.Parse("mentaldesk/a-team", "main", "README.md\ndocs/vision.md\ndocs/guide/index.md\nscripts/board.sh\ndocs/guide/teams.markdown\n");

    [Fact]
    public void Only_Markdown_files_are_listed()
    {
        Assert.Equal(["README.md", "docs/guide/index.md", "docs/guide/teams.markdown", "docs/vision.md"], Files.Paths);
        Assert.Null(Files.Failure);
    }

    [Fact]
    public void It_shows_the_repo_and_branch_above_the_filter()
    {
        using var picker = new RepoFilePicker("Vision for a-team", Files);

        Assert.Equal("Vision for a-team", picker.Title);
        Assert.Equal("4 Markdown files in mentaldesk/a-team on main", picker.Where);
        Assert.Equal(Files.Paths, picker.Labels);
    }

    [Fact]
    public void Typing_narrows_the_list_best_match_first()
    {
        using var picker = new RepoFilePicker("Docs", Files);

        picker.FilterField.Text = "in";

        Assert.Equal("docs/guide/index.md", picker.Labels[0]);
    }

    [Fact]
    public void Enter_picks_the_selected_file()
    {
        using var picker = new RepoFilePicker("Vision", Files);
        picker.FilterField.Text = "vision";

        Assert.True(picker.Route(Key.Enter));

        Assert.Equal("docs/vision.md", picker.Picked);
    }

    [Fact]
    public void Enter_on_a_path_that_matches_no_file_picks_it_as_a_new_one()
    {
        using var picker = new RepoFilePicker("Vision", Files);
        picker.FilterField.Text = "VISION_NEW.txt";
        Assert.True(picker.NoMatches);

        picker.Route(Key.Enter);

        Assert.Equal("VISION_NEW.txt", picker.Picked);
    }

    [Fact]
    public void A_repo_that_can_t_be_read_says_so_in_a_line_and_typing_still_works()
    {
        using var picker = new RepoFilePicker("Vision", RepoFiles.Unread("mentaldesk/a-tem", "Couldn't read mentaldesk/a-tem: Not Found"));

        Assert.Equal("Couldn't read mentaldesk/a-tem: Not Found", picker.Where);
        Assert.Equal(RepoFilePicker.Unreadable, picker.NoMatchesText);

        picker.FilterField.Text = "docs/vision.md";
        picker.Route(Key.Enter);
        Assert.Equal("docs/vision.md", picker.Picked);
    }

    [Fact]
    public void Esc_picks_nothing()
    {
        using var picker = new RepoFilePicker("Vision", Files);

        picker.Route(Key.Esc);

        Assert.Null(picker.Picked);
    }

    [Fact]
    public async Task No_repo_yet_is_said_without_asking_GitHub() =>
        Assert.Equal("No Repo yet, so there are no files to pick from.", (await RepoFiles.Read("")).Failure);
}
