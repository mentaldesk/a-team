using MentalDesk.Tui.Paths;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

public sealed class ImagePickerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 21, 42, 12, TimeSpan.Zero);
    private readonly string _home = Directory.CreateTempSubdirectory("a-team-images").FullName;

    public void Dispose() => Directory.Delete(_home, recursive: true);

    private string At(string relative) => Path.Combine(_home, relative);

    private string Folder(string relative) => Directory.CreateDirectory(At(relative)).FullName;

    private string Write(string relative, TimeSpan? ago = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(At(relative))!);
        File.WriteAllBytes(At(relative), new byte[10]);
        File.SetLastWriteTimeUtc(At(relative), (Now - (ago ?? TimeSpan.FromHours(1))).UtcDateTime);
        return At(relative);
    }

    private ImagePicker Picker() => new(_home, new FixedClock(Now));

    [Fact]
    public void It_starts_in_the_last_folder_picked_then_the_screenshots_folder_then_the_Desktop_then_home()
    {
        var last = Folder("work");
        var shots = Folder("Pictures/Shots");
        var desktop = Folder("Desktop");

        Assert.Equal(last, ImagePicker.Start(last, "~/Pictures/Shots", _home));
        Assert.Equal(shots, ImagePicker.Start(null, "~/Pictures/Shots", _home));
        Assert.Equal(desktop, ImagePicker.Start(null, null, _home));
        Directory.Delete(desktop);
        Assert.Equal(_home, ImagePicker.Start(null, null, _home));
    }

    [Fact]
    public void It_skips_a_last_folder_or_screenshots_folder_that_is_gone()
    {
        var desktop = Folder("Desktop");

        Assert.Equal(desktop, ImagePicker.Start(At("gone"), "~/Gone", _home));
    }

    [Fact]
    public void It_is_titled_Attach_image_file_and_lists_a_way_up_and_folders_first_then_files_by_name()
    {
        Folder("Desktop/b-folder");
        Write("Desktop/a.png");
        Write("Desktop/c.md");

        using var picker = Picker().Create(null);

        Assert.Equal("Attach image file", picker.Title);
        Assert.Equal("~/Desktop", picker.Where);
        Assert.Equal(["../", "b-folder/", "a.png", "c.md"], picker.Labels);
    }

    [Fact]
    public void A_screenshot_from_the_last_10_minutes_is_selected_in_the_screenshots_folder_so_Enter_picks_it()
    {
        Folder("work");
        Write("Pictures/Shots/notes.md");
        var shot = Write("Pictures/Shots/Screenshot 2026-11-11 at 21.40.12.png", TimeSpan.FromMinutes(2));
        var images = Picker();
        Remember(images, At("work/old.png"));

        using var picker = images.Create("~/Pictures/Shots");

        Assert.Equal("~/Pictures/Shots", picker.Where);
        Assert.Equal(shot, picker.Selected?.FullPath);
        Assert.True(picker.NewKeyDownEvent(Key.Enter));
        Assert.Equal("~/Pictures/Shots/Screenshot 2026-11-11 at 21.40.12.png", images.Picked(picker));
    }

    [Fact]
    public void An_older_screenshot_is_not_selected()
    {
        Write("Desktop/Screenshot 2026-11-11 at 21.30.00.png", TimeSpan.FromMinutes(11));

        using var picker = Picker().Create(null);

        Assert.Equal("~/Desktop", picker.Where);
        Assert.Equal("../", picker.Selected?.Label);
    }

    [Fact]
    public void The_next_one_opens_in_the_folder_of_the_file_picked_whether_or_not_it_is_an_image()
    {
        Folder("Desktop");
        var images = Picker();

        Assert.Equal("~/work/notes.md", Remember(images, Write("work/notes.md")));
        using var next = images.Create(null);

        Assert.Equal("~/work", next.Where);
    }

    [Fact]
    public void Cancelling_keeps_the_folder_it_would_have_opened_in()
    {
        Folder("Desktop");
        var images = Picker();
        Remember(images, Write("work/a.png"));

        using (var cancelled = images.Create(null))
        {
            Assert.True(cancelled.NewKeyDownEvent(Key.Esc));
            Assert.Null(images.Picked(cancelled));
        }
        using var next = images.Create(null);

        Assert.Equal("~/work", next.Where);
    }

    [Fact]
    public void Enter_on_a_folder_goes_into_it_and_Backspace_with_nothing_typed_goes_up()
    {
        Folder("Desktop/inner");
        using var picker = Picker().Create(null);

        picker.SelectedIndex = 1;
        Assert.True(picker.NewKeyDownEvent(Key.Enter));
        Assert.Equal("~/Desktop/inner", picker.Where);
        Assert.True(picker.NewKeyDownEvent(Key.Backspace));
        Assert.Equal("~/Desktop", picker.Where);
    }

    [Fact]
    public void Typing_searches_the_folders_below_and_a_typed_path_picks_that_file()
    {
        Folder("Desktop");
        Write("Desktop/deep/down/diagram.png");
        var elsewhere = Write("elsewhere/x.png");
        var images = Picker();
        using var picker = images.Create(null);

        picker.FilterField.Text = "diagram";
        Assert.Equal(["deep/down/diagram.png"], picker.Labels);
        picker.FilterField.Text = elsewhere;
        Assert.True(picker.NewKeyDownEvent(Key.Enter));

        Assert.Equal("~/elsewhere/x.png", images.Picked(picker));
    }

    private static string? Remember(ImagePicker images, string file)
    {
        using var picker = images.Create(null);
        picker.FilterField.Text = file;
        picker.NewKeyDownEvent(Key.Enter);
        return images.Picked(picker);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
