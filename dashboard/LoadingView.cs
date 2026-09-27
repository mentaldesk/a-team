using System.Drawing;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Color = Terminal.Gui.Drawing.Color;

namespace ATeam.Dashboard;

/// <summary>A run of one row's cells in a single colour.</summary>
internal readonly record struct ArtSpan(string Text, string Colour);

/// <summary>An animation: the body every frame is painted on, and the frames.</summary>
internal sealed record Art(IReadOnlyList<string> Body, IReadOnlyList<IReadOnlyList<IReadOnlyList<ArtSpan>>> Frames);

/// <summary>The surface outside the van, the dots on it, and the colour the exhaust is drawn in there.</summary>
internal readonly record struct Road(Color Surface, Color Dots, Color Smoke);

/// <summary>The van, driving, in place of the issue body while the team writes one rank and fetches the next
/// item: a solid body on a dotted road, in a frame of its own. <see cref="SpinnerView"/> draws a single line
/// in the view's own colour, so it can't show this.</summary>
public sealed class LoadingView : View
{
    private const string Resource = "loading.anim";
    private const string BodyBreak = "---BODY---";
    private const string FrameBreak = "---FRAME---";
    private const string Untagged = "gray";
    private const string Exhaust = "yellow";

    private const char Dot = '·';
    private const char Open = ' ';

    private static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(120);

    // The van is a picture, not chrome: its body and its inks are the same in every theme.
    private static readonly Dictionary<char, Color> Fills = new()
    {
        ['g'] = new Color(0x3A, 0x3A, 0x3A),
        ['d'] = new Color(0x23, 0x23, 0x23),
        ['b'] = new Color(0x00, 0x00, 0x00),
        ['r'] = new Color(0x6B, 0x14, 0x14),
    };

    private static readonly Dictionary<string, Color> Inks = new()
    {
        [Untagged] = new Color(StandardColor.Gray),
        ["white"] = new Color(StandardColor.White),
        ["red"] = new Color(StandardColor.BrightRed),
        [Exhaust] = new Color(StandardColor.BrightYellow),
    };

    private static readonly Road Dos =
        new(new Color(0xC0, 0xC0, 0xC0), new Color(0x55, 0x55, 0x55), new Color(0x8A, 0x5A, 0x00));

