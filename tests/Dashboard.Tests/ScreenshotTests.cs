namespace ATeam.Dashboard.Tests;

public sealed class ScreenshotTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 21, 42, 12, TimeSpan.Zero);
    private readonly string _home = Directory.CreateTempSubdirectory("screenshots").FullName;

    public void Dispose() => Directory.Delete(_home, recursive: true);

    private string Shot(string folder, string name, TimeSpan ago)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, new byte[10]);
        File.SetLastWriteTimeUtc(path, (Now - ago).UtcDateTime);
        return path;
    }

    [Fact]
    public void It_looks_in_the_folder_set_for_screenshots()
    {
        var set = Path.Combine(_home, "Pictures", "Shots");
        Directory.CreateDirectory(set);

        Assert.Equal(set, Screenshot.Folder("~/Pictures/Shots", _home));
        Assert.Equal(set, Screenshot.Folder(set + "\n", _home));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("~/Gone")]
    public void With_no_folder_set_it_looks_on_the_Desktop(string? setting)
    {
        Assert.Equal(Path.Combine(_home, "Desktop"), Screenshot.Folder(setting, _home));
    }

    [Fact]
    public void The_newest_screenshot_of_the_last_10_minutes_wins()
    {
        var desktop = Path.Combine(_home, "Desktop");
        Shot(desktop, "Screenshot 2026-11-11 at 21.33.00.png", TimeSpan.FromMinutes(9));
        var newest = Shot(desktop, "Screenshot 2026-11-11 at 21.40.12.png", TimeSpan.FromMinutes(2));
        Shot(desktop, "Screen Shot 2026-11-11 at 21.38.00.png", TimeSpan.FromMinutes(4));

        Assert.Equal(new Screenshot(newest, Now - TimeSpan.FromMinutes(2)), Screenshot.Find(desktop, Now));
    }

    [Fact]
    public void One_older_than_10_minutes_isn_t_offered()
    {
        var desktop = Path.Combine(_home, "Desktop");
        Shot(desktop, "Screenshot 2026-11-11 at 21.30.00.png", TimeSpan.FromMinutes(11));

        Assert.Null(Screenshot.Find(desktop, Now));
    }

    [Theory]
    [InlineData("diagram.png")]
    [InlineData("My Screenshot 2026-11-11 at 21.41.00.png")]
    [InlineData("Screenshot 2026-11-11 at 21.41.00.mov")]
    public void Only_files_the_Mac_named_as_screenshots_count(string name)
    {
        var desktop = Path.Combine(_home, "Desktop");
        Shot(desktop, name, TimeSpan.FromMinutes(1));

        Assert.Null(Screenshot.Find(desktop, Now));
    }

    [Fact]
    public void A_folder_that_isn_t_there_has_none()
    {
        Assert.Null(Screenshot.Find(Path.Combine(_home, "Desktop"), Now));
    }

    [Fact]
    public void It_shows_a_path_under_home_from_the_tilde()
    {
        Assert.Equal("~/Desktop/shot.png", Screenshot.FromHome("/Users/me/Desktop/shot.png", "/Users/me"));
        Assert.Equal("/Volumes/x/shot.png", Screenshot.FromHome("/Volumes/x/shot.png", "/Users/me"));
    }

    [Theory]
    [InlineData(20, "Your latest screenshot, taken just now")]
    [InlineData(70, "Your latest screenshot, taken 1 minute ago")]
    [InlineData(150, "Your latest screenshot, taken 2 minutes ago")]
    [InlineData(599, "Your latest screenshot, taken 9 minutes ago")]
    public void It_says_how_long_ago_you_took_it(int seconds, string said)
    {
        Assert.Equal(said, new Screenshot("shot.png", Now - TimeSpan.FromSeconds(seconds)).Said(Now));
    }
}
