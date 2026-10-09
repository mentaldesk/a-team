using System.Text.Json;
using System.Text.Json.Nodes;

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

    /// <summary>What the team's check depends on: its file without <c>dispatch</c>, which pausing and holding change.</summary>
    public string? Stamp(string team)
    {
        string text;
        try
        {
            text = File.ReadAllText(PathOf(team));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        try
        {
            if (JsonNode.Parse(text) is JsonObject config)
            {
                config.Remove("dispatch");
                return config.ToJsonString();
            }
        }
        catch (JsonException) { }
        return text;
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

    /// <summary>Writes a new team's file from <paramref name="example"/>, with the form's values, its checkout at
    /// <c>&lt;workdir&gt;/main</c>, and paused. Fails rather than replace a file that's already there.</summary>
    public void Create(string team, byte[] example, TeamSettings settings)
    {
        var config = (settings with { Working = false }).Write(example, TeamSettings.Read(example));
        config = ConfigEdit.Set(config, ["checkout"], settings.CheckoutPath);
        config = ConfigEdit.SetEnabled(config, false);
        Directory.CreateDirectory(TeamsDirectory);
        using var file = new FileStream(PathOf(team), FileMode.CreateNew, FileAccess.Write);
        file.Write(config);
    }

    public void Delete(string team) => File.Delete(PathOf(team));

    /// <summary>Writes the form's values over a new team's file as it is now, keeping what its steps saved there,
    /// under <paramref name="name"/> where it was renamed. Its checkout follows the workdir.</summary>
    public void Redo(string team, string name, TeamSettings settings)
    {
        var config = settings.Write(File.ReadAllBytes(PathOf(team)), Settings(team));
        config = ConfigEdit.Set(config, ["checkout"], (settings with { Checkout = null }).CheckoutPath);
        if (name == team)
        {
            File.WriteAllBytes(PathOf(team), config);
            return;
        }
        using (var file = new FileStream(PathOf(name), FileMode.CreateNew, FileAccess.Write))
            file.Write(config);
        Delete(team);
    }

    /// <summary>The team whose file names a GitHub App for a repo under <paramref name="owner"/>, if any.</summary>
    public string? WithApp(string owner, string except = "") =>
        Names().FirstOrDefault(team => team != except && AppOwner(team) == owner);

    /// <summary>Whether the team's file names its GitHub App.</summary>
    public bool HasApp(string team) => AppOwner(team) is not null;

    /// <summary>The owner of the repo a team with a GitHub App works on, or null where it has none.</summary>
    private string? AppOwner(string team)
    {
        try
        {
            using var config = JsonDocument.Parse(File.ReadAllText(PathOf(team)));
            var root = config.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("app", out var app) || app.ValueKind != JsonValueKind.Object ||
                !app.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number)
                return null;
            var repo = root.TryGetProperty("repo", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
            return repo.Split('/')[0];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Where <see cref="Remove"/> would keep the team's file: <c>&lt;team&gt;.json.removed</c>, or, when an
    /// older one is there, the first free <c>&lt;team&gt;.json.removed.&lt;n&gt;</c>.</summary>
    public string RemovedName(string team)
    {
        var name = $"{team}.json.removed";
        for (var n = 2; File.Exists(Path.Combine(TeamsDirectory, name)); n++)
            name = $"{team}.json.removed.{n}";
        return name;
    }

    /// <summary>Moves the team's file aside, so nothing lists it any more, and returns the name it's kept under.
    /// The team's state is left alone.</summary>
    public string Remove(string team)
    {
        var kept = RemovedName(team);
        File.Move(PathOf(team), Path.Combine(TeamsDirectory, kept), overwrite: false);
        return kept;
    }

    private string PathOf(string team) => Path.Combine(TeamsDirectory, $"{team}.json");

    private static string Reason(string message) =>
        message.IndexOf(" LineNumber:", StringComparison.Ordinal) is var at and >= 0 ? message[..at] : message;

    /// <summary>The roles the team runs, in the order their panes sit: the Customer lead and the Reviewer only where
    /// they're on, and just the Lead and Dev where the file can't be read.</summary>
    public string[] Roles(string team)
    {
        try
        {
            var settings = Settings(team);
            return ["lead", "dev", .. settings.Customer ? ["customer"] : Array.Empty<string>(),
                .. settings.Reviewer ? ["reviewer"] : Array.Empty<string>()];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return ["lead", "dev"];
        }
    }

    /// <summary>The team's Project board on GitHub, or null when its config doesn't name one.</summary>
    public string? BoardUrl(string team)
    {
        try
        {
            using var config = JsonDocument.Parse(File.ReadAllText(PathOf(team)));
            if (config.RootElement.ValueKind != JsonValueKind.Object ||
                !config.RootElement.TryGetProperty("project", out var project) ||
                project.ValueKind != JsonValueKind.Object ||
                !project.TryGetProperty("owner", out var owner) || owner.ValueKind != JsonValueKind.String ||
                owner.GetString() is not { Length: > 0 } login ||
                !project.TryGetProperty("number", out var number) || !number.TryGetInt32(out var board))
                return null;
            var kind = project.TryGetProperty("ownerType", out var type) && type.ValueKind == JsonValueKind.String &&
                       type.GetString() == "user"
                ? "users"
                : "orgs";
            return $"https://github.com/{kind}/{Uri.EscapeDataString(login)}/projects/{board}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

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
