using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>Who could be a team's stakeholder: the people with write access to its repo.</summary>
public static class Stakeholders
{
    /// <summary>The collaborators with push access, <paramref name="me"/> first, or null where they can't be read.</summary>
    public static IReadOnlyList<string>? Parse(Reading collaborators, string me)
    {
        if (collaborators.Failure is not null)
            return null;
        try
        {
            using var document = JsonDocument.Parse(collaborators.Output);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;
            var logins = document.RootElement.EnumerateArray()
                .Where(person => person.ValueKind == JsonValueKind.Object &&
                                 person.TryGetProperty("permissions", out var permissions) &&
                                 permissions.ValueKind == JsonValueKind.Object &&
                                 permissions.TryGetProperty("push", out var push) && push.ValueKind == JsonValueKind.True &&
                                 person.TryGetProperty("login", out var login) && login.ValueKind == JsonValueKind.String)
                .Select(person => person.GetProperty("login").GetString() ?? "")
                .Where(login => login.Length > 0)
                .ToList();
            return [.. logins.Where(login => login == me), .. logins.Where(login => login != me)];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Asks GitHub who can push to <paramref name="repo"/>, and who's signed in to put them first.</summary>
    public static async Task<IReadOnlyList<string>?> Read(string repo)
    {
        var gh = new TeamCommand("gh");
        var me = gh.Read("api", "user", "--jq", ".login");
        var collaborators = gh.Read("api", $"repos/{repo}/collaborators?per_page=100");
        return Parse(await collaborators, (await me).Output.Trim());
    }

    /// <summary>What the picker says once the list has come back, or not.</summary>
    public static string Message(IReadOnlyList<string>? found, string repo) =>
        found is null
            ? $"Couldn't reach GitHub to see who can push to {repo}. Type a login to add one."
            : $"{found.Count} can push to {repo}. Type a login to add one that isn't listed.";
}
