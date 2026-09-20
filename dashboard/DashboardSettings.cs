using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>The dashboard's settings, in a-team's own config directory beside <c>teams/</c>. Only the theme so far.</summary>
public sealed class DashboardSettings
{
    private readonly string _path;

    public DashboardSettings(string configRoot) => _path = Path.Combine(configRoot, "dashboard.json");

    /// <summary>a-team's config directory: <c>A_TEAM_CONFIG</c>, else <c>XDG_CONFIG_HOME/a-team</c>, else <c>~/.config/a-team</c>.</summary>
    public static string ConfigRoot() =>
        Environment.GetEnvironmentVariable("A_TEAM_CONFIG") is { Length: > 0 } configured
            ? configured
            : Path.Combine(
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
                    ? xdg
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
                "a-team");

    /// <summary>The theme to start in: the default unless the file names one we ship. Never writes, whatever it finds.</summary>
    public string ReadTheme()
    {
        using var file = Parse();
        return file?.RootElement is { ValueKind: JsonValueKind.Object } root &&
               root.TryGetProperty("theme", out var theme) &&
               theme.ValueKind == JsonValueKind.String &&
               theme.GetString() is { } name &&
               BundledThemes.Names.Contains(name)
            ? name
            : BundledThemes.Default;
    }

    public void WriteTheme(string theme)
    {
        if (Path.GetDirectoryName(_path) is { Length: > 0 } dir)
            Directory.CreateDirectory(dir);

        using var stream = File.Create(_path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("theme", theme);
        writer.WriteEndObject();
    }

    private JsonDocument? Parse()
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(_path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
