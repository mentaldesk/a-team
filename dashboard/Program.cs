using ATeam.Dashboard;
using MentalDesk.Tui.Theming;

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

var requested = args is ["--area", var name, ..] && Enum.TryParse<Area>(name, ignoreCase: true, out var chosen)
    ? chosen
    : (Area?)null;
var wanted = requested is null ? args : args[2..];

var teams = new TeamConfigs(configRoot);
if ((wanted.Length > 0 ? wanted : teams.Names()).Length == 0)
{
    Console.Error.WriteLine(
        $"a-team-dashboard: no teams in {teams.TeamsDirectory}. Start from examples/team.json.");
    return 1;
}

var settings = new DashboardSettings(configRoot);
var command = new TeamCommand(Path.Combine(root, "bin", "a-team"));
var readerPanes = new ReaderPanes();
var start = new TeamStart(
    Path.Combine(root, "examples", "team.json"),
    command,
    new TeamCommand("gh"),
    home,
    () => File.Exists(Path.Combine(home, "Library", "LaunchAgents", "com.a-team.dispatch.plist")));

var recording = new List<Task>();
var attention = new Attention(TimeProvider.System, visit =>
{
    lock (recording)
        recording.Add(command.Run(visit.Arguments));
});

return CrashReport.Guard(Run, stateRoot, args, Console.Error);

int Run()
{
    var terminal = TerminalMode.Save();
    BundledThemes.Cursor = TerminalCursor.ForConsole();
    Handover? back = null;
    while (Show(back) is { } handover)
    {
        BundledThemes.Cursor.Restore();
        terminal.Restore();
        back = handover switch
        {
            TeamsChanged => handover,
            EditorHandover editor => editor with { Failure = editor.Open() },
            _ => handover with { Failure = command.Hand(handover.Arguments) },
        };
    }
    attention.Stop();
    lock (recording)
        Task.WaitAll([.. recording], TimeSpan.FromSeconds(5));
    BundledThemes.Cursor.Restore();
    terminal.Restore();
    return 0;
}

// Handing the terminal over ends the app, which gives the terminal back, and a new one takes it again after.
Handover? Show(Handover? back)
{
    BundledThemes.Load(settings.ReadTheme());
    using var app = Application.Create();
    app.Init();
    app.Keyboard.KeyDown += (_, _) => attention.Key();
    LogSchemes.Register();
    Handover? handedOver = null;
    var named = wanted.Length > 0 ? wanted : teams.Names();
    using var window = new DashboardWindow(
        [.. named.SelectMany(team => teams.Roles(team).Select(role => (team, role)))],
        stateRoot,
        settings,
        teams,
        command.Run,
        team => command.Read("board", team, "waiting"),
        item => command.Read("board", item.Team, "body", item.Number.ToString()),
        url => Link.OpenUrl(url),
        (item, body, onGitHub, onApprove, accept, comment, tryIt, rank) =>
            ReaderDialog.Show(app, item, body, onGitHub, onApprove, accept, comment, tryIt, rank, readerPanes),
        back?.Area ?? requested ?? settings.ReadArea(),
        TerminalIcons.Detect(Environment.GetEnvironmentVariable),
        handover =>
        {
            handedOver = handover;
            app.RequestStop();
        },
        back is TeamsChanged ? null : back,
        start,
        item => command.Read("board", item.Team, "conversation", item.Number.ToString()),
        item => AcceptDialog.Show(app, item),
        showGuide: page => GuideDialog.Show(app, Path.Combine(root, "docs", "guide"), url => Link.OpenUrl(url), page),
        confirmStop: task => StopRunDialog.Show(app, task),
        readHistory: item => command.Read("board", item.Team, "history", item.Number.ToString()),
        pass: new DispatchPass(stateRoot, Path.Combine(root, "bin", "a-team")),
        readTrend: team => command.Read("board", team, "trend"),
        showTrends: teams => TrendsDialog.Show(app, teams, team => command.Read("board", team, "trends")),
        newIdea: (teams, team) => NewIdeaDialog.Show(app, teams, team, new IdeaFiler(command.Read)),
        readBoard: team => command.Read("board", team, "overview"),
        attention: attention);
    window.Refresh();
    app.AddTimeout(TimeSpan.FromSeconds(1), () =>
    {
        window.Refresh();
        return true;
    });
    app.Run(window);
    return handedOver;
}

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
