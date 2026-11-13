using MentalDesk.Tui.Paths;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

public sealed class AttachPickerTests : IDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("a-team-attach").FullName;
    private string? _setting;
    private Screenshot? _latest;

    public AttachPickerTests()
    {
        foreach (var folder in new[] { "Desktop", "Pictures/Shots", "notes/old" })
            Directory.CreateDirectory(At(folder));
        foreach (var file in new[] { "Desktop/b.png", "Desktop/a.md", "notes/old/diagram.png", "notes/plan.md" })
            File.WriteAllText(At(file), "");
    }

    public void Dispose() => Directory.Delete(_home, recursive: true);

    private string At(string relative) => Path.Combine(_home, relative);

    private AttachPicker Picker() => new(_home, () => _setting, _ => _latest);

    [Fact]
    public void It_is_titled_Attach_image_file_and_lists_a_way_up_and_folders_first_then_files_by_name()
    {
        Directory.CreateDirectory(At("Desktop/zed"));
        using var picker = Picker().Create();

        Assert.Equal("Attach image file", picker.Title);
        Assert.Equal("~/Desktop", picker.Where);
        Assert.Equal(["../", "zed/", "a.md", "b.png"], picker.Labels);
    }

    [Fact]
    public void It_starts_in_the_folder_set_for_screenshots_else_the_Desktop_else_home()
    {
        _setting = "~/Pictures/Shots";
        Assert.Equal(At("Pictures/Shots"), Picker().Start(_setting));
        Assert.Equal(At("Desktop"), Picker().Start("~/Gone"));
        Assert.Equal(At("Desktop"), Picker().Start(null));

        Directory.Delete(At("Desktop"), recursive: true);
        Assert.Equal(_home, Picker().Start(null));
    }

    [Fact]
    public void After_a_pick_the_next_one_opens_in_that_file_s_folder()
    {
        _setting = _home;
        var attach = Picker();
        using (var first = attach.Create())
        {
            first.FilterField.Text = "diagram";
            first.Choose(first.Shown.Single());
            Assert.Equal("~/notes/old/diagram.png", attach.Picked(first));
        }

        using var next = attach.Create();

        Assert.Equal("~/notes/old", next.Where);
    }

    [Fact]
    public void A_cancelled_pick_leaves_where_the_next_one_opens_alone()
    {
        var attach = Picker();
        using (var first = attach.Create())
        {
            Assert.True(first.NewKeyDownEvent(Key.Esc));
            Assert.Null(attach.Picked(first));
        }

        Assert.Equal(At("Desktop"), attach.Start(null));
    }

    [Fact]
    public void A_last_folder_that_has_gone_is_skipped()
    {
        _setting = _home;
        var attach = Picker();
        using (var first = attach.Create())
        {
            first.FilterField.Text = "diagram";
            first.Choose(first.Shown.Single());
            attach.Picked(first);
        }
        Directory.Delete(At("notes/old"), recursive: true);

        Assert.Equal(At("Desktop"), attach.Start(null));
    }

    [Fact]
    public void A_recent_screenshot_opens_its_folder_with_it_selected_even_after_a_pick_elsewhere()
    {
        var shot = At("Pictures/Shots/Screenshot 2026-11-14 at 10.00.00.png");
        File.WriteAllText(shot, "");
        File.WriteAllText(At("Pictures/Shots/another.png"), "");
        _setting = _home;
        var attach = Picker();
        using (var first = attach.Create())
        {
            first.FilterField.Text = "diagram";
            first.Choose(first.Shown.Single());
            attach.Picked(first);
        }
        _latest = new Screenshot(shot, DateTimeOffset.UtcNow);

        using var picker = attach.Create();

        Assert.Equal("~/Pictures/Shots", picker.Where);
        Assert.Equal(shot, picker.Selected!.FullPath);
        Assert.True(picker.NewKeyDownEvent(Key.Enter));
        Assert.Equal("~/Pictures/Shots/Screenshot 2026-11-14 at 10.00.00.png", attach.Picked(picker));
    }

    [Fact]
    public void With_no_recent_screenshot_nothing_but_the_way_up_is_selected()
    {
        using var picker = Picker().Create();

        Assert.Equal(PathRowKind.Up, picker.Selected!.Kind);
    }

    [Fact]
    public void Enter_on_a_folder_goes_into_it_and_Backspace_goes_up()
    {
        _setting = _home;
        using var picker = Picker().Create();
        picker.FilterField.Text = "old";
        Assert.True(picker.NewKeyDownEvent(Key.Enter));
        Assert.Equal("~/notes/old", picker.Where);

        picker.FilterField.SetFocus();
        Assert.True(picker.NewKeyDownEvent(Key.Backspace));

        Assert.Equal("~/notes", picker.Where);
    }

    [Fact]
    public void Typing_searches_this_folder_and_every_one_below_it()
    {
        _setting = _home;
        using var picker = Picker().Create();

        picker.FilterField.Text = "diagram";

        Assert.Equal(["notes/old/diagram.png"], picker.Labels);
    }

    [Fact]
    public void A_typed_path_from_home_picks_that_file()
    {
        var attach = Picker();
        using var picker = attach.Create();

        picker.FilterField.Text = "~/notes/old/diagram.png";
        Assert.True(picker.NewKeyDownEvent(Key.Enter));

        Assert.Equal("~/notes/old/diagram.png", attach.Picked(picker));
    }
}
