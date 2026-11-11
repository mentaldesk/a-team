using System.Text.RegularExpressions;

namespace ATeam.Dashboard;

/// <summary>A screenshot the Mac saved to a file, which <c>Ctrl+U</c> offers while it's recent.</summary>
public sealed partial record Screenshot(string Path, DateTimeOffset Taken)
{
    internal static readonly TimeSpan Recent = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(2);

    [GeneratedRegex(@"^(Screenshot|Screen Shot) \d{4}-\d{2}-\d{2} at ")]
    private static partial Regex MacName();

    /// <summary>The newest screenshot you took in the last 10 minutes, on macOS; null anywhere else.</summary>
    public static Screenshot? Latest()
    {
        if (!OperatingSystem.IsMacOS())
            return null;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Find(Folder(Location(), home), DateTimeOffset.UtcNow);
    }

    /// <summary>Where the Mac saves screenshots: the folder set for them, or the Desktop.</summary>
    internal static string Folder(string? setting, string home) =>
        setting is { Length: > 0 } && Directory.Exists(TeamSettings.Expand(setting.Trim(), home))
            ? TeamSettings.Expand(setting.Trim(), home)
            : System.IO.Path.Combine(home, "Desktop");

    internal static Screenshot? Find(string folder, DateTimeOffset now)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFiles()
                .Where(file => MacName().IsMatch(file.Name) && ImageFiles.IsImage(file.Name))
                .Select(file => new Screenshot(file.FullName, file.LastWriteTimeUtc))
                .Where(shot => now - shot.Taken <= Recent)
                .MaxBy(shot => shot.Taken);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal string Said(DateTimeOffset now) => (int)(now - Taken).TotalMinutes switch
    {
        <= 0 => "Your latest screenshot, taken just now",
        1 => "Your latest screenshot, taken 1 minute ago",
        var minutes => $"Your latest screenshot, taken {minutes} minutes ago",
    };

    private static string? Location()
    {
        try
        {
            var read = new TeamCommand("defaults").Read("read", "com.apple.screencapture", "location");
            return read.Wait(Patience) && read.Result.Failure is null ? read.Result.Output.Trim() : null;
        }
        catch (AggregateException)
        {
            return null;
        }
    }
}
