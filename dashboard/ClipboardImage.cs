using System.ComponentModel;
using System.Diagnostics;

namespace ATeam.Dashboard;

/// <summary>The image on the system clipboard, which a terminal never passes through a paste.</summary>
public interface IClipboardImage
{
    /// <summary>Whether the clipboard that matters is another machine's, as over SSH, so this one's is no use.</summary>
    bool Remote { get; }

    /// <summary>Saves the clipboard's image to <paramref name="file"/> as a PNG; false where it holds none.</summary>
    bool Save(string file);
}

/// <summary>Reads the clipboard's image with what the system has: AppleScript on macOS, wl-paste or xclip on Linux.</summary>
public sealed class SystemClipboardImage(Func<string, string?> environment) : IClipboardImage
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static readonly string[] SaveAsPng =
    [
        "on run argv",
        "try",
        "set png to the clipboard as «class PNGf»",
        "on error",
        "return \"none\"",
        "end try",
        "set target to open for access (POSIX file (item 1 of argv)) with write permission",
        "set eof target to 0",
        "write png to target",
        "close access target",
        "return \"saved\"",
        "end run",
    ];

    public bool Remote => environment("SSH_CONNECTION") is { Length: > 0 } || environment("SSH_TTY") is { Length: > 0 };

    public bool Save(string file)
    {
        if (Remote)
            return false;
        if (OperatingSystem.IsMacOS())
            return Run("osascript", [.. SaveAsPng.SelectMany(line => new[] { "-e", line }), file]) == "saved" && Saved(file);
        if (!OperatingSystem.IsLinux())
            return false;
        if (environment("WAYLAND_DISPLAY") is { Length: > 0 })
            return Offers(Run("wl-paste", ["--list-types"])) && Run("wl-paste", ["--type", "image/png"], file) is not null && Saved(file);
        return Offers(Run("xclip", ["-selection", "clipboard", "-t", "TARGETS", "-o"]))
            && Run("xclip", ["-selection", "clipboard", "-t", "image/png", "-o"], file) is not null && Saved(file);
    }

    private static bool Offers(string? types) =>
        types?.Split('\n', StringSplitOptions.TrimEntries).Contains("image/png") == true;

    private static bool Saved(string file) => File.Exists(file) && new FileInfo(file).Length > 0;

    /// <summary>What the command printed, or into <paramref name="output"/> where given; null where it failed.</summary>
    private static string? Run(string command, IEnumerable<string> arguments, string? output = null)
    {
        var start = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return null;
            _ = process.StandardError.ReadToEndAsync();
            try
            {
                string printed;
                if (output is null)
                    printed = process.StandardOutput.ReadToEndAsync().WaitAsync(Patience).GetAwaiter().GetResult();
                else
                {
                    using var file = File.Create(output);
                    process.StandardOutput.BaseStream.CopyToAsync(file).WaitAsync(Patience).GetAwaiter().GetResult();
                    printed = "";
                }
                if (!process.WaitForExit(Patience))
                    throw new TimeoutException();
                return process.ExitCode == 0 ? printed.Trim() : null;
            }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
        }
        catch (Exception e) when (e is IOException or Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
