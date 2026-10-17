using MentalDesk.Tui.Theming;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard.Tests;

public sealed class FolderPickerTests : IDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("a-team-home").FullName;

    public FolderPickerTests()
    {
        foreach (var folder in new[] { "code/a-team/main", "code/TuiCode", "code/.hidden", "notes" })
            Directory.CreateDirectory(Path.Combine(_home, folder));
    }

    public void Dispose() => Directory.Delete(_home, recursive: true);

    private string At(string relative) => Path.Combine(_home, relative);

    [Theory]
    [InlineData("~/code/a-team", "code/a-team")]
    [InlineData("~/code/a-team/nowhere/deeper", "code/a-team")]
    [InlineData("", "")]
    public void It_starts_at_the_folder_typed_or_its_nearest_parent_that_is_there_or_home(string typed, string expected) =>
        Assert.Equal(Path.TrimEndingDirectorySeparator(At(expected)), FolderPicker.Start(typed, _home));

    [Fact]
    public void A_folder_with_no_parent_but_the_root_starts_at_home() =>
        Assert.Equal(_home, FolderPicker.Start($"/no-such-{Guid.NewGuid():N}/deeper", _home));

    [Fact]
    public void It_shows_where_it_is_above_the_filter_and_lists_a_way_up_then_the_folders_in_it_hidden_ones_left_out()
    {
        using var picker = new FolderPicker("Workdir for a-team", At("code"), _home);

        Assert.Equal("Workdir for a-team", picker.Title);
        Assert.Equal("~/code", picker.Where);
        Assert.Equal(Pos.Absolute(1).ToString(), picker.FilterField.Y.ToString());
        Assert.Equal([FolderPicker.ParentRow, "a-team", "TuiCode"], picker.Labels);
        Assert.Equal(" Ctrl+Enter Use this folder ", picker.UseButton.Text);
    }

    [Fact]
    public void Its_buttons_are_Open_then_Use_this_folder_the_Primary_then_Cancel()
    {
        using var picker = new FolderPicker("Workdir", At("code"), _home);

        Assert.Equal([" Enter Open ", " Ctrl+Enter Use this folder ", " Esc Cancel "],
            new[] { picker.PickButton, picker.UseButton, picker.CancelButton }.Select(button => button.Text));
        Assert.Equal(SchemeNames.ButtonSecondary, picker.PickButton.SchemeName);
        Assert.Equal(SchemeNames.ButtonPrimary, picker.UseButton.SchemeName);
    }

    [Fact]
    public void F1_lists_its_keys_under_its_title_Up_only_while_the_filter_is_empty()
    {
        using var picker = new FolderPicker("Workdir for a-team", At("code"), _home);

        var sheet = DialogTests.F1(picker);

        Assert.Equal("Workdir for a-team", sheet.Here!.Heading);
        Assert.Equal(
            [("Enter", "Open"), ("↑", "Previous"), ("↓", "Next"), ("PgUp", "Page up"), ("PgDn", "Page down"),
                ("Ctrl+Enter", "Use this folder"), ("Backspace", "Up"), ("Esc", "Cancel")],
            DialogTests.Rows(sheet));
        picker.FilterField.Text = "a";
        Assert.DoesNotContain(("Backspace", "Up"), DialogTests.Rows(DialogTests.F1(picker)));
    }

    [Fact]
    public void Typing_narrows_the_folders()
    {
        using var picker = new FolderPicker("Workdir", At("code"), _home);

        picker.FilterField.Text = "tc";

        Assert.Equal(["TuiCode"], picker.Labels);
    }

    [Fact]
    public void Enter_goes_into_the_selected_folder()
    {
        using var picker = new FolderPicker("Workdir", At("code"), _home);
        picker.FilterField.Text = "tui";

        Assert.True(picker.NewKeyDownEvent(Key.Enter));

        Assert.Equal(At("code/TuiCode"), picker.Next);
        Assert.Null(picker.Picked);
    }

    [Fact]
    public void Enter_on_the_parent_row_goes_up_a_level()
    {
        using var picker = new FolderPicker("Workdir", At("code/a-team"), _home);

        Assert.True(picker.NewKeyDownEvent(Key.Enter));

        Assert.Equal(At("code"), picker.Next);
    }

    [Fact]
    public void The_root_has_no_parent_row()
    {
        using var picker = new FolderPicker("Workdir", Path.GetPathRoot(_home)!, _home);

        Assert.DoesNotContain(FolderPicker.ParentRow, picker.Labels);
    }

    [Fact]
    public void Backspace_in_an_empty_filter_goes_up_a_level_and_otherwise_deletes()
    {
        using var picker = new FolderPicker("Workdir", At("code/a-team"), _home);
        picker.FilterField.SetFocus();
        picker.FilterField.Text = "m";
        picker.FilterField.InsertionPoint = 1;

        Assert.True(picker.NewKeyDownEvent(Key.Backspace));
        Assert.Equal("", picker.FilterField.Text);
        Assert.Null(picker.Next);

        Assert.True(picker.NewKeyDownEvent(Key.Backspace));
        Assert.Equal(At("code"), picker.Next);
    }

    [Fact]
    public void Ctrl_Enter_uses_the_folder_it_is_in_written_with_a_tilde()
    {
        using var picker = new FolderPicker("Workdir", At("code/a-team"), _home);

        Assert.True(picker.NewKeyDownEvent(Key.Enter.WithCtrl));

        Assert.Equal("~/code/a-team", picker.Picked);
    }

    [Fact]
    public void Esc_picks_nothing()
    {
        using var picker = new FolderPicker("Workdir", At("code"), _home);

        Assert.True(picker.NewKeyDownEvent(Key.Esc));

        Assert.Null(picker.Picked);
        Assert.Null(picker.Next);
    }

    [Theory]
    [InlineData("/Users/me", "/Users/me", "~")]
    [InlineData("/Users/me/code/x", "/Users/me", "~/code/x")]
    [InlineData("/Users/meadow/x", "/Users/me", "/Users/meadow/x")]
    [InlineData("/opt/work", "/Users/me", "/opt/work")]
    public void Paths_under_home_are_written_with_a_tilde(string path, string home, string expected) =>
        Assert.Equal(expected, FolderPicker.Tilde(path, home));
}
