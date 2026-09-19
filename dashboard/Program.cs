using System.Text.Json;
using ATeam.Dashboard;

var root = FindRepoRoot(AppContext.BaseDirectory) ?? FindRepoRoot(Environment.CurrentDirectory);
if (root is null)
{
    Console.Error.WriteLine("a-team-dashboard: can't find the a-team repo (a folder with teams/ and scripts/dispatch.sh).");
    return 1;
}

var stateRoot = Environment.GetEnvironmentVariable("A_TEAM_STATE") is { Length: > 0 } explicitState
    ? explicitState
    : Path.Combine(
        Environment.GetEnvironmentVariable("XDG_STATE_HOME") is { Length: > 0 } xdg
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state"),
        "a-team");

var teams = args.Length > 0 ? args : EnabledTeams(root);
if (teams.Length == 0)
{
    Console.Error.WriteLine("a-team-dashboard: no team has dispatch.enabled; pass a team name.");
    return 1;
}
var agents = teams.SelectMany(team => new[] { (team, "lead"), (team, "dev") }).ToList();

using var app = Application.Create();
app.Init();
using var window = new DashboardWindow(agents, stateRoot);
window.Refresh();
app.AddTimeout(TimeSpan.FromSeconds(1), () =>
{
    window.Refresh();
    return true;
});
app.Run(window);
return 0;

static string? FindRepoRoot(string start)
{
    for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "teams")) &&
            File.Exists(Path.Combine(dir.FullName, "scripts", "dispatch.sh")))
            return dir.FullName;
    }
    return null;
}

static string[] EnabledTeams(string root) =>
    Directory.GetDirectories(Path.Combine(root, "teams"))
        .Where(dir => IsEnabled(Path.Combine(dir, "team.json")))
        .Select(Path.GetFileName)
        .OfType<string>()
        .Order()
        .ToArray();

static bool IsEnabled(string configPath)
{
    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        return doc.RootElement.TryGetProperty("dispatch", out var dispatch) &&
               dispatch.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True;
    }
    catch (Exception e) when (e is IOException or JsonException)
    {
        return false;
    }
}
