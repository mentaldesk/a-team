using System.Drawing;
using Terminal.Gui.Input;
using Color = Terminal.Gui.Drawing.Color;

namespace ATeam.Dashboard.Tests;

/// <summary>Attached images drawn as pictures where the terminal can, and left as their text line where it can't.</summary>
public class PicturesTests
{
    private const string Attached = "https://github.com/mentaldesk/a-team/blob/a-team/attachments/2026-10/0a1b2c.png?raw=true";
    private const string RedPng = "iVBORw0KGgoAAAANSUhEUgAAAAQAAAACCAYAAAB/qH1jAAAAEklEQVR4nGP4z8DwHxkzoAsAAA8hD/EEN8afAAAAAElFTkSuQmCC";

    private static readonly WaitingItem Item =
        new(180, "The dashboard tells me a pitch needs me", "Ready", "https://github.com/x/180", "a-team", "you", "");

    /// <summary>Reads every image as the 4×2 red PNG, counting what it was asked for.</summary>
    private sealed class Fetcher
    {
        public List<string> Asked { get; } = [];

        public Task<bool> Fetch(string path, string file)
        {
            Asked.Add(path);
            File.WriteAllBytes(file, Convert.FromBase64String(RedPng));
            return Task.FromResult(true);
        }
    }

    private static Pictures Drawing(bool graphics, Fetcher? fetcher = null) =>
        new(() => graphics, () => new Size(1, 1), (fetcher ?? new Fetcher()).Fetch);

    private static async Task<Pictures> Read(Pictures pictures, string url)
    {
        pictures.For(url);
        while (pictures.Reading)
            await Task.Delay(10);
        return pictures;
    }

    [Theory]
    [InlineData("![Screenshot 2026-10-13 at 07.02](https://x/y.png)", "Screenshot 2026-10-13 at 07.02", "https://x/y.png")]
    [InlineData("  ![shot](uploading…)  ", "shot", "uploading…")]
    public void An_image_alone_on_its_line_is_an_image_line(string line, string alt, string url) =>
        Assert.Equal(new ImageLink(alt, url), Pictures.Link(line));

    [Theory]
    [InlineData("See ![shot](https://x/y.png) here")]
    [InlineData("[shot](https://x/y.png)")]
    public void An_image_inside_prose_or_a_plain_link_is_not(string line) => Assert.Null(Pictures.Link(line));

    [Theory]
    [InlineData(Attached, "repos/mentaldesk/a-team/contents/2026-10/0a1b2c.png?ref=a-team%2Fattachments")]
    [InlineData("https://github.com/o/r/blob/main/docs/shot.png", "repos/o/r/contents/docs/shot.png?ref=main")]
    [InlineData("https://raw.githubusercontent.com/o/r/main/shot.png", "repos/o/r/contents/shot.png?ref=main")]
    public void A_file_on_GitHub_is_read_with_your_gh_login(string url, string path) =>
        Assert.Equal(path, Pictures.ApiPath(url));

    [Theory]
    [InlineData("https://github.com/user-attachments/assets/0a1b2c")]
    [InlineData("https://example.com/shot.png")]
    [InlineData("uploading…")]
    public void An_image_your_login_can_t_read_is_never_fetched(string url)
    {
        var fetcher = new Fetcher();

        Assert.Null(Drawing(graphics: true, fetcher).For(url));
        Assert.Null(Pictures.ApiPath(url));
        Assert.Empty(fetcher.Asked);
    }

    [Fact]
    public async Task An_image_is_read_once_and_decoded_to_its_pixels()
    {
        var fetcher = new Fetcher();
        var pictures = await Read(Drawing(graphics: true, fetcher), Attached);

        var picture = pictures.For(Attached);

        Assert.NotNull(picture);
        Assert.Equal(new Size(4, 2), picture.Size);
        Assert.Equal(255, picture.Pixels[0, 0].R);
        Assert.Single(fetcher.Asked);
    }

    [Fact]
    public void A_file_that_isn_t_an_image_decodes_to_nothing()
    {
        var file = Path.GetTempFileName();
        File.WriteAllText(file, "<html>Not Found</html>");

        Assert.Null(Pictures.Decode(file));
        File.Delete(file);
    }

    [Fact]
    public void A_picture_is_drawn_over_the_pane_s_background_so_no_text_shows_through_it()
    {
        var pixels = new Color[3, 1];
        pixels[0, 0] = new(255, 0, 0, 0);
        pixels[1, 0] = new(255, 0, 0, 255);
        pixels[2, 0] = new(255, 0, 0, 128);

        var flat = new Picture(pixels).Over(new Color(0, 0, 255));

        Assert.Equal(new Color(0, 0, 255), flat[0, 0]);
        Assert.Equal(new Color(255, 0, 0), flat[1, 0]);
        Assert.Equal(new Color(128, 0, 127), flat[2, 0]);
        Assert.All(flat.Cast<Color>(), pixel => Assert.Equal(255, pixel.A));
    }

