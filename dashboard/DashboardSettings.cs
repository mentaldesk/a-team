using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>The dashboard's settings, in a-team's own config directory beside <c>teams/</c>.</summary>
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
        return Setting(file, "theme") is { ValueKind: JsonValueKind.String } theme &&
               theme.GetString() is { } name &&
               BundledThemes.Names.Contains(name)
            ? name
            : BundledThemes.Default;
    }

    public void WriteTheme(string theme) => Write("theme", writer => writer.WriteStringValue(theme));

    /// <summary>Whether panes start with every tool call showing. Off unless the file says otherwise.</summary>
    public bool ReadExpandToolCalls()
    {
        using var file = Parse();
        return Setting(file, "expandToolCalls") is { ValueKind: JsonValueKind.True };
    }

    public void WriteExpandToolCalls(bool expand) => Write("expandToolCalls", writer => writer.WriteBooleanValue(expand));

    /// <summary>Writes one setting, carrying over every other one the file already holds.</summary>
    private void Write(string name, Action<Utf8JsonWriter> value)
    {
        using var existing = Parse();
        List<JsonProperty> others = existing?.RootElement is { ValueKind: JsonValueKind.Object } root
            ? [.. root.EnumerateObject().Where(property => property.Name != name)]
            : [];

        if (Path.GetDirectoryName(_path) is { Length: > 0 } dir)
            Directory.CreateDirectory(dir);

        using var stream = File.Create(_path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        foreach (var other in others)
            other.WriteTo(writer);
        writer.WritePropertyName(name);
        value(writer);
        writer.WriteEndObject();
    }

    private static JsonElement? Setting(JsonDocument? file, string name) =>
        file?.RootElement is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty(name, out var setting)
            ? setting
            : null;

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
