using MentalDesk.Tui.Theming;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>The themes the dashboard offers, copied from TuiCode so both windows on the desk match.</summary>
public static class BundledThemes
{
    public const string Midnight = "Midnight";
    public const string Daylight = "Daylight";
    public const string TurboPascal = "Turbo Pascal";
    public const string ModernBorland = "Modern Borland";

    public const string Default = Midnight;

    public static IReadOnlyList<string> Names { get; } = [Midnight, Daylight, TurboPascal, ModernBorland];

    public static string Config { get; } = ReadConfig();

    public static string Current => ThemeManager.Theme;

    /// <summary>The terminal cursor each theme colours. Silent until the app points it at the console.</summary>
    public static TerminalCursor Cursor { get; set; } = new(_ => { });

    /// <summary>Registers the bundled themes and applies <paramref name="theme"/>. Call before the application starts.</summary>
    public static void Load(string theme = Default)
    {
        TuiConfigurationBuilder.Shared.RuntimeConfig = Config;
        TuiConfigurationBuilder.Shared.ApplyToStaticFacades();
        Apply(theme);
    }

    /// <summary>Switches to <paramref name="theme"/>, or to the default if it isn't one we ship.</summary>
    public static void Apply(string theme)
    {
        ThemeManager.Theme = Names.Contains(theme) ? theme : Default;
        LogSchemes.Register();
        if (CursorColour(ThemeManager.Theme) is { } colour)
            Cursor.Colour(colour);
    }

    /// <summary>The colour of <paramref name="theme"/>'s caret: its <c>Cursor</c> scheme, which nothing draws with.</summary>
    public static Color? CursorColour(string theme) =>
        TuiConfigurationBuilder.Shared.Configuration[$"Themes:{theme}:Schemes:Cursor:Normal:Foreground"] is { } colour
            ? Color.Parse(colour)
            : null;

    private static string ReadConfig()
    {
        using var stream = typeof(BundledThemes).Assembly.GetManifestResourceStream("themes.json")
            ?? throw new InvalidOperationException("The embedded themes are missing.");
        return new StreamReader(stream).ReadToEnd();
    }
}
