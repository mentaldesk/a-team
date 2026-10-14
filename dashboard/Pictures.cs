using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using StbImageSharp;
using Color = Terminal.Gui.Drawing.Color;
using Size = System.Drawing.Size;

namespace ATeam.Dashboard;

/// <summary>An image line in what you read or write: <c>![alt](url)</c> alone on its line.</summary>
public sealed record ImageLink(string Alt, string Url);

/// <summary>An image read and decoded, ready to draw.</summary>
public sealed record Picture(Color[,] Pixels)
{
    public Size Size => new(Pixels.GetLength(0), Pixels.GetLength(1));
}

/// <summary>The pictures image lines link to, read with your own <c>gh</c> login and drawn where the terminal can.
/// <paramref name="fetch"/> saves what an API path returns to a file, and says whether it did.</summary>
public sealed partial class Pictures(Func<bool> graphics, Func<Size> cell, Func<string, string, Task<bool>>? fetch = null)
{
    /// <summary>Wider than this is scaled down as it's read: no pane is that many pixels across.</summary>
    internal const int MaxWidth = 1600;

    private readonly Func<string, string, Task<bool>> _fetch = fetch ?? Gh;
    private readonly ConcurrentDictionary<string, Task<Picture?>> _read = new();

    /// <summary>Pictures as <paramref name="app"/>'s terminal draws them: Kitty graphics where it has them, else Sixel.</summary>
    public static Pictures Of(IApplication app) => new(
        () => app.Driver is { } driver && (driver.KittyGraphicsSupport?.IsSupported == true || driver.SixelSupport?.IsSupported == true),
        () => app.Driver?.KittyGraphicsSupport is { IsSupported: true } kitty ? kitty.Resolution
            : app.Driver?.SixelSupport?.Resolution ?? new Size(10, 20));

    /// <summary>Whether the terminal draws pictures at all; where it doesn't, image lines stay as text.</summary>
    public bool Drawn => graphics();

    /// <summary>How many pixels a cell is, across and down.</summary>
    public Size Cell => cell();

    public static ImageLink? Link(string line) =>
        ImageLine().Match(line) is { Success: true } match ? new ImageLink(match.Groups[1].Value, match.Groups[2].Value) : null;

    [GeneratedRegex(@"^\s*!\[([^\]]*)\]\((\S+?)\)\s*$")]
    private static partial Regex ImageLine();

    /// <summary>The <c>gh api</c> path that reads a file a github.com or raw.githubusercontent.com link points to, or
    /// null for any other link: a host your login doesn't reach stays text. The attachments branch is the one ref
    /// with a slash it knows.</summary>
    public static string? ApiPath(string url)
    {
        if (GitHubFile().Match(url) is not { Success: true } match)
            return null;
        var (repo, rest) = ($"{match.Groups["owner"].Value}/{match.Groups["repo"].Value}", match.Groups["rest"].Value);
        var reference = rest.StartsWith("a-team/attachments/", StringComparison.Ordinal) ? "a-team/attachments" : rest.Split('/')[0];
        var path = rest[(reference.Length + 1)..];
        return path.Length == 0 ? null : $"repos/{repo}/contents/{path}?ref={Uri.EscapeDataString(reference)}";
    }

    [GeneratedRegex(@"^https://(?:github\.com/(?<owner>[\w.-]+)/(?<repo>[\w.-]+)/(?:blob|raw)|raw\.githubusercontent\.com/(?<owner>[\w.-]+)/(?<repo>[\w.-]+))/(?<rest>[^?#]+)(?:\?raw=true)?$")]
    private static partial Regex GitHubFile();

    /// <summary>The picture once it's read, or null while it's being read or where it can't be; the first ask starts
    /// reading it.</summary>
    public Picture? For(string url) => Read(url) is { IsCompletedSuccessfully: true } read ? read.Result : null;

    /// <summary>Whether a picture asked for is still on its way.</summary>
    public bool Reading => _read.Values.Any(read => !read.IsCompleted);

    /// <summary>The rows a picture takes at <paramref name="width"/> cells across, no more than <paramref name="most"/>.</summary>
    public int Rows(Picture picture, int width, int most)
    {
        var cell = Cell;
        var across = Math.Min(width * cell.Width, picture.Size.Width);
        var down = (double)across * picture.Size.Height / picture.Size.Width;
        return Math.Clamp((int)Math.Ceiling(down / cell.Height), 1, Math.Max(1, most));
    }

    private Task<Picture?>? Read(string url) =>
        ApiPath(url) is { } path ? _read.GetOrAdd(url, _ => Task.Run(() => Load(path))) : null;

    private async Task<Picture?> Load(string path)
    {
        var file = Path.Combine(Path.GetTempPath(), $"a-team-picture-{Guid.NewGuid():N}");
        try
        {
            return await _fetch(path, file) ? Decode(file) : null;
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>The file's pixels, scaled down to <see cref="MaxWidth"/>, or null where it isn't a PNG, JPEG or GIF.</summary>
    internal static Picture? Decode(string file)
    {
        ImageResult image;
        try
        {
            using var stream = File.OpenRead(file);
            image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or ArgumentException)
        {
            return null;
        }
        var step = Math.Max(1.0, (double)image.Width / MaxWidth);
        var width = (int)(image.Width / step);
        var height = Math.Max(1, (int)(image.Height / step));
        var pixels = new Color[width, height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var at = ((int)(y * step) * image.Width + (int)(x * step)) * 4;
                pixels[x, y] = new Color(image.Data[at], image.Data[at + 1], image.Data[at + 2], image.Data[at + 3]);
            }
        return new Picture(pixels);
    }

    private static async Task<bool> Gh(string path, string file)
    {
        var start = new ProcessStartInfo("gh") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "api", path, "-H", "Accept: application/vnd.github.raw" })
            start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return false;
            await using (var output = File.Create(file))
            {
                var errors = process.StandardError.ReadToEndAsync();
                await process.StandardOutput.BaseStream.CopyToAsync(output);
                await errors;
            }
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Exception e) when (e is IOException or Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
