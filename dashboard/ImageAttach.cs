using MentalDesk.Tui.Fields;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>Attaching images where you write: <paramref name="Upload"/> stores a file on a team's repo and prints its
/// link, and <paramref name="PickFile"/> asks for the path of one, on <paramref name="Key"/>.</summary>
public sealed record Attaching(Key Key, Func<string, string, Task<Reading>> Upload, IClipboardImage Clipboard, Func<string?> PickFile);

/// <summary>Attaching images to what you're writing: a line goes in at the cursor while the image uploads, and
/// becomes its link once it's up. <paramref name="upload"/> stores a file and prints its link.</summary>
public sealed class ImageAttach(
    MultiLineField field, Func<string, Task<Reading>> upload, IClipboardImage clipboard, Action<string, Schemes> say,
    string? home = null, TimeProvider? clock = null)
{
    internal const string OverSsh = "Over SSH the clipboard is your own machine's: drop the image file here to attach it";
    private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(100);

    private readonly string _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private int _uploading;

    /// <summary>Whether an image is still on its way up, so what you wrote doesn't have its link yet.</summary>
    public bool Uploading => _uploading > 0;

    /// <summary>Ctrl+V attaches the clipboard's image where it holds one; otherwise its text pastes as ever.</summary>
    public bool Pressed(Key key) => key == Key.V.WithCtrl && PasteImage(sayOverSsh: Empty(field.App?.Clipboard));

    /// <summary>A paste the terminal delivered: a dropped image file attaches; an empty one is the terminal's paste
    /// of an image it couldn't pass on. True where it's taken care of, so the text doesn't go in.</summary>
    public bool Paste(string text)
    {
        if (ImageFiles.Dropped(text, _home) is { } path)
        {
            Attach(path);
            return true;
        }
        if (text.Trim().Length > 0)
            return false;
        PasteImage(sayOverSsh: true);
        return true;
    }

    /// <summary>Attaches the image at <paramref name="path"/>, or says why it can't.</summary>
    public void Attach(string path)
    {
        path = TeamSettings.Expand(path.Trim(), _home);
        var name = Path.GetFileName(path);
        if (!File.Exists(path))
            Failed($"there's no file {path}");
        else if (!ImageFiles.IsImage(path))
            Failed($"{name} isn't a {ImageFiles.Kinds}");
        else
            Attach(path, name, temporary: false);
    }

    private bool PasteImage(bool sayOverSsh)
    {
        if (clipboard.Remote)
        {
            if (sayOverSsh)
                say(OverSsh, Schemes.Accent);
            return false;
        }
        var file = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():N}.png");
        if (!clipboard.Save(file))
        {
            File.Delete(file);
            return false;
        }
        Attach(file, ImageFiles.ScreenshotName(_clock.GetLocalNow()), temporary: true);
        return true;
    }

    private void Attach(string path, string name, bool temporary)
    {
        var bytes = new FileInfo(path).Length;
        if (bytes > ImageFiles.MaxBytes)
        {
            Failed($"{name} is {ImageFiles.Size(bytes)}, over the 10 MB GitHub takes");
            if (temporary)
                File.Delete(path);
            return;
        }
        var line = ImageFiles.Uploading(name);
        if (field.HasSelection)
            field.ReplaceSelection(line);
        else
            field.Document!.Insert(field.CaretOffset, line);
        _uploading++;
        say($"Attaching {name}…", Schemes.Accent);
        var uploading = upload(path);
        if (uploading.IsCompleted)
        {
            Settle(uploading, path, name, line, bytes, temporary);
            return;
        }
        // Polled rather than Invoked, as the reader's comment is: Invoke can wait on a timer's lock.
        field.App?.AddTimeout(PollEvery, () =>
        {
            if (!uploading.IsCompleted)
                return true;
            Settle(uploading, path, name, line, bytes, temporary);
            return false;
        });
    }

    /// <summary>The line becomes the image's link, or goes again where the upload failed; anything else you've
    /// written since stays as it is.</summary>
    private void Settle(Task<Reading> done, string path, string name, string line, long bytes, bool temporary)
    {
        _uploading--;
        if (temporary)
            File.Delete(path);
        var reading = done.Status == TaskStatus.RanToCompletion ? done.Result : null;
        var link = reading?.Output.Trim() ?? "";
        var failure = reading is null ? done.Exception?.GetBaseException().Message ?? "the upload didn't finish"
            : reading.Failure ?? (link.Length == 0 ? "no link came back" : null);
        var at = field.Text.IndexOf(line, StringComparison.Ordinal);
        if (at >= 0)
            field.Document!.Replace(at, line.Length, failure is null ? ImageFiles.Linked(name, link) : "");
        if (failure is null)
            say($"Attached {name}  •  {ImageFiles.Size(bytes)}", Schemes.Accent);
        else
            Failed(failure);
    }

    private void Failed(string why) => say($"Attach failed: {why}", Schemes.Error);

    private static bool Empty(IClipboard? text) => text is null || !text.TryGetClipboardData(out var said) || string.IsNullOrEmpty(said);
}
