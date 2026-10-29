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

    /// <summary>Asks GitHub who can push to <paramref name="repo"/>, and who's signed in to put them first; throws
    /// where GitHub says why it can't.</summary>
    public static async Task<IReadOnlyList<string>?> Read(string repo)
    {
        var gh = new TeamCommand("gh");
        var me = gh.Read("api", "user", "--jq", ".login");
        var collaborators = await gh.Read("api", $"repos/{repo}/collaborators?per_page=100");
        if (collaborators.Failure is { } failure)
            throw new InvalidOperationException(failure.StartsWith("gh: ", StringComparison.Ordinal) ? failure[4..] : failure);
        return Parse(collaborators, (await me).Output.Trim());
    }

    /// <summary>What the picker says once the list has come back.</summary>
    public static string Message(IReadOnlyList<string> found, string repo) =>
        $"{found.Count} can push to {repo}. Type a login to add one that isn't listed.";

    /// <summary>What the picker says where the list couldn't be read, and why where that's known.</summary>
    public static string Failed(string repo, string? why) =>
        why is null ? $"Couldn't read who can push to {repo}." : $"Couldn't read who can push to {repo}: {why}";
}
