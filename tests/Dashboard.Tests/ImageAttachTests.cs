using MentalDesk.Tui.Fields;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

/// <summary>Attaching an image where you write: each way in, the line that holds its place, and what a failure leaves.</summary>
public sealed class ImageAttachTests : IDisposable
{
    private const string Link = "https://github.com/x/y/blob/a-team/attachments/2026-10/abc.png?raw=true";
    private readonly string _folder = Directory.CreateTempSubdirectory("image-attach").FullName;
    private readonly List<string> _uploaded = [];
    private readonly List<(string Text, Schemes Scheme)> _said = [];

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Image(string name, int bytes = 2048)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private (MultiLineField Field, ImageAttach Attach) Open(
        string text = "", Clipboard? clipboard = null, Func<string, Task<Reading>>? upload = null, IClipboard? copied = null)
    {
        var field = new MultiLineField { Text = text };
        field.CaretOffset = text.Length;
        var attach = new ImageAttach(field, upload ?? (file =>
        {
            _uploaded.Add(file);
            return Task.FromResult(new Reading(Link + "\n", null));
        }), clipboard ?? new Clipboard(), (said, scheme) => _said.Add((said, scheme)), _folder, new Clock(), copied);
        return (field, attach);
    }

    [Fact]
    public void Ctrl_V_with_a_screenshot_on_the_clipboard_attaches_it_where_the_cursor_is()
    {
        var (field, attach) = Open("Cut off here: ", new Clipboard(png: 412 * 1024));

        Assert.True(attach.Pressed(Key.V.WithCtrl));

        Assert.Equal($"Cut off here: ![Screenshot 2026-10-13 at 07.02.09]({Link})", field.Text);
        Assert.Single(_uploaded);
        Assert.False(File.Exists(_uploaded[0]));
        Assert.Equal(("Attached Screenshot 2026-10-13 at 07.02.09.png  •  412 KB", Schemes.Accent), _said[^1]);
    }

    [Fact]
    public void Ctrl_V_with_text_on_the_clipboard_is_left_to_paste_the_text()
    {
        var (field, attach) = Open("as typed");

        Assert.False(attach.Pressed(Key.V.WithCtrl));
        Assert.Equal("as typed", field.Text);
        Assert.Empty(_uploaded);
    }

