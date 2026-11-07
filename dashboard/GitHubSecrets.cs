using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>The GitHub Actions secret a task is held asking for: its name, the environment it goes in (none for the
/// repo's own), the team's repo, why it's needed, and which role asked.</summary>
public sealed record SecretAsk(string Name, string Environment, string Repo, string Why, string By)
{
    public string Asker => By == "lead" ? "the Lead" : "the Dev";

    /// <summary>Where it goes, as the dialog says it.</summary>
    public string Place => Environment.Length == 0 ? $"a repo secret of {Repo}" : $"a secret in the {Environment} environment of {Repo}";
}

/// <summary>Reads and stores Actions secrets with <paramref name="gh"/>, which must be the stakeholder's own sign-in:
/// never the team's App. In a dry run nothing is stored.</summary>
public sealed class GitHubSecrets(TeamCommand gh, bool dryRun = false)
{
    /// <summary>Whether it's already set there. A read that fails says no, and saving reports what's wrong.</summary>
    public async Task<bool> IsSet(SecretAsk ask)
    {
        var read = await gh.Read(["secret", "list", .. Target(ask), "--json", "name"]).ConfigureAwait(false);
        if (read.Failure is not null)
            return false;
        try
        {
            using var names = JsonDocument.Parse(read.Output);
            return names.RootElement.ValueKind == JsonValueKind.Array && names.RootElement.EnumerateArray()
                .Any(each => string.Equals(each.GetProperty("name").GetString(), ask.Name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Stores it, the value on stdin so that no process listing shows it. Null once GitHub has it, otherwise why not.</summary>
    public async Task<string?> Store(SecretAsk ask, string value)
    {
        if (dryRun)
            return null;
        var failure = await gh.Feed(value, ["secret", "set", ask.Name, .. Target(ask)]).ConfigureAwait(false);
        return failure is null ? null : Why(ask, failure);
    }

    private static string[] Target(SecretAsk ask) =>
        ask.Environment.Length == 0 ? ["--repo", ask.Repo] : ["--repo", ask.Repo, "--env", ask.Environment];

    private static string Why(SecretAsk ask, string failure) =>
        failure.Contains("HTTP 404", StringComparison.Ordinal)
            ? ask.Environment.Length == 0 ? $"GitHub can't find {ask.Repo}, or you aren't an admin of it"
                : $"GitHub can't find the {ask.Environment} environment of {ask.Repo}, or you aren't an admin of it"
            : failure.Contains("HTTP 403", StringComparison.Ordinal) ? $"GitHub refused: you aren't an admin of {ask.Repo}"
            : failure;
}
