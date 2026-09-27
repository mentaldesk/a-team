using System.Drawing;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Color = Terminal.Gui.Drawing.Color;

namespace ATeam.Dashboard;

/// <summary>A run of one row's cells in a single colour.</summary>
internal readonly record struct ArtSpan(string Text, string Colour);

/// <summary>The van, driving, in place of the issue body while the team writes one rank and fetches the next
/// item: a solid body on a dotted road, in a frame of its own. <see cref="SpinnerView"/> draws a single line
/// in the view's own colour, so it can't show this.</summary>
public sealed class LoadingView : View
{
    private const string Resource = "loading.anim";
    private const string Break = "---FRAME---";
    private const string Untagged = "gray";

    private const char Dot = '·';

    private static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(120);

    // The van is a picture, not chrome: it keeps its own palette on black in every theme.
    private static readonly Dictionary<string, Attribute> Inks = new()
    {
        [Untagged] = new Attribute(StandardColor.Gray, StandardColor.Black),
        ["white"] = new Attribute(StandardColor.White, StandardColor.Black),
        ["red"] = new Attribute(StandardColor.BrightRed, StandardColor.Black),
        ["yellow"] = new Attribute(StandardColor.BrightYellow, StandardColor.Black),
    };

    private static readonly Attribute Dos = new(new Color(0x55, 0x55, 0x55), new Color(0xC0, 0xC0, 0xC0));

    // The road is the part that follows the theme, so the frame isn't a black hole in a pale one. The DOS
    // themes get grey rather than their own blue: the van is parked on a road, not in a window.
    private static readonly Dictionary<string, Attribute> Roads = new()
    {
        [BundledThemes.Midnight] = new Attribute(new Color(0x4B, 0x52, 0x63), new Color(0x1B, 0x1E, 0x24)),
        [BundledThemes.Daylight] = new Attribute(new Color(0xA0, 0xA4, 0xAC), new Color(0xFA, 0xFA, 0xFA)),
        [BundledThemes.TurboPascal] = Dos,
        [BundledThemes.ModernBorland] = Dos,
    };

    private int _frame;
    private object? _beat;

    public LoadingView()
    {
        CanFocus = false;
        Visible = false;
        BorderStyle = LineStyle.Single;
    }

    internal static IReadOnlyList<IReadOnlyList<IReadOnlyList<ArtSpan>>> Frames { get; } = Read();

    internal static int Cells { get; } =
        Frames.SelectMany(frame => frame).Max(row => row.Sum(span => span.Text.Length));

    internal static int Rows { get; } = Frames.Max(frame => frame.Count);

    internal int Showing => _frame;

    /// <summary>Shows it, driving. One already up keeps rolling, so the write and the read that follows it
    /// don't jog it back to the first frame between them.</summary>
    public void Start()
    {
        if (!Visible)
            _frame = 0;
        Visible = true;
        Roll();
        SetNeedsDraw();
    }

    /// <summary>Takes it away and stops it drawing.</summary>
    public void Stop()
    {
        Visible = false;
        if (_beat is { } beat)
            App?.RemoveTimeout(beat);
        _beat = null;
    }

    /// <summary>One shown before the application was running has no loop to drive it, so it picks one up
    /// here: the Work area's first read starts before <c>Run</c> does.</summary>
    public override void EndInit()
    {
        base.EndInit();
        if (Visible)
            Roll();
    }

    internal void Advance()
    {
        _frame = (_frame + 1) % Frames.Count;
        SetNeedsDraw();
    }

    /// <summary>The frames of an animation: rows of <c>[colour]text[/colour]</c>, a <c>---FRAME---</c> line
    /// between frames, and anything outside a tag in the default colour. A closing tag goes back to that
    /// colour rather than to whatever was around it, so the tags don't nest.</summary>
    internal static List<List<List<ArtSpan>>> Parse(string art) =>
    [
        .. art.Replace("\r", "").Split(Break)
            .Select(frame => frame.Trim('\n').Split('\n').Select(Spans).ToList()),
    ];

    /// <summary>Whether <paramref name="colour"/> is one the view has an ink for.</summary>
    internal static bool Draws(string colour) => Inks.ContainsKey(colour);