    [Fact]
    public void A_picture_is_as_many_rows_as_its_shape_needs_at_the_pane_s_width()
    {
        var pictures = new Pictures(() => true, () => new Size(10, 20));
        var wide = new Picture(new Color[400, 200]);

        Assert.Equal(5, pictures.Rows(wide, width: 20, most: 50));
        Assert.Equal(3, pictures.Rows(wide, width: 20, most: 3));
    }

    [Fact]
    public void An_image_line_wraps_to_its_picture_s_rows_then_an_empty_row_with_no_alt_text()
    {
        LogLine[] lines = [new("Before", LogLineKind.Prose), new("![shot](https://x/y.png)", LogLineKind.Prose)];

        var rows = LogView.Wrap(lines, 40, line => Pictures.Link(line.Text) is { } link ? 2 : null);

        Assert.Equal(
            [
                new LogRow("Before", LogLineKind.Prose),
                new LogRow("", LogLineKind.Prose, false, 1, Pictured: true),
                new LogRow("", LogLineKind.Prose, true, 1, Pictured: true),
                new LogRow("", LogLineKind.Prose, true, 1),
            ],
            rows);
    }

    private static ReaderDialog Reader(string body, Pictures pictures, List<string> opened, List<string> viewed, string did = "opened")
    {
        var dialog = new ReaderDialog(Item, new IssueBody(body), () => null, pictures: pictures, openUrl: Opening.Into(opened, did),
            viewImage: (_, alt) => viewed.Add(alt));
        dialog.Layout(new Size(60, 20));
        return dialog;
    }

    [Fact]
    public async Task With_graphics_the_body_draws_the_picture_and_Enter_on_it_opens_the_viewer()
    {
        var pictures = await Read(Drawing(graphics: true), Attached);
        List<string> opened = [], viewed = [];
        using var dialog = Reader($"Before\n\n![shot]({Attached})", pictures, opened, viewed);

        Assert.Contains(dialog.Body.Rows(40), row => row.Pictured);
        Assert.DoesNotContain(dialog.Body.Rows(40), row => row.Text.Contains("shot"));
        dialog.NewKeyDownEvent(Key.End.WithCtrl);
        Assert.True(dialog.NewKeyDownEvent(Key.Enter));

        Assert.Equal(["shot"], viewed);
        Assert.Empty(opened);
    }

    [Fact]
    public async Task The_PR_description_draws_its_pictures_under_their_captions_and_Enter_on_one_opens_the_viewer()
    {
        var pictures = await Read(Drawing(graphics: true), Attached);
        List<string> opened = [], viewed = [];
        var description = new Remark("dev", DateTimeOffset.UnixEpoch,
            $"## What it looks like\n\nSave is left-aligned\n\n![Save is left-aligned]({Attached})", Pr: 532, Description: true);
        using var dialog = new ReaderDialog(Item, new IssueBody("The task").With(new Conversation([description])), () => null,
            pictures: pictures, openUrl: Opening.Into(opened, "opened"), viewImage: (_, alt) => viewed.Add(alt));
        dialog.Layout(new Size(60, 20));

        var rows = dialog.Body.Rows(40).ToList();
        Assert.Single(rows, row => row.Text.Contains("Save is left-aligned"));
        Assert.InRange(rows.FindIndex(row => row.Text.Contains("Save is left-aligned")), 0, rows.FindIndex(row => row.Pictured) - 1);
        dialog.NewKeyDownEvent(Key.End.WithCtrl);
        Assert.True(dialog.NewKeyDownEvent(Key.Enter));

        Assert.Equal(["Save is left-aligned"], viewed);
        Assert.Empty(opened);
    }

    [Theory]
    [InlineData("opened")]
    [InlineData("copied")]
    [InlineData("failed")]
    public async Task Without_graphics_the_line_stays_text_and_Enter_opens_it_on_GitHub_saying_what_happened(string did)
    {
        var fetcher = new Fetcher();
        var pictures = await Read(Drawing(graphics: false, fetcher), Attached);
        List<string> opened = [], viewed = [];
        using var dialog = Reader($"Before\n\n![shot]({Attached})", pictures, opened, viewed, did);

        Assert.DoesNotContain(dialog.Body.Rows(40), row => row.Pictured);
        dialog.NewKeyDownEvent(Key.End.WithCtrl);
        Assert.True(dialog.NewKeyDownEvent(Key.Enter));

        Assert.Equal([Attached], opened);
        Assert.Empty(viewed);
        Assert.Equal(Opening.Outcome(did, Attached).Message, dialog.Message.Says);
        Assert.Equal(Opening.SchemeOf(did), dialog.Message.SchemeName);
    }

