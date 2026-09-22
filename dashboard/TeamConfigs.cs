using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>The team configs beside dashboard.json: which teams there are, and which are paused.</summary>
public sealed class TeamConfigs
{
    public TeamConfigs(string configRoot) => TeamsDirectory = Path.Combine(configRoot, "teams");

    public string TeamsDirectory { get; }

    /// <summary>Every configured team in name order, paused or not.</summary>
    public string[] Names() =>
        Directory.Exists(TeamsDirectory)
            ?
            [
                .. Directory.GetFiles(TeamsDirectory, "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OfType<string>()
                    .Order()
            ]
            : [];

    /// <summary>Paused is whatever the dispatcher skips: anything but <c>dispatch.enabled</c> true.</summary>
    public bool IsPaused(string team)
    {
        try
        {
            using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(TeamsDirectory, $"{team}.json")));
            return !(config.RootElement.TryGetProperty("dispatch", out var dispatch) &&
                     dispatch.TryGetProperty("enabled", out var enabled) &&
                     enabled.ValueKind == JsonValueKind.True);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return true;
        }
    }
}