    /// <summary>The dots and the surface behind them in <paramref name="theme"/>, or Midnight's for one we
    /// don't ship.</summary>
    internal static Attribute Road(string theme) =>
        Roads.TryGetValue(theme, out var road) ? road : Roads[BundledThemes.Default];

    /// <summary>The picture in a row — its runs from the first mark to the last, and the cell that starts at.
    /// The van's own ink fills those cells, so it reads as a body; everywhere else the road shows through.</summary>
    internal static (int Start, List<ArtSpan> Spans) Solid(IReadOnlyList<ArtSpan> row)
    {
        var text = string.Concat(row.Select(span => span.Text));
        var start = text.AsSpan().IndexOfAnyExcept(' ');
        if (start < 0)
            return (0, []);
        var end = text.AsSpan().LastIndexOfAnyExcept(' ') + 1;
        var spans = new List<ArtSpan>(row.Count);
        var column = 0;
        foreach (var span in row)
        {
            var from = Math.Clamp(start, column, column + span.Text.Length) - column;
            var to = Math.Clamp(end, column, column + span.Text.Length) - column;
            if (to > from)
                spans.Add(span with { Text = span.Text[from..to] });
            column += span.Text.Length;
        }
        return (start, spans);
    }

    /// <summary>Where the art sits in <paramref name="room"/>: centred, and cut down to it rather than
    /// spilling out of it.</summary>
    internal static Rectangle Box(Size room)
    {
        var width = Math.Min(Cells, room.Width);
        var height = Math.Min(Rows, room.Height);
        return new Rectangle((room.Width - width) / 2, (room.Height - height) / 2, width, height);
    }

    /// <summary>The runs of a row that fall inside <paramref name="width"/>, the one that straddles it cut
    /// short.</summary>
    internal static List<ArtSpan> Clip(IReadOnlyList<ArtSpan> row, int width)
    {
        var clipped = new List<ArtSpan>(row.Count);
        var column = 0;
        foreach (var span in row)
        {
            var room = width - column;
            if (room < 1)
                break;
            clipped.Add(span.Text.Length <= room ? span : span with { Text = span.Text[..room] });
            column += clipped[^1].Text.Length;
        }
        return clipped;
    }

    internal static List<ArtSpan> Spans(string row)
    {
        var spans = new List<ArtSpan>();
        var colour = Untagged;
        for (var at = 0; at < row.Length;)
        {
            var close = row[at] == '[' ? row.IndexOf(']', at) : -1;
            if (close > at && Tag(row[(at + 1)..close]) is { } tag)
            {
                colour = tag;
                at = close + 1;
                continue;
            }
            var next = row.IndexOf('[', at + 1);
            var end = next < 0 ? row.Length : next;
            spans.Add(new ArtSpan(row[at..end], colour));
            at = end;
        }
        return spans;
    }

    /// <summary>The colour a <c>[…]</c> names, the default for a <c>[/…]</c>, and null for brackets around
    /// anything but a name, which are text like any other.</summary>
    private static string? Tag(string body)
    {
        var closing = body.StartsWith('/');
        var name = closing ? body[1..] : body;
        if (name.Length == 0 || !name.All(char.IsAsciiLetter))
            return null;
        return closing ? Untagged : name;
    }

    private void Roll() => _beat ??= App?.AddTimeout(Beat, () =>
    {
        Advance();
        return true;
    });

    protected override void Dispose(bool disposing)
    {
        Stop();
        base.Dispose(disposing);
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var room = Viewport.Size;
        SetAttribute(Road(BundledThemes.Current));
        for (var row = 0; row < room.Height; row++)
            AddStr(0, row, new string(Dot, room.Width));

        var box = Box(room);
        var frame = Frames[_frame];
        for (var row = 0; row < box.Height && row < frame.Count; row++)
        {
            var (start, spans) = Solid(Clip(frame[row], box.Width));
            var column = start;
            foreach (var span in spans)
            {
                SetAttribute(Inks.TryGetValue(span.Colour, out var ink) ? ink : Inks[Untagged]);
                AddStr(box.X + column, box.Y + row, span.Text);
                column += span.Text.Length;
            }
        }
        return true;
    }

    private static List<List<List<ArtSpan>>> Read()
    {
        using var stream = typeof(LoadingView).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"The embedded {Resource} is missing.");
        return Parse(new StreamReader(stream).ReadToEnd());
    }
}
