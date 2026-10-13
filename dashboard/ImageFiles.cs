using System.Globalization;
using System.Text.RegularExpressions;

namespace ATeam.Dashboard;

/// <summary>Which files attach as images, what a drop pasted, and how an attachment reads in what you write.</summary>
public static partial class ImageFiles
{
    /// <summary>GitHub's own limit for an image on its pages.</summary>
    public const long MaxBytes = 10L * 1024 * 1024;

    internal const string Kinds = ".png, .jpg, .gif or .webp";

    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

    public static bool IsImage(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>The image file a drop pasted, quoted, escaped or as a file URL as terminals paste one; null where the
    /// paste is anything else, a path to another kind of file included.</summary>
    public static string? Dropped(string pasted, string home)
    {
        var text = pasted.Trim();
        if (text.Length == 0 || text.Contains('\n'))
            return null;
        var path = TeamSettings.Expand(Unquote(text), home);
        return IsImage(path) && Path.IsPathRooted(path) && File.Exists(path) ? path : null;
    }

    private static string Unquote(string text)
    {
        if (text.StartsWith("file://", StringComparison.Ordinal) && Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return uri.LocalPath;
        if (text.Length > 1 && text[0] is '\'' or '"' && text[^1] == text[0])
            return text[1..^1];
        return Escaped().Replace(text, "$1");
    }

    [GeneratedRegex(@"\\(.)")]
    private static partial Regex Escaped();

    /// <summary>The name a pasted screenshot gets, as macOS names one.</summary>
    public static string ScreenshotName(DateTimeOffset at) =>
        string.Create(CultureInfo.InvariantCulture, $"Screenshot {at:yyyy-MM-dd} at {at:HH.mm.ss}.png");

    public static string Size(long bytes) =>
        bytes < 1024 ? $"{bytes} B"
        : bytes < 1024 * 1024 ? $"{bytes / 1024} KB"
        : string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024):0.#} MB");

    /// <summary>The line that holds an image's place while it uploads.</summary>
    public static string Uploading(string name) => Linked(name, "uploading…");

    public static string Linked(string name, string url) =>
        $"![{Path.GetFileNameWithoutExtension(name).Replace('[', '(').Replace(']', ')')}]({url})";
}
