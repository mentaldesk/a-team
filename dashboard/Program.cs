using System.Globalization;
using ATeam.Dashboard;
using MentalDesk.Tui.Keys;
using MentalDesk.Tui.Theming;

var root = FindRepoRoot(AppContext.BaseDirectory) ?? FindRepoRoot(Environment.CurrentDirectory);
if (root is null)
{
    Console.Error.WriteLine("ateam: can't find the a-team repo (a folder with bin/a-team and scripts/dispatch.sh).");
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
        $"ateam: no teams in {teams.TeamsDirectory}. Start from examples/team.json.");
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
var news = new WhatsNew(DateTimeOffset.UtcNow);
var writes = new WriteQueue(command.Run);
var macKeys = new MacKeys("ateam");

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
            DocumentHandover document => document.Open(
                arguments => new TeamCommand(document.Editor[0]).Hand(arguments, Path.GetFileName(document.Editor[0])),
                arguments => command.Run(arguments).GetAwaiter().GetResult(),
                Path.Combine(stateRoot, "edits"),
                DateTimeOffset.Now),
            TryHandover tried => tried.Open((arguments, environment) => command.Hand(arguments, environment: environment)),
            _ => handover with { Failure = command.Hand(handover.Arguments) },
        };
    }
    attention.Stop();
    writes.InFlight?.Wait(TimeSpan.FromMinutes(1));
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
    using var active = macKeys.Activate();
    app.Keyboard.KeyDown += (_, _) => attention.Key();
    LogSchemes.Register();
    Handover? handedOver = null;
    var named = wanted.Length > 0 ? wanted : teams.Names();
    var pictures = Pictures.Of(app);
    using var window = new DashboardWindow(
        [.. named.SelectMany(team => teams.Roles(team).Select(role => (team, role)))],
        stateRoot,
        settings,
        teams,
        command.Run,
        team => command.Read("board", team, "waiting"),
        item => command.Read("board", item.Team, "body", item.Number.ToString()),
        url => Link.OpenUrl(url),
        (item, body, onGitHub, onApprove, accept, comment, tryIt, edit, copyBranch, idea, keys, rank) =>
            ReaderDialog.Show(app, item, body, onGitHub, onApprove, accept, comment, tryIt, rank, readerPanes, edit, copyBranch, idea, keys,
                pictures, url => Link.OpenUrl(url)),
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
        newIdea: (teams, team, description, attaching) =>
            NewIdeaDialog.Show(app, teams, team, new IdeaFiler(command.Read), description, attaching),
        readBoard: team => command.Read("board", team, "overview"),
        attention: attention,
        readMoves: (team, since) => command.Read("board", team, "moves", since.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
        whatsNew: news,
        readDocument: item => command.Read("board", item.Team, "document", item.Number.ToString()),
        writes: writes,
        confirmQuit: (running, queued) => QuitDialog.Show(app, running, queued),
        confirmApprove: item => ApproveDialog.Show(app, item),
        attaching: key => new Attaching(key, (team, file) => command.Read("board", team, "attach", "you", file),
            new SystemClipboardImage(Environment.GetEnvironmentVariable), () => AttachFileDialog.Show(app)),
        showSpending: (names, open) => SpendingDialog.Show(app, names, team => command.Read("board", team, "spending"),
            names.SelectMany(teams.Roles), open),
        showDecisions: team => DecisionsDialog.Show(app, team, () => command.Read("board", team, "decisions", "--replaced"),
            change => change.Run(team, command.Read), url => Link.OpenUrl(url)),
        macKeys: macKeys);
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
