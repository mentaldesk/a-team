using ATeam.Dashboard;
using Terminal.Gui.Input;

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

var teams = new TeamConfigs(configRoot);
var named = args.Length > 0 ? args : teams.Names();
if (named.Length == 0)
{
    Console.Error.WriteLine(
        $"a-team-dashboard: no teams in {teams.TeamsDirectory}. Start from examples/team.json.");
    return 1;
}
var agents = named.SelectMany(team => new[] { (team, "lead"), (team, "dev") }).ToList();

var settings = new DashboardSettings(configRoot);
var command = new TeamCommand(Path.Combine(root, "bin", "a-team"));
BundledThemes.Load(settings.ReadTheme());
using var app = Application.Create();
app.Init();
// Esc is Terminal.Gui's built-in quit key; here it goes back to the grid, and quit has a key of its own.
Application.RemoveDefaultKeyBinding(Command.Quit);
LogSchemes.Register();
using var window = new DashboardWindow(agents, stateRoot, settings, teams, command.Run);
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
