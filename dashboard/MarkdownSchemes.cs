using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>The colours markdown is read in, taken from the token theme TuiCode's editor pairs with each of our
/// themes, so a pitch reads the same in both.</summary>
public static class MarkdownSchemes
{
    private sealed record Palette(string Heading, string Code, string InlineCode, string Quote, string ListMarker, string? Strong);

    private static readonly Palette DarkPlus = new("#569CD6", "#CE9178", "#CE9178", "#6A9955", "#6796E6", "#569CD6");
    private static readonly Palette LightPlus = new("#800000", "#800000", "#800000", "#0451A5", "#0451A5", "#000080");
    private static readonly Palette Borland = new("#FFFFFF", "#55FF55", "#55FFFF", "#AAAAAA", "#FFFFFF", null);

    private static readonly Dictionary<string, Palette> Palettes = new()
    {
        [BundledThemes.Midnight] = DarkPlus,
        [BundledThemes.Daylight] = LightPlus,
        [BundledThemes.TurboPascal] = Borland,
        [BundledThemes.ModernBorland] = Borland,
    };

    public static string Scheme(LogLineKind kind) => $"Markdown.{kind}";

    internal static void Register(Scheme reader, string theme)
    {
        var palette = Palettes.GetValueOrDefault(theme, DarkPlus);
        Add(reader, LogLineKind.Heading, palette.Heading, TextStyle.Bold);
        Add(reader, LogLineKind.Code, palette.Code);
        Add(reader, LogLineKind.InlineCode, palette.InlineCode);
        Add(reader, LogLineKind.Quote, palette.Quote);
        Add(reader, LogLineKind.ListMarker, palette.ListMarker);
        Add(reader, LogLineKind.Strong, palette.Strong, TextStyle.Bold);
    }

    private static void Add(Scheme reader, LogLineKind kind, string? ink, TextStyle style = TextStyle.None) =>
        SchemeManager.AddScheme(Scheme(kind), reader with
        {
            Normal = reader.Normal with
            {
                Foreground = ink is null ? reader.Normal.Foreground : new Color(ink),
                Style = reader.Normal.Style | style,
            },
        });
}
