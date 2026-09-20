using Terminal.Gui.Configuration;

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

    /// <summary>Registers the bundled themes and applies the default. Call before the application starts.</summary>
    public static void Load()
    {
        ConfigurationManager.RuntimeConfig = Config;
        // a-team keeps its own config, so ~/.tui and TUI_CONFIG are left out.
        ConfigurationManager.Enable(ConfigLocations.HardCoded | ConfigLocations.LibraryResources | ConfigLocations.Runtime);
        Apply(Default);
    }

    /// <summary>Switches to <paramref name="theme"/>, or to the default if it isn't one we ship.</summary>
    public static void Apply(string theme)
    {
        ThemeManager.Theme = Names.Contains(theme) ? theme : Default;
        ConfigurationManager.Apply();
        LogSchemes.Register();
    }

    private static string ReadConfig()
    {
        using var stream = typeof(BundledThemes).Assembly.GetManifestResourceStream("themes.json")
            ?? throw new InvalidOperationException("The embedded themes are missing.");
        return new StreamReader(stream).ReadToEnd();
    }
}