    [Fact]
    public async Task A_picture_read_after_the_view_opens_keeps_the_remark_it_opened_at_on_top()
    {
        var held = new TaskCompletionSource();
        var pictures = new Pictures(() => true, () => new Size(1, 1), async (_, file) =>
        {
            await held.Task;
            File.WriteAllBytes(file, Convert.FromBase64String(RedPng));
            return true;
        });
        LogLine[] lines = [new("Before", LogLineKind.Prose), new($"![shot]({Attached})", LogLineKind.Prose),
            .. Enumerable.Range(0, 30).Select(i => new LogLine($"Remark {i}", LogLineKind.Prose))];
        var view = new LogView { Width = 120, Height = 10, Following = false, Scrolls = true, Pictures = pictures, Lines = lines };
        view.ShowLine(20);
        view.Layout(new Size(120, 10));
        Assert.Equal(20, view.TopLine);

        held.SetResult();
        while (pictures.Reading)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        view.PicturesRead();

        Assert.Contains(view.Rows(view.Viewport.Width), row => row.Pictured);
        Assert.Equal(20, view.TopLine);
    }

    [Fact]
    public void An_image_still_uploading_stays_its_text_line()
    {
        List<string> opened = [], viewed = [];
        using var dialog = Reader("![shot](uploading…)", Drawing(graphics: true), opened, viewed);

        Assert.Equal([new LogRow("![shot](uploading…)", LogLineKind.Prose)], dialog.Body.Rows(40));
        dialog.NewKeyDownEvent(Key.End.WithCtrl);
        dialog.NewKeyDownEvent(Key.Enter);

        Assert.Empty(opened);
        Assert.Empty(viewed);
        Assert.Contains("hasn't finished uploading", dialog.Message.Text);
    }

    [Fact]
    public async Task View_image_opens_the_one_at_the_comment_s_cursor()
    {
        var pictures = await Read(Drawing(graphics: true), Attached);
        var image = new ReaderImage();
        List<string> viewed = [];
        using var dialog = new ReaderDialog(Item, new IssueBody("Body"), () => null,
            comment: new ReaderComment(new Key('c'), "comment", _ => Task.FromResult<string?>(null), Image: image),
            pictures: pictures, viewImage: (_, alt) => viewed.Add(alt));
        dialog.Layout(new Size(80, 20));
        dialog.NewKeyDownEvent(new Key('c'));
        dialog.Field.Text = $"Look:\n![shot]({Attached})";
        dialog.Field.CaretOffset = 0;

        Assert.False(image.Viewable());
        dialog.Field.CaretOffset = dialog.Field.Text.Length;
        Assert.True(image.Viewable());
        image.View();

        Assert.Equal(["shot"], viewed);
    }

    [Fact]
    public void Commands_opens_from_the_comment_so_View_image_can_be_picked()
    {
        List<Key> passed = [];
        using var dialog = new ReaderDialog(Item, new IssueBody("Body"), () => null,
            comment: new ReaderComment(new Key('c'), "comment", _ => Task.FromResult<string?>(null), Image: new ReaderImage()),
            window: key =>
            {
                passed.Add(key);
                return key == Key.E.WithCtrl;
            });
        dialog.Layout(new Size(80, 20));
        dialog.NewKeyDownEvent(new Key('c'));
        dialog.Field.Text = $"![shot]({Attached})";

        Assert.True(dialog.NewKeyDownEvent(Key.E.WithCtrl));
        Assert.Contains(Key.E.WithCtrl, passed);
        Assert.Equal($"![shot]({Attached})", dialog.Field.Text);
    }

    [Fact]
    public void Enter_in_the_comment_stays_a_newline()
    {
        var image = new ReaderImage();
        using var dialog = new ReaderDialog(Item, new IssueBody("Body"), () => null,
            comment: new ReaderComment(new Key('c'), "comment", _ => Task.FromResult<string?>(null), Image: image),
            pictures: Drawing(graphics: true));
        dialog.Layout(new Size(80, 20));
        dialog.NewKeyDownEvent(new Key('c'));
        dialog.Field.Text = $"![shot]({Attached})";
        dialog.Field.CaretOffset = dialog.Field.Text.Length;

        dialog.NewKeyDownEvent(Key.Enter);

        Assert.Equal($"![shot]({Attached})\n", dialog.Field.Text);
    }
}