    // The road is the part that follows the theme, so the frame isn't a black hole in a pale one. The DOS
    // themes get grey rather than their own blue: the van is parked on a road, not in a window.
    private static readonly Dictionary<string, Road> Roads = new()
    {
        [BundledThemes.Midnight] =
            new(new Color(0x1B, 0x1E, 0x24), new Color(0x4B, 0x52, 0x63), new Color(StandardColor.BrightYellow)),
        [BundledThemes.Daylight] =
            new(new Color(0xFA, 0xFA, 0xFA), new Color(0xA0, 0xA4, 0xAC), new Color(0xA8, 0x74, 0x00)),
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

    internal static Art Shipped { get; } = Read();

    internal static IReadOnlyList<IReadOnlyList<IReadOnlyList<ArtSpan>>> Frames => Shipped.Frames;

    internal static int Cells { get; } = Math.Max(
        Shipped.Frames.SelectMany(frame => frame).Max(row => row.Sum(span => span.Text.Length)),
        Shipped.Body.Select(row => row.Length).DefaultIfEmpty().Max());

    internal static int Rows { get; } = Math.Max(Shipped.Frames.Max(frame => frame.Count), Shipped.Body.Count);

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

    /// <summary>An animation: a <c>---BODY---</c> section, then a <c>---FRAME---</c> section per frame. The body
    /// is one letter per cell for what's under the picture — <c>g</c> grey, <c>d</c> dark grey, <c>b</c> black,
    /// <c>r</c> red — and a space for the road. A frame is rows of <c>[colour]text[/colour]</c>, anything outside
    /// a tag in the default colour. A closing tag goes back to that colour rather than to whatever was around
    /// it, so the tags don't nest.</summary>
    internal static Art Parse(string art)
    {
        List<string> body = [];
        List<List<string>> frames = [];
        List<string>? section = null;
        foreach (var line in art.Replace("\r", "").Split('\n'))
        {
            if (line == BodyBreak)
                section = body;
            else if (line == FrameBreak)
                frames.Add(section = []);
            else
            {
                if (section is null)
                    frames.Add(section = []);
                section.Add(line);
            }
        }
        return new Art(
            Trimmed(body),
            [.. frames.Select(rows => (IReadOnlyList<IReadOnlyList<ArtSpan>>)[.. Trimmed(rows).Select(Spans)])]);
    }

    /// <summary>Whether <paramref name="colour"/> is one the view has an ink for.</summary>
    internal static bool Draws(string colour) => Inks.ContainsKey(colour);

    /// <summary>Whether <paramref name="fill"/> is a body letter the view can paint, or the road.</summary>
    internal static bool Paints(char fill) => fill == Open || Fills.ContainsKey(fill);

    /// <summary>The surface, dots and exhaust in <paramref name="theme"/>, or Midnight's for one we don't
    /// ship.</summary>
    internal static Road RoadIn(string theme) =>
        Roads.TryGetValue(theme, out var road) ? road : Roads[BundledThemes.Default];

    /// <summary>Where the art sits in <paramref name="room"/>: centred, and cut down to it rather than
    /// spilling out of it.</summary>
    internal static Rectangle Box(Size room)
    {
        var width = Math.Min(Cells, room.Width);
        var height = Math.Min(Rows, room.Height);
        return new Rectangle((room.Width - width) / 2, (room.Height - height) / 2, width, height);
    }

    /// <summary>The glyph at <paramref name="column"/> of a row and the colour it's in: a space past the end.</summary>
    internal static (char Glyph, string Colour) At(IReadOnlyList<ArtSpan> row, int column)
    {
        foreach (var span in row)
        {
            if (column < span.Text.Length)
                return (span.Text[column], span.Colour);
            column -= span.Text.Length;
        }
        return (Open, Untagged);
    }

    /// <summary>What a cell shows: on the body, its glyph in its own ink on the body's colour; on the road, a
    /// dot where there's nothing, and the exhaust in the road's own colour for it.</summary>
    internal static (char Glyph, Attribute Paint) Cell(char fill, char glyph, string colour, Road road)
    {
        if (Fills.TryGetValue(fill, out var body))
            return (glyph, new Attribute(Ink(colour), body));
        if (glyph == Open)
            return (Dot, new Attribute(road.Dots, road.Surface));
        return (glyph, new Attribute(colour == Exhaust ? road.Smoke : Ink(colour), road.Surface));
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

    private static Color Ink(string colour) => Inks.TryGetValue(colour, out var ink) ? ink : Inks[Untagged];

    private static List<string> Trimmed(List<string> rows)
    {
        while (rows.Count > 0 && rows[^1].Length == 0)
            rows.RemoveAt(rows.Count - 1);
        return rows;
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
        var road = RoadIn(BundledThemes.Current);
        var box = Box(room);
        var frame = Frames[_frame];
        for (var row = 0; row < room.Height; row++)
        {
            var y = row - box.Y;
            var inside = y >= 0 && y < box.Height;
            var body = inside && y < Shipped.Body.Count ? Shipped.Body[y] : "";
            var art = inside && y < frame.Count ? frame[y] : [];
            for (var column = 0; column < room.Width; column++)
            {
                var x = column - box.X;
                var fill = x >= 0 && x < box.Width && x < body.Length ? body[x] : Open;
                var (glyph, colour) = x >= 0 && x < box.Width ? At(art, x) : (Open, Untagged);
                var (shown, paint) = Cell(fill, glyph, colour, road);
                SetAttribute(paint);
                AddStr(column, row, shown.ToString());
            }
        }
        return true;
    }

    private static Art Read()
    {
        using var stream = typeof(LoadingView).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"The embedded {Resource} is missing.");
        return Parse(new StreamReader(stream).ReadToEnd());
    }
}
