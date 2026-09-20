using System.Text.Json;
using ATeam.Dashboard;

var root = FindRepoRoot(AppContext.BaseDirectory) ?? FindRepoRoot(Environment.CurrentDirectory);
if (root is null)
{
    Console.Error.WriteLine("a-team-dashboard: can't find the a-team repo (a folder with bin/a-team and scripts/dispatch.sh).");
    return 1;
}

var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var stateRoot = Environment.GetEnvironmentVariable("A_TEAM_STATE") is { Length: > 0 } explicitState
    ? explicitState
    : Path.Combine(
        Environment.GetEnvironmentVariable("XDG_STATE_HOME") is { Length: > 0 } xdg
            ? xdg
            : Path.Combine(home, ".local", "state"),
        "a-team");

var configRoot = DashboardSettings.ConfigRoot();

var teams = args.Length > 0 ? args : EnabledTeams(Path.Combine(configRoot, "teams"));
if (teams.Length == 0)
{
    Console.Error.WriteLine($"a-team-dashboard: no team in {configRoot}/teams has dispatch.enabled; pass a team name.");
    return 1;
}
var agents = teams.SelectMany(team => new[] { (team, "lead"), (team, "dev") }).ToList();

var settings = new DashboardSettings(configRoot);
BundledThemes.Load(settings.ReadTheme());
using var app = Application.Create();
app.Init();
LogSchemes.Register();
using var window = new DashboardWindow(agents, stateRoot, settings);
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
        if (File.Exists(Path.Combine(dir.FullName, "bin", "a-team")) &&
            File.Exists(Path.Combine(dir.FullName, "scripts", "dispatch.sh")))
            return dir.FullName;
    }
    return null;
}

static string[] EnabledTeams(string teamsDir) =>
    Directory.Exists(teamsDir)
        ? Directory.GetFiles(teamsDir, "*.json")
            .Where(IsEnabled)
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Order()
            .ToArray()
        : [];

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
