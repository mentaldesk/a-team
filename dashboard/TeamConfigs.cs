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

    /// <summary>A team as the Teams page lists it: its repo, whether it's paused, or why its file can't be read.</summary>
    public TeamRow Row(string team)
    {
        try
        {
            using var config = JsonDocument.Parse(File.ReadAllText(PathOf(team)));
            if (config.RootElement.ValueKind != JsonValueKind.Object)
                return new TeamRow(team, "", true, "it isn't a JSON object");
            var repo = config.RootElement.TryGetProperty("repo", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
            return new TeamRow(team, repo, IsPaused(team), null);
        }
        catch (JsonException e)
        {
            return new TeamRow(team, "", true, $"line {e.LineNumber + 1}: {Reason(e.Message)}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new TeamRow(team, "", true, e.Message);
        }
    }

    /// <summary>Sets <c>dispatch.enabled</c> and nothing else: every other byte of the file stays as it was.</summary>
    public void SetWorking(string team, bool working)
    {
        var path = PathOf(team);
        File.WriteAllBytes(path, ConfigEdit.SetEnabled(File.ReadAllBytes(path), working));
    }

    /// <summary>What the team form shows for <paramref name="team"/>, read from its file as it is now.</summary>
    public TeamSettings Settings(string team) => TeamSettings.Read(File.ReadAllBytes(PathOf(team)));

    /// <summary>Writes the settings that differ from <paramref name="before"/> into the file as it is now, leaving
    /// every other byte as it was.</summary>
    public void Save(string team, TeamSettings before, TeamSettings after)
    {
        var path = PathOf(team);
        File.WriteAllBytes(path, after.Write(File.ReadAllBytes(path), before));
    }

    private string PathOf(string team) => Path.Combine(TeamsDirectory, $"{team}.json");

    private static string Reason(string message) =>
        message.IndexOf(" LineNumber:", StringComparison.Ordinal) is var at and >= 0 ? message[..at] : message;

    /// <summary>Held is a role named in <c>dispatch.hold</c>, which <c>a-team stop</c> writes.</summary>
    public bool IsHeld(string team, string role)
    {
        try
        {
            using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(TeamsDirectory, $"{team}.json")));
            return config.RootElement.TryGetProperty("dispatch", out var dispatch) &&
                   dispatch.TryGetProperty("hold", out var hold) &&
                   hold.ValueKind == JsonValueKind.Array &&
                   hold.EnumerateArray().Any(held => held.ValueKind == JsonValueKind.String && held.GetString() == role);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}

/// <summary>A team's row on the Teams page. <see cref="Problem"/> is set when its file can't be read.</summary>
public sealed record TeamRow(string Name, string Repo, bool Paused, string? Problem);
