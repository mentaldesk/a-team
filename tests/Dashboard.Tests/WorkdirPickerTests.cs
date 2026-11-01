using MentalDesk.Tui.Paths;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

public sealed class WorkdirPickerTests : IDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("a-team-home").FullName;

    public WorkdirPickerTests()
    {
        foreach (var folder in new[] { "code/a-team/main", "code/TuiCode/src/Editor", "code/.hidden", "notes" })
            Directory.CreateDirectory(Path.Combine(_home, folder));
        File.WriteAllText(Path.Combine(_home, "code", "README.md"), "");
    }

    public void Dispose() => Directory.Delete(_home, recursive: true);

    private string At(string relative) => Path.Combine(_home, relative);

    [Theory]
    [InlineData("~/code/a-team", "code/a-team")]
    [InlineData("~/code/a-team/nowhere/deeper", "code/a-team")]
    [InlineData("", "")]
    public void It_starts_at_the_folder_typed_or_its_nearest_parent_that_is_there_or_home(string typed, string expected) =>
        Assert.Equal(Path.TrimEndingDirectorySeparator(At(expected)), WorkdirPicker.Start(typed, _home));

    [Fact]
    public void A_full_path_that_is_there_starts_there() =>
        Assert.Equal(At("code/TuiCode"), WorkdirPicker.Start(At("code/TuiCode"), _home));

    [Fact]
    public void A_folder_with_no_parent_but_the_root_starts_at_home() =>
        Assert.Equal(_home, WorkdirPicker.Start($"/no-such-{Guid.NewGuid():N}/deeper", _home));

    [Fact]
    public void It_names_the_folder_it_is_in_and_lists_a_way_up_then_its_folders_without_files_or_hidden_ones()
    {
        using var picker = WorkdirPicker.Create("Workdir for a-team", "~/code", _home);

        Assert.Equal("Workdir for a-team", picker.Title);
        Assert.Equal("~/code", picker.Where);
        Assert.Equal(["../", "a-team/", "TuiCode/"], picker.Labels);
    }

    [Fact]
    public void Typing_searches_below_the_folder_and_Enter_on_a_deep_match_goes_into_it_and_empties_the_filter()
    {
        using var picker = WorkdirPicker.Create("Workdir", "~/code", _home);

        picker.FilterField.Text = "editor";
        Assert.Equal(["TuiCode/src/Editor/"], picker.Labels);
        Assert.True(picker.NewKeyDownEvent(Key.Enter));

        Assert.Equal("~/code/TuiCode/src/Editor", picker.Where);
        Assert.Equal("", picker.FilterField.Text);
        Assert.Null(picker.PickedPath);
    }

    [Fact]
    public void Backspace_in_an_empty_filter_goes_up()
    {
        using var picker = WorkdirPicker.Create("Workdir", "~/code/a-team", _home);
        picker.FilterField.SetFocus();

        Assert.True(picker.NewKeyDownEvent(Key.Backspace));

        Assert.Equal("~/code", picker.Where);
    }

    [Fact]
    public void Ctrl_Enter_uses_the_folder_it_is_in_written_with_a_tilde()
    {
        using var picker = WorkdirPicker.Create("Workdir", "~/code/a-team", _home);

        Assert.True(picker.NewKeyDownEvent(Key.Enter.WithCtrl));

        Assert.Equal("~/code/a-team", picker.PickedPath);
    }

    [Fact]
    public void Esc_uses_nothing()
    {
        using var picker = WorkdirPicker.Create("Workdir", "~/code", _home);

        Assert.True(picker.NewKeyDownEvent(Key.Esc));

        Assert.Null(picker.PickedPath);
    }
}
