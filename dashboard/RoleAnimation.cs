using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Color = Terminal.Gui.Drawing.Color;

namespace ATeam.Dashboard;

/// <summary>How strongly a cell of a frame is drawn.</summary>
public enum Tone
{
    Dim,
    Normal,
    Bright,
}

/// <summary>One frame of an animation: a glyph per cell, and a tone per cell as a digit, <c>0</c> dim to <c>2</c> bright.</summary>
public readonly record struct AnimationFrame(string Text, string Tones)
{
    public Tone ToneAt(int cell) => (Tone)(Tones[cell] - '0');

    /// <summary>The glyph a cell is drawn as: the Magnifier's lens is <c>nf-md-magnify</c> where a Nerd Font is in effect.</summary>
    public string Glyph(int cell, IconStyle style) =>
        Text[cell] == RoleAnimation.Lens && style == IconStyle.NerdFont ? RoleAnimation.NerdLens : Text[cell].ToString();
}

/// <summary>A role's animation in Overseer: three cells in a card's age slot while a run is on it, and one beside the
/// role's name in its lane's title while it's busy. Each frame lasts <see cref="Every"/>. One with no palette is drawn
/// dim in the theme's own colour.</summary>
public sealed class RoleAnimation
{
    private readonly Func<int, int, IReadOnlyList<AnimationFrame>> _chip;
    private readonly Func<int, int, IReadOnlyList<AnimationFrame>> _header;

    private RoleAnimation(
        string name, TimeSpan every, Func<int, int, IReadOnlyList<AnimationFrame>> chip,
        Func<int, int, IReadOnlyList<AnimationFrame>> header, TonePalette? dark, TonePalette? light)
    {
        Name = name;
        Every = every;
        _chip = chip;
        _header = header;
        Dark = dark;
        Light = light;
    }

    public string Name { get; }

    public TimeSpan Every { get; }

    internal TonePalette? Dark { get; }

    internal TonePalette? Light { get; }

    public const int ChipCells = 3;

    /// <summary>The run of chip frames for <paramref name="cycle"/>; <paramref name="salt"/> keeps chips that start
    /// together from scrambling alike.</summary>
    public IReadOnlyList<AnimationFrame> ChipFrames(int cycle = 0, int salt = 0) => _chip(cycle, salt);

    public IReadOnlyList<AnimationFrame> HeaderFrames(int cycle = 0, int salt = 0) => _header(cycle, salt);

    public AnimationFrame Chip(TimeSpan elapsed, int salt) => At(_chip, elapsed, salt);

    public AnimationFrame Header(TimeSpan elapsed, int salt) => At(_header, elapsed, salt);

    /// <summary>Which frame is showing; it changes exactly when the drawing does.</summary>
    public long Step(TimeSpan elapsed) => elapsed.Ticks / Every.Ticks;

    public Color Colour(Tone tone, Attribute under) =>
        (under.Background.IsDarkColor() ? Dark : Light)?[(int)tone] ?? Midway(under.Foreground, under.Background);

    private static Color Midway(Color from, Color to) => new((from.R + to.R) / 2, (from.G + to.G) / 2, (from.B + to.B) / 2);

    private AnimationFrame At(Func<int, int, IReadOnlyList<AnimationFrame>> frames, TimeSpan elapsed, int salt)
    {
        var step = Step(elapsed);
        var count = frames(0, salt).Count;
        var run = frames((int)(step / count), salt);
        return run[(int)(step % count)];
    }

    public static RoleAnimation For(string role) => role switch
    {
        "dev" => Decrypt,
        "lead" => ThoughtToIdea,
        "customer" => Typing,
        "reviewer" => Magnifier,
        _ => Spinner,
    };

    internal const char Lens = '⌕';

    internal const string NerdLens = "\U000F0349"; // nf-md-magnify

    private static readonly AnimationFrame[] ThoughtChip =
    [
        new(".oO", "011"), new(".oO", "011"), new(" oO", "011"), new("  O", "001"), new("  ✺", "002"), new("  ✹", "002"),
        new("  ✦", "002"), new("  ✦", "001"), new(". ✧", "001"), new(".o·", "010"),
    ];

    private static readonly AnimationFrame[] ThoughtHeader =
    [
        new(".", "0"), new("o", "1"), new("O", "1"), new("O", "1"), new("✺", "2"), new("✹", "2"), new("✦", "2"), new("✦", "1"),
        new("✧", "1"), new("·", "0"),
    ];

    private static readonly AnimationFrame[] TypingChip =
    [
        new("⣤⣤⣤", "000"), new("⠶⣤⣤", "100"), new("⠛⣤⣤", "200"), new("⠶⠶⣤", "110"), new("⣤⠛⣤", "020"), new("⣤⠶⠶", "011"),
        new("⣤⣤⠛", "002"), new("⣤⣤⠶", "001"), new("⣤⣤⣤", "000"), new("⣤⣤⣤", "000"), new("⣤⣤⣤", "000"), new("⣤⣤⣤", "000"),
        new("⣤⣤⣤", "000"), new("⣤⣤⣤", "000"),
    ];

    private static readonly AnimationFrame[] TypingHeader =
    [
        new("⣤", "0"), new("⠶", "1"), new("⠛", "2"), new("⠶", "1"), new("⣤", "0"), new("⣤", "0"), new("⣤", "0"), new("⣤", "0"),
    ];

