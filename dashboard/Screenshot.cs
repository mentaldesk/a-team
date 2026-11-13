using System.Text.RegularExpressions;

namespace ATeam.Dashboard;

/// <summary>A screenshot the Mac saved to a file, which <c>Ctrl+U</c> offers while it's recent.</summary>
public sealed partial record Screenshot(string Path, DateTimeOffset Taken)
{
    internal static readonly TimeSpan Recent = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(2);

    [GeneratedRegex(@"^(Screenshot|Screen Shot) \d{4}-\d{2}-\d{2} at ")]
    private static partial Regex MacName();

    /// <summary>The newest screenshot you took in the last 10 minutes in the folder <paramref name="setting"/> names, on
    /// macOS; null anywhere else.</summary>
    public static Screenshot? Latest(string? setting, string home) =>
        OperatingSystem.IsMacOS() ? Find(Folder(setting, home), DateTimeOffset.UtcNow) : null;

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

    /// <summary>The folder set for screenshots, on macOS; null where none is set.</summary>
    public static string? Setting()
    {
        if (!OperatingSystem.IsMacOS())
            return null;
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