    [Fact]
    public void Ctrl_V_with_text_copied_along_with_a_picture_of_it_pastes_the_text()
    {
        var copied = new FakeClipboard();
        copied.TrySetClipboardData("1\t2\t3");
        var (field, attach) = Open("as typed", new Clipboard(png: 10), copied: copied);

        Assert.False(attach.Pressed(Key.V.WithCtrl));
        Assert.Equal("as typed", field.Text);
        Assert.Empty(_uploaded);
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("'{0}'")]
    [InlineData("\"{0}\"")]
    [InlineData("file://{0}")]
    public void A_dropped_image_file_attaches_instead_of_pasting_its_path(string pasted)
    {
        var path = Image("my shot.PNG");
        var (field, attach) = Open();

        Assert.True(attach.Paste(string.Format(pasted, pasted.StartsWith("file") ? path.Replace(" ", "%20") : path) + "\n"));

        Assert.Equal($"![my shot]({Link})", field.Text);
        Assert.Equal([path], _uploaded);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void A_dropped_path_with_its_spaces_escaped_attaches_too()
    {
        var path = Image("Screen Shot.jpg");
        var (_, attach) = Open();

        Assert.True(attach.Paste(path.Replace(" ", "\\ ")));
        Assert.Equal([path], _uploaded);
    }

    [Fact]
    public void A_pasted_path_to_anything_but_an_image_stays_text()
    {
        var notes = Path.Combine(_folder, "notes.txt");
        File.WriteAllText(notes, "words");
        var (_, attach) = Open();

        Assert.False(attach.Paste(notes));
        Assert.False(attach.Paste(Path.Combine(_folder, "missing.png")));
        Assert.False(attach.Paste("just some words.png"));
        Assert.Empty(_uploaded);
    }

    [Fact]
    public void An_empty_paste_is_the_terminal_s_paste_of_a_screenshot()
    {
        var (field, attach) = Open(clipboard: new Clipboard(png: 10));

        Assert.True(attach.Paste(""));
        Assert.StartsWith("![Screenshot ", field.Text);
        Assert.Single(_uploaded);
    }

    [Fact]
    public void Over_SSH_pasting_leaves_the_text_and_says_to_drop_the_file()
    {
        var (field, attach) = Open("as typed", new Clipboard(png: 10, remote: true));

        Assert.True(attach.Paste(""));
        Assert.False(attach.Pressed(Key.V.WithCtrl));

        Assert.Equal("as typed", field.Text);
        Assert.Empty(_uploaded);
        Assert.Equal((ImageAttach.OverSsh, Schemes.Accent), _said[^1]);
    }

    [Fact]
    public void A_typed_path_attaches_from_home()
    {
        var path = Image("typed.gif");
        var (field, attach) = Open();

        attach.Attach("~/typed.gif");

        Assert.Equal([path], _uploaded);
        Assert.Equal($"![typed]({Link})", field.Text);
    }

    [Fact]
    public void A_failed_upload_says_why_and_leaves_what_was_typed()
    {
        var (field, attach) = Open("Before ", upload: _ => Task.FromResult(new Reading("", "can't store shot.png")));

        attach.Attach(Image("shot.png"));

        Assert.Equal("Before ", field.Text);
        Assert.Equal(("Attach failed: can't store shot.png", Schemes.Error), _said[^1]);
    }

    [Fact]
    public void An_image_over_10_MB_fails_with_a_reason_and_isn_t_uploaded()
    {
        var (field, attach) = Open("as typed");

        attach.Attach(Image("huge.png", (int)ImageFiles.MaxBytes + 1));

        Assert.Equal("as typed", field.Text);
        Assert.Empty(_uploaded);
        Assert.Equal(("Attach failed: huge.png is 10 MB, over the 10 MB GitHub takes", Schemes.Error), _said[^1]);
    }

    [Fact]
    public void A_file_that_isn_t_an_image_fails_with_a_reason()
    {
        var notes = Path.Combine(_folder, "notes.txt");
        File.WriteAllText(notes, "words");
        var (_, attach) = Open();

        attach.Attach(notes);
        Assert.Equal(("Attach failed: notes.txt isn't a .png, .jpg, .gif or .webp", Schemes.Error), _said[^1]);
        attach.Attach(Path.Combine(_folder, "gone.png"));
        Assert.StartsWith("Attach failed: there's no file ", _said[^1].Text);
        Assert.Empty(_uploaded);
    }

    [Fact]
    public void While_it_uploads_the_line_holds_its_place_and_you_can_keep_writing()
    {
        var pending = new TaskCompletionSource<Reading>();
        var (field, attach) = Open(upload: _ => pending.Task);

        attach.Attach(Image("shot.webp"));

        Assert.True(attach.Uploading);
        Assert.Equal("![shot](uploading…)", field.Text);
        Assert.Equal(("Attaching shot.webp…", Schemes.Accent), _said[^1]);
    }

    [Fact]
    public void Sizes_read_as_the_message_bar_shows_them()
    {
        Assert.Equal("900 B", ImageFiles.Size(900));
        Assert.Equal("412 KB", ImageFiles.Size(412 * 1024 + 300));
        Assert.Equal("2.5 MB", ImageFiles.Size(5 * 512 * 1024));
    }

    internal sealed class Clipboard(int png = 0, bool remote = false) : IClipboardImage
    {
        public bool Remote => remote;

        public bool Save(string file)
        {
            if (png == 0)
                return false;
            File.WriteAllBytes(file, new byte[png]);
            return true;
        }
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 7, 2, 9, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