    private static readonly AnimationFrame[] MagnifierChip =
    [
        new("⌕-+", "200"), new("⌕-+", "200"), new("⌕-+", "200"), new("+⌕+", "020"), new("+⌕+", "020"), new("+⌕+", "020"),
        new("+-⌕", "002"), new("+-⌕", "002"), new("+-⌕", "002"), new("-+⌕", "002"), new("-+⌕", "002"), new("-+⌕", "002"),
        new("-⌕-", "020"), new("-⌕-", "020"), new("-⌕-", "020"), new("⌕+-", "200"), new("⌕+-", "200"), new("⌕+-", "200"),
        new("⌕+-", "200"), new("⌕+-", "200"), new("⌕+-", "200"), new("+⌕-", "020"), new("+⌕-", "020"), new("+⌕-", "020"),
        new("++⌕", "002"), new("++⌕", "002"), new("++⌕", "002"), new("-+⌕", "002"), new("-+⌕", "002"), new("-+⌕", "002"),
        new("-⌕+", "020"), new("-⌕+", "020"), new("-⌕+", "020"), new("⌕++", "200"), new("⌕++", "200"), new("⌕++", "200"),
    ];

    private static readonly AnimationFrame[] MagnifierHeader =
    [
        new("⌕", "2"), new("⌕", "2"), new("⌕", "2"), new("+", "0"), new("⌕", "2"), new("⌕", "2"), new("⌕", "2"), new("-", "0"),
    ];

    internal const string SpinnerGlyphs = "⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏";

    private static readonly AnimationFrame[] SpinnerChip = [.. SpinnerGlyphs.Select(glyph => new AnimationFrame($"  {glyph}", "000"))];

    private static readonly AnimationFrame[] SpinnerHeader = [.. SpinnerGlyphs.Select(glyph => new AnimationFrame($"{glyph}", "0"))];

    public static readonly RoleAnimation Decrypt = new(
        "Decrypt", TimeSpan.FromMilliseconds(140), DecryptChip, DecryptHeader,
        new("#2f7d48", "#4fd27a", "#e6ffec"), new("#8dc4a0", "#23864a", "#0b4a22"));

    public static readonly RoleAnimation ThoughtToIdea = new(
        "Thought to idea", TimeSpan.FromMilliseconds(280), (_, _) => ThoughtChip, (_, _) => ThoughtHeader,
        new("#8d6818", "#f0b23e", "#fff3c8"), new("#d4b77a", "#a86f00", "#5c3a00"));

    public static readonly RoleAnimation Typing = new(
        "Typing", TimeSpan.FromMilliseconds(220), (_, _) => TypingChip, (_, _) => TypingHeader,
        new("#2b6789", "#5cb9ea", "#e2f5ff"), new("#8fb6cf", "#1f76a8", "#0b3d5c"));

    public static readonly RoleAnimation Magnifier = new(
        "Magnifier", TimeSpan.FromMilliseconds(240), (_, _) => MagnifierChip, (_, _) => MagnifierHeader,
        new("#6b5299", "#b691f2", "#f1e8ff"), new("#b9a6d9", "#7a4fc4", "#3d2170"));

    public static readonly RoleAnimation Spinner = new(
        "Spinner", TimeSpan.FromMilliseconds(80), (_, _) => SpinnerChip, (_, _) => SpinnerHeader, null, null);

    internal static readonly string[] Words = ["</>", "{;}", "f()", "0x1"];

    internal static readonly string[] Symbols = [">", "}", ";", "#"];

    internal const string Scramble = "ｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜ#$%&*+=<>@:;|[]{}/0123456789Z";

    /// <summary>Each word scrambles, locks in left to right, holds bright, then holds.</summary>
    private static IReadOnlyList<AnimationFrame> DecryptChip(int cycle, int salt)
    {
        var random = new Random(HashCode.Combine(cycle, salt));
        List<AnimationFrame> frames = [];
        foreach (var word in Words)
        {
            Add(frames, 5, () => Scrambled(random, 3), "111");
            Add(frames, 2, () => word[..1] + Scrambled(random, 2), "211");
            Add(frames, 2, () => word[..2] + Scrambled(random, 1), "221");
            Add(frames, 4, () => word, "222");
            Add(frames, 5, () => word, "111");
        }
        return frames;
    }

    private static IReadOnlyList<AnimationFrame> DecryptHeader(int cycle, int salt)
    {
        var random = new Random(HashCode.Combine(cycle, salt, 1));
        List<AnimationFrame> frames = [];
        foreach (var symbol in Symbols)
        {
            Add(frames, 5, () => Scrambled(random, 1), "1");
            Add(frames, 4, () => symbol, "2");
            Add(frames, 5, () => symbol, "1");
        }
        return frames;
    }

    private static void Add(List<AnimationFrame> frames, int count, Func<string> text, string tones)
    {
        for (var each = 0; each < count; each++)
            frames.Add(new(text(), tones));
    }

    private static string Scrambled(Random random, int cells) =>
        string.Concat(Enumerable.Range(0, cells).Select(_ => Scramble[random.Next(Scramble.Length)]));
}

/// <summary>A role's dim, normal and bright colours on one kind of background.</summary>
internal sealed class TonePalette(string dim, string normal, string bright)
{
    private readonly Color[] _colours = [Color.Parse(dim), Color.Parse(normal), Color.Parse(bright)];

    public Color this[int tone] => _colours[tone];
}
