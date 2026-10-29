using MentalDesk.Tui.Loading;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

public class RepoFilePickerTests
{
    private static readonly RepoFiles Files =
        RepoFiles.Parse("mentaldesk/a-team", "main", "README.md\ndocs/vision.md\ndocs/guide/index.md\nscripts/board.sh\ndocs/guide/teams.markdown\n");

    private static RepoFilePicker Loaded(string title = "Vision")
    {
        var picker = new RepoFilePicker(title, "mentaldesk/a-team");
        picker.Show(Files, () => { });
        return picker;
    }

    [Fact]
    public void Only_Markdown_files_are_listed()
    {
        Assert.Equal(["README.md", "docs/guide/index.md", "docs/guide/teams.markdown", "docs/vision.md"], Files.Paths);
        Assert.Null(Files.Failure);
    }

    [Fact]
    public void It_opens_loading_in_its_list_with_Pick_dimmed_and_Enter_doing_nothing()
    {
        using var picker = new RepoFilePicker("Vision for a-team", "mentaldesk/a-team");

        Assert.Equal(LoadState.Loading, picker.State);
        Assert.Equal("Markdown files in mentaldesk/a-team", picker.Where);
        Assert.False(picker.PickButton.Enabled);

        picker.FilterField.Text = "docs/vision.md";
        picker.NewKeyDownEvent(Key.Enter);

        Assert.Null(picker.Picked);
    }

    [Fact]
    public void Once_read_it_shows_the_repo_and_branch_above_the_filter_and_the_files_below()
    {
        using var picker = Loaded("Vision for a-team");

        Assert.Equal("Vision for a-team", picker.Title);
        Assert.Equal("4 Markdown files in mentaldesk/a-team on main", picker.Where);
        Assert.Equal(Files.Paths, picker.Labels);
        Assert.True(picker.PickButton.Enabled);
    }

    [Fact]
    public void Typing_narrows_the_list_best_match_first()
    {
        using var picker = Loaded("Docs");

        picker.FilterField.Text = "in";

        Assert.Equal("docs/guide/index.md", picker.Labels[0]);
    }

    [Fact]
    public void Enter_picks_the_selected_file()
    {
        using var picker = Loaded();
        picker.FilterField.Text = "vision";

        Assert.True(picker.NewKeyDownEvent(Key.Enter));

        Assert.Equal("docs/vision.md", picker.Picked);
    }

    [Fact]
    public void Enter_on_a_path_that_matches_no_file_picks_it_as_a_new_one()
    {
        using var picker = Loaded();
        picker.FilterField.Text = "VISION_NEW.txt";
        Assert.True(picker.NoMatches);

        picker.NewKeyDownEvent(Key.Enter);

        Assert.Equal("VISION_NEW.txt", picker.Picked);
    }

    [Fact]
    public void A_repo_with_no_Markdown_files_says_so_and_takes_a_typed_path()
    {
        using var picker = new RepoFilePicker("Vision", "mentaldesk/a-team");
        picker.Show(RepoFiles.Parse("mentaldesk/a-team", "main", "scripts/board.sh\n"), () => { });

        Assert.Equal(LoadState.Empty, picker.State);

        picker.FilterField.Text = "docs/vision.md";
        picker.NewKeyDownEvent(Key.Enter);
        Assert.Equal("docs/vision.md", picker.Picked);
    }

    [Fact]
    public void A_failed_read_shows_in_the_list_and_a_typed_path_is_not_taken()
    {
        using var picker = new RepoFilePicker("Vision", "mentaldesk/a-tem");

        picker.Show(RepoFiles.Unread("mentaldesk/a-tem", "Couldn't read mentaldesk/a-tem: Not Found"), () => { });

        Assert.Equal(LoadState.Failed, picker.State);
        Assert.False(picker.PickButton.Enabled);
        picker.FilterField.Text = "docs/vision.md";
        picker.NewKeyDownEvent(Key.Enter);
        Assert.Null(picker.Picked);
    }

    [Fact]
    public void R_reads_again_while_failed_and_the_files_appear_once_it_succeeds()
    {
        using var picker = new RepoFilePicker("Vision", "mentaldesk/a-team");
        var retries = 0;
        picker.Show(RepoFiles.Unread("mentaldesk/a-team", "offline"), () =>
        {
            retries++;
            picker.ShowLoading();
            picker.Show(Files, () => { });
        });
        picker.SetFocus();

        Assert.True(picker.FilterField.NewKeyDownEvent(Key.R));

        Assert.Equal(1, retries);
        Assert.Equal("", picker.FilterField.Text);
        Assert.Equal(LoadState.Loaded, picker.State);
        Assert.Equal(Files.Paths, picker.Labels);
    }

    [Fact]
    public void R_is_typed_into_the_filter_once_the_files_are_in()
    {
        using var picker = Loaded();
        picker.SetFocus();

        picker.FilterField.NewKeyDownEvent(Key.R);

        Assert.Equal("r", picker.FilterField.Text);
    }

    [Fact]
    public void Esc_picks_nothing()
    {
        using var picker = Loaded();

        picker.NewKeyDownEvent(Key.Esc);

        Assert.Null(picker.Picked);
    }

    [Fact]
    public async Task No_repo_yet_is_said_without_asking_GitHub() =>
        Assert.Equal("No Repo yet, so there are no files to pick from.", (await RepoFiles.Read("")).Failure);

    [Fact]
    public void F1_lists_picking_and_Esc_Cancel_under_its_title()
    {
        using var picker = Loaded("Vision for a-team");

        var sheet = DialogTests.F1(picker);

        Assert.Equal("Vision for a-team", sheet.Here!.Heading);
        var rows = DialogTests.Rows(sheet);
        Assert.Equal(("Enter", "Pick"), rows[0]);
        Assert.Equal(("Esc", "Cancel"), rows[^1]);
        Assert.DoesNotContain(rows, row => row.Label == "Retry");
    }

    [Fact]
    public void F1_lists_Retry_while_the_read_has_failed()
    {
        using var picker = new RepoFilePicker("Vision", "mentaldesk/a-team");
        picker.Show(RepoFiles.Unread("mentaldesk/a-team", "offline"), () => { });

        Assert.Contains(("R", "Retry"), DialogTests.Rows(DialogTests.F1(picker)));
    }
}
