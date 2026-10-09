using System.Drawing;
using System.Reflection;
using Terminal.Gui;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;

namespace ATeam.Dashboard;

/// <summary>The two areas the app opens in: the agents, or everything waiting on the reviewer.</summary>
public enum Area
{
    Dashboard,
    Work,
}

public sealed class DashboardWindow : Window
{
    private const int MenuLines = 1;
    private const int StatusLines = 1;
    private const int TitleLines = 1;
    private const int DispatchLines = 4;
    private const int BrokeLines = 20;
    private const string BrokeRule = "── the dispatcher's own output since then ──";
    private const int TitleMargin = 4;
    private const int MinCellHeight = 5;
    private const string AllItems = "All items";
    private const string MyItems = "My items";
    private const string LoadingText = "Loading…";
    private const int LoadingPadding = 8;
    private const int LoadingHeight = 5;
    private readonly List<AgentPane> _panes = [];
    private readonly CommandRegistry _commands = new();
    private readonly string _version = Version();
    private readonly AppMenu _menu;
    private readonly StatusBar _status = new();
    private readonly View _agents;
    private readonly FrameView _dispatchFrame;
    private readonly LogView _dispatch;
    private readonly LogView _dispatchAll;
    private readonly WorkView _work;
    private readonly List<string> _teamNames;
    private readonly string _dispatchLog;
    private readonly string _nextPass;
    private readonly string _stateRoot;
    private static readonly string BaseScheme = SchemeManager.SchemesToSchemeName(Schemes.Base)!;
    private static readonly string ErrorScheme = SchemeManager.SchemesToSchemeName(Schemes.Error)!;
    private readonly string _home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private readonly DashboardSettings _settings;
    private readonly TeamConfigs _teams;
    private readonly Func<string[], Task<string?>> _run;
    private readonly TeamStart? _start;
    private readonly TeamChecks? _checks;
    private readonly DispatchPass _pass;
    private readonly Func<string, Task<Reading>> _readWaiting;
    private readonly Func<WaitingItem, Task<Reading>> _readBody;
    private readonly Func<WaitingItem, Task<Reading>>? _readConversation;
    private readonly Func<WaitingItem, Task<Reading>>? _readHistory;
    private readonly Func<string, Task<Reading>>? _readTrend;
    private readonly Action<IReadOnlyList<(string Team, int? Waiting)>>? _showTrends;
    private readonly Func<IReadOnlyList<string>, string, (string Team, int Number)?>? _newIdea;
    private readonly Action<string> _openUrl;
    private readonly Func<WaitingItem, IssueBody, Action, Action?, ReaderCommand?, ReaderComment?, ReaderTry?, Rank?, Rank?> _showBody;
    private readonly Func<WaitingItem, bool> _confirmAccept;
    private readonly Func<RunTask, bool> _confirmStop;
    private readonly Action<string> _showGuide;
    private readonly Action<Handover>? _handOver;
    private readonly IconStyle _auto;
    private readonly FrameView _loading;
    private readonly Label _title;
    private readonly TimeProvider _clock;
    private Area _area;
    private Task<string?>? _pending;
    private (WaitingItem Item, Rank Rank)? _ranking;
    private WaitingItem? _approvable;
    private WaitingItem? _approving;
    private WaitingItem? _shown;
    private WaitingItem? _accepting;
    private WaitingItem? _declined;
    private string? _said;
    private string? _added;
    private WaitingItem? _saidOn;
    private Task<Reading[]>? _reading;
    private Task<Reading[]>? _trending;
    private IReadOnlyList<WorkTrend> _trends = [];
    private (WaitingItem Item, Task<IssueBody> Read, Action<WaitingItem, IssueBody> Then)? _readingBody;
    private DateTimeOffset? _readAt;
    private DateTimeOffset? _askedAt;
    private string? _failure;
    private string? _progress;
    private int? _expanded;
    private bool _onDispatcher;
    private bool _dispatcherExpanded;
    private IReadOnlyList<string> _broke = [];
    private (long Length, DateTime Written) _wholeRead;
    private IReadOnlyList<LogLine> _whole = [];
    private IconStyle _drawn = IconStyle.Unicode;
    private AgentPane? _lastSelected;
    private Size _laidOutOver;
    private Handover? _resume;
    private Place? _left;
    private string[] _activityRead = [];
    private (IssueBody Body, int Top)? _triedFrom;
    private (WaitingItem Item, ReaderPlace Place, string? Failure)? _reopen;
    private (string Text, Schemes Scheme)? _copied;
    private readonly IClipboard? _clipboard;

    public DashboardWindow(
        IReadOnlyList<(string Team, string Role)> agents,
        string stateRoot,
        DashboardSettings settings,
        TeamConfigs teams,
        Func<string[], Task<string?>> run,
        Func<string, Task<Reading>> readWaiting,
        Func<WaitingItem, Task<Reading>> readBody,
        Action<string> openUrl,
        Func<WaitingItem, IssueBody, Action, Action?, ReaderCommand?, ReaderComment?, ReaderTry?, Rank?, Rank?> showBody,
        Area area,
        IconStyle auto,
        Action<Handover>? handOver = null,
        Handover? resume = null,
        TeamStart? start = null,
        Func<WaitingItem, Task<Reading>>? readConversation = null,
        Func<WaitingItem, bool>? confirmAccept = null,
        TimeProvider? clock = null,
        Action<string>? showGuide = null,
        Func<RunTask, bool>? confirmStop = null,
        IClipboard? clipboard = null,
        Func<WaitingItem, Task<Reading>>? readHistory = null,
        TeamChecks? checks = null,
        DispatchPass? pass = null,
        Func<string, Task<Reading>>? readTrend = null,
        Action<IReadOnlyList<(string Team, int? Waiting)>>? showTrends = null,
        Func<IReadOnlyList<string>, string, (string Team, int Number)?>? newIdea = null)
    {
        _clipboard = clipboard;
        _newIdea = newIdea;
        _readHistory = readHistory;
        _readTrend = readTrend;
        _showTrends = showTrends;
        _pass = pass ?? new DispatchPass(stateRoot, "a-team");
        _checks = checks ?? (start is null ? null : new TeamChecks(start.Check, teams.Stamp));
        _showGuide = showGuide ?? (_ => { });
        _start = start;
        _clock = clock ?? TimeProvider.System;
        BorderStyle = LineStyle.None;
        _settings = settings;
        _auto = auto;
        _teams = teams;
        _run = run;
        _readWaiting = readWaiting;
        _readBody = readBody;
        _readConversation = readConversation;
        _openUrl = openUrl;
        _showBody = showBody;
        _confirmAccept = confirmAccept ?? (_ => false);
        _confirmStop = confirmStop ?? (_ => false);
        _handOver = handOver;
        _area = area;
        _dispatchLog = Path.Combine(stateRoot, "dispatch.log");
        _nextPass = Path.Combine(stateRoot, "next-pass");
        _stateRoot = stateRoot;
        _teamNames = [.. agents.Select(agent => agent.Team).Distinct()];

        _agents = new View
        {
            X = 0,
            Y = MenuLines,
            Width = Dim.Fill(),
            Height = Dim.Func(_ => Math.Max(0, Viewport.Height - Foot() - MenuLines), this),
            CanFocus = true,
            Visible = area == Area.Dashboard,
        };
        _agents.VerticalScrollBar.VisibilityMode = ScrollBarVisibilityMode.Auto;
        _agents.SubViewLayout += (_, _) => FitGrid();
        Add(_agents);

        var expandToolCalls = settings.ReadExpandToolCalls();
        for (var i = 0; i < agents.Count; i++)
        {
            var (team, role) = agents[i];
            var pane = new AgentPane(team, role, Path.Combine(stateRoot, team, role), expandToolCalls);
            pane.X = Pos.Func(_ => Cell(_panes.IndexOf(pane)).X, this);
            pane.Y = Pos.Func(_ => Cell(_panes.IndexOf(pane)).Y, this);
            pane.Width = Dim.Func(_ => Cell(_panes.IndexOf(pane)).Width, this);
            pane.Height = Dim.Func(_ => Cell(_panes.IndexOf(pane)).Height, this);
            pane.HasFocusChanged += (_, e) =>
            {
                if (!e.NewValue)
                    return;
                _lastSelected = pane;
                if (_onDispatcher)
                {
                    _onDispatcher = false;
                    ShowDispatcher(_clock.GetUtcNow());
                }
            };
            _panes.Add(pane);
            _agents.Add(pane);
        }

        _dispatchFrame = new FrameView
        {
            Title = "dispatcher",
            X = 0,
            Y = Pos.Func(_ => _dispatcherExpanded ? MenuLines : Math.Max(0, Viewport.Height - Foot()), this),
            Width = Dim.Fill(),
            Height = Dim.Func(_ => _dispatcherExpanded ? Math.Max(0, Viewport.Height - MenuLines - StatusLines) : DispatchLines + 2, this),
            CanFocus = true,
            Visible = area == Area.Dashboard,
        };
        _dispatchFrame.HasFocusChanged += (_, e) =>
        {
            if (e.NewValue && !_onDispatcher)
            {
                _onDispatcher = true;
                ShowDispatcher(_clock.GetUtcNow());
            }
        };
        _dispatch = new LogView { Width = Dim.Fill(), Height = Dim.Fill(), Elides = true, SchemeName = BaseScheme };
        _dispatchAll = new LogView { Width = Dim.Fill(), Height = Dim.Fill(), Elides = true, Scrolls = true, SchemeName = BaseScheme, Visible = false };
        _dispatchFrame.Add(_dispatch, _dispatchAll);
        Add(_dispatchFrame);

        _title = new Label { X = 1, Y = MenuLines, Width = Dim.Fill(1), Text = "Work", Visible = area == Area.Work };
        Add(_title);
        _work = new WorkView(_teamNames)
        {
            X = 0,
            Y = MenuLines + TitleLines,
            Width = Dim.Fill(),
            Height = Dim.Func(_ => WorkHeight(), this),
            Visible = area == Area.Work,
        };
        _work.FocusChanged += ShowMessage;
        _work.ShowOnlyMine(settings.ReadOnlyMine());
        Add(_work);
        _loading = new FrameView
        {
            X = Pos.Center(),
            Y = Pos.Func(_ => MenuLines + TitleLines + Math.Max(0, (WorkHeight() - LoadingHeight) / 2), this),
            Width = LoadingText.Length + (LoadingPadding * 2) + 2,
            Height = LoadingHeight,
            CanFocus = false,
            Visible = false,
        };
        _loading.Add(new Label { Text = LoadingText, X = Pos.Center(), Y = Pos.Center() });
        Add(_loading);
        ShowIcons(settings.ReadIcons());

        _status.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - StatusLines), this);
        Add(_status);

        RegisterCommands();
        _commands.Apply(settings.ReadKeys());
        SyncQuitKey();
        _menu = new AppMenu(_commands, _area);
        _menu.Bar.X = 0;
        _menu.Bar.Y = 0;
        Add(_menu.Bar);

        if (resume is not null)
            Resume(resume);
        if (resume is null or TeamsChanged && _area == Area.Work)
            ReadWaiting();
    }

    internal IReadOnlyList<AgentPane> Panes => _panes;

    internal View Dispatcher => _dispatchFrame;

    internal LogView DispatchLog => _dispatch;

    internal LogView WholeDispatchLog => _dispatchAll;

    internal bool DispatcherSelected => _onDispatcher;

    internal bool DispatcherExpanded => _dispatcherExpanded;

    internal View Agents => _agents;

    internal WorkView Work => _work;

    internal MessageBar Message => _status.Message;

    internal StatusBar Status => _status;

    internal MenuBar Menu => _menu.Bar;

    internal IReadOnlyList<(string Id, MenuItem Item)> MenuItems => _menu.Items;

    internal IReadOnlyList<MenuBarItem> Menus => _menu.Menus;

    internal Area CurrentArea => _area;

    internal int? ExpandedAgent => _expanded;

    internal CommandRegistry Commands => _commands;

    internal FrameView Loading => _loading;

    /// <summary>How long Work, in front with nothing over it, goes before it reads again by itself.</summary>
    internal static readonly TimeSpan ReadEvery = TimeSpan.FromMinutes(5);

    public void Refresh()
    {
        Settle();
        SettleTrend();
        ShowTitle();
        var now = _clock.GetUtcNow();
        if (_area == Area.Work && Uncovered && (_askedAt is not { } asked || now - asked >= ReadEvery))
            ReadWaiting();
        DateTimeOffset? nextCheck = long.TryParse(ReadText(_nextPass), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
        var paused = _panes.Select(pane => pane.Team).Distinct().ToDictionary(team => team, _teams.IsPaused);
        _checks?.Follow(paused.Keys);
        if (_checks?.Answered() is [.., var failed])
            _failure = failed;
        var passing = DispatcherState.Passing(_stateRoot);
        foreach (var pane in _panes)
            pane.Refresh(now, nextCheck, paused[pane.Team], _teams.IsHeld(pane.Team, pane.Role), _checks?.Fatal(pane.Team), passing);

        var tail = ReadTail(_dispatchLog, DispatchLines);
        if (!_dispatch.Lines.SequenceEqual(tail))
            _dispatch.Lines = tail;
        _broke = DispatcherState.Broke(_stateRoot, BrokeLines);
        if (_dispatcherExpanded)
            ShowWholeLog();
        ShowDispatcher(now);

        _menu.Refresh();
        ShowMessage();
        ShowLoading();
    }

    private void ShowDispatcher(DateTimeOffset now)
    {
        var state = DispatcherState.Read(_stateRoot, now, _home);
        if (_broke.Count > 0 && !_dispatcherExpanded)
            state = state.Failed();
        var marker = _onDispatcher ? Icons.Field(Icon.Selected, _drawn) : "";
        var width = _dispatchFrame.Frame.Width > 0 ? _dispatchFrame.Frame.Width - TitleMargin - marker.GetColumns() : int.MaxValue;
        var title = marker + state.Title(width);
        if (_dispatchFrame.Title != title)
            _dispatchFrame.Title = title;
        var scheme = state.Error ? ErrorScheme : BaseScheme;
        if (_dispatchFrame.SchemeName != scheme)
            _dispatchFrame.SchemeName = scheme;
    }

    /// <summary>Reads dispatch.log afresh only when it has changed: it is never trimmed.</summary>
    private void ShowWholeLog()
    {
        var file = new FileInfo(_dispatchLog);
        var stamp = file.Exists ? (file.Length, file.LastWriteTimeUtc) : default;
        if (stamp != _wholeRead || _whole.Count == 0)
        {
            _wholeRead = stamp;
            _whole = ReadTail(_dispatchLog, int.MaxValue);
        }
        IReadOnlyList<LogLine> lines = _broke.Count == 0
            ? _whole
            : [.. _whole, new LogLine(BrokeRule, LogLineKind.DispatchFailed), .. _broke.Select(line => new LogLine(line, LogLineKind.DispatchFailed))];
        if (!_dispatchAll.Lines.SequenceEqual(lines))
            _dispatchAll.Lines = lines;
    }

    /// <summary>When the Work area was last read, for the status bar.</summary>
    internal static string Stamped(DateTimeOffset? at, DateTimeOffset now) =>
        at is { } read ? $"read {AgentPane.Ago(now - read)} ago" : "";

    /// <summary>While a menu is open it owns the keyboard: its own keys would otherwise run a command as well.</summary>
    protected override bool OnKeyDown(Key key)
    {
        if (_copied is not null)
        {
            _copied = null;
            ShowMessage();
        }
        return (!_menu.Bar.IsOpen() && _commands.Press(key)) || base.OnKeyDown(key);
    }

    private void RegisterCommands()
    {
        bool OnDashboard() => _area == Area.Dashboard;
        bool OnWork() => _area == Area.Work;
        bool AnyAgents() => OnDashboard() && _panes.Count > 0;
        bool Selection() => OnDashboard() && Selected() is not null;
        bool Scrollable() => Selection() || (OnDashboard() && _dispatcherExpanded);
        bool OnGrid() => AnyAgents() && _expanded is null;
        bool Reading() => Selection() && _expanded is not null;
        _commands
            .Register("agent.next", "Select the next agent", () => Step(+1), Key.Tab, isEnabled: AnyAgents)
            .Register("agent.previous", "Select the previous agent", () => Step(-1), Key.Tab.WithShift, isEnabled: AnyAgents)
            .Register("agent.right", "Select the agent to the right", () => MoveSelection(0, +1), Key.CursorRight, isEnabled: AnyAgents)
            .Register("agent.left", "Select the agent to the left", () => MoveSelection(0, -1), Key.CursorLeft, isEnabled: AnyAgents)
            .Register("agent.down", "Select the agent below", () => MoveSelection(+1, 0), Key.CursorDown, isEnabled: OnGrid)
            .Register("agent.up", "Select the agent above", () => MoveSelection(-1, 0), Key.CursorUp, isEnabled: OnGrid)
            .Register("log.lineDown", "Select the next line of the log", () => Selected()?.MoveLine(+1, extend: false), Key.CursorDown, isEnabled: Reading)
            .Register("log.lineUp", "Select the line above in the log", () => Selected()?.MoveLine(-1, extend: false), Key.CursorUp, isEnabled: Reading)
            .Register("log.extendDown", "Extend the selection down", () => Selected()?.MoveLine(+1, extend: true), Key.CursorDown.WithShift, isEnabled: Reading)
            .Register("log.extendUp", "Extend the selection up", () => Selected()?.MoveLine(-1, extend: true), Key.CursorUp.WithShift, isEnabled: Reading)
            .Register("agent.expand", () => "Expand the selected agent", () => Expand(), Key.Enter,
                isEnabled: () => Selection() || (OnDashboard() && _onDispatcher && !_dispatcherExpanded),
                menuLabel: () => "Expand", inMenu: () => _expanded is null && !_dispatcherExpanded)
            .Register("log.pageUp", "Scroll the log up", () => Scroll(pane => pane.Page(-1), log => log.Page(-1)), Key.PageUp, isEnabled: Scrollable)
            .Register("log.pageDown", "Scroll the log down", () => Scroll(pane => pane.Page(+1), log => log.Page(+1)), Key.PageDown, isEnabled: Scrollable)
            .Register("log.top", "Jump to the top of the log", () => Scroll(pane => pane.Home(), log => log.Home()), Key.Home, isEnabled: Scrollable)
            .Register("log.bottom", "Jump to the bottom of the log", () => Scroll(pane => pane.End(), log => log.End()), Key.End, isEnabled: Scrollable)
            .Register("log.toolCalls", "Show tool calls in full", () => Selected()?.ToggleToolCalls(), new Key('t'), isEnabled: Selection)
            .Register("log.copyLines", () => "Copy the selected lines", () => Copy(pane => pane.CopySelection()), new Key('l'), isEnabled: Reading,
                menuLabel: () => "Copy selected lines", inMenu: () => _expanded is not null)
            .Register("log.copyAll", () => "Copy the whole log", () => Copy(pane => pane.CopyAll()), new Key('L'), isEnabled: Reading,
                menuLabel: () => "Copy whole log", inMenu: () => _expanded is not null)
            .Register("log.editor", () => "Open the whole log in your editor", OpenInEditor, new Key('e'), isEnabled: Reading,
                menuLabel: () => "Open whole log in editor", inMenu: () => _expanded is not null)
            .Register("work.right", "Select the column to the right", () => _work.MoveColumn(+1), Key.CursorRight, isEnabled: OnWork)
            .Register("work.left", "Select the column to the left", () => _work.MoveColumn(-1), Key.CursorLeft, isEnabled: OnWork)
            .Register("work.down", "Select the card below", () => _work.MoveCard(+1), Key.CursorDown, isEnabled: OnWork)
            .Register("work.up", "Select the card above", () => _work.MoveCard(-1), Key.CursorUp, isEnabled: OnWork)
            .Register("work.read", "Open", ReadSelected, Key.Enter, isEnabled: () => OnWork() && _work.Selected is not null, onCard: true)
            .Register("work.priority", "Set priority", SetPriority, new Key('p'), isEnabled: () => OnWork() && _work.SelectedCard is not null, onCard: true)
            .Register("work.try", "Try", Try, new Key('t'), isEnabled: () => OnWork() && _work.Selected is { Triable: true }, onCard: true)
            .Register("work.github", "Open on GitHub", OpenSelected, new Key('g'), isEnabled: () => OnWork() && _work.SelectedUrl is { Length: > 0 }, onCard: true)
            .Register("team.board", "Open team's board on GitHub", OpenBoard, new Key('b'), isEnabled: () => SelectedTeam() is not null)
            .Register("work.approve", "Approve the pitch you're reading", Approve, new Key('a'), isEnabled: () => _approvable is not null)
            .Register("work.accept", "Accept", () => Accept(), new Key('a'), isEnabled: () => Acceptable() is not null, onCard: true)
            .Register("work.comment", "Comment on the item you're reading", () => { }, new Key('c'), isEnabled: () => _shown is not null)
            .Register("work.decline", "Decline", () => ReadSelected(declining: true), new Key('x'),
                isEnabled: () => OnWork() && _work.SelectedCard is { Declinable: true }, onCard: true)
            .Register("work.nextTeam", () => "Next team", () => _work.MoveTeam(+1), Key.PageDown.WithCtrl, isEnabled: OnWork, inMenu: OnWork)
            .Register("work.previousTeam", () => "Previous team", () => _work.MoveTeam(-1), Key.PageUp.WithCtrl, isEnabled: OnWork, inMenu: OnWork)
            .Register("work.team", () => "Go to team…", GoToTeam, isEnabled: OnWork, inMenu: OnWork)
            .Register("work.new", "New idea", NewIdea, new Key('n'), isEnabled: () => _newIdea is not null && _teamNames.Count > 0)
            .Register("work.mine", () => "Show only what's your move", ToggleOnlyMine, new Key('m'), isEnabled: OnWork,
                menuLabel: () => _work.OnlyMine ? "Show all" : "Show only mine")
            .Register("work.refresh", () => "Read what's waiting again", ReadWaiting, Key.F5, isEnabled: OnWork,
                menuLabel: () => "Refresh")
            .Register("view.dashboard", "Dashboard", () => Show(Area.Dashboard), new Key('d'))
            .Register("view.work", "Work", () => Show(Area.Work), new Key('w'))
            .Register("agent.hold", () => UnlessHeld("Pause selected agent's role", "Let selected agent's role start again"), ToggleHold, new Key('h'),
                isEnabled: () => OnDashboard() && Selected() is not null,
                menuLabel: () => UnlessHeld("Pause this role", "Let this role start again"))
            .Register("agent.interrupt", () => UnlessStopped("Interrupt selected agent", "Let selected agent start again"), ToggleInterrupt, new Key('i'),
                isEnabled: () => OnDashboard() && Selected() is { Running: true } or { Held: true } or { RunHeld: true },
                menuLabel: () => UnlessStopped("Interrupt", "Let it start again"))
            .Register("dispatch.pass", "Run a dispatch pass now", PassNow)
            .Register("commands", "Commands", OpenCommands, Key.E.WithCtrl, isEnabled: HasApp)
            .Register("settings", "Settings", () => OpenSettings(), new Key('s'), isEnabled: HasApp)
            .Register("teams", "Teams", () => OpenSettings(SettingsDialog.TeamsPage), isEnabled: HasApp)
            .Register("teams.new", "New team", () => OpenSettings(SettingsDialog.TeamsPage, newTeam: true), isEnabled: HasApp)
            .Register("help", "Keys", OpenHelp, Key.F1, isEnabled: HasApp)
            .Register("guide", "Guide", () => _showGuide(OnWork() ? GuideDialog.Work : GuideDialog.Dashboard))
            .Register("about", "About", OpenAbout, isEnabled: HasApp)
            .Register("trends", "Trends", OpenTrends, isEnabled: () => _showTrends is not null)
            .Register("agent.collapse", () => "Back to the agent grid", Collapse, Key.Esc,
                isEnabled: () => OnDashboard() && (_expanded is not null || _dispatcherExpanded), menuLabel: () => "Back to all agents",
                inMenu: () => _expanded is not null || _dispatcherExpanded)
            .Register("quit", "Quit", () => App?.RequestStop(), new Key('q'));
        for (var tab = 0; tab < 9; tab++)
        {
            var index = tab;
            _commands.Register($"work.team{index + 1}",
                () => index < _work.Lanes.Count ? $"Go to {_work.Lanes[index].Team}" : $"Go to team {index + 1}",
                () => _work.PickTab(index), new Key((char)('1' + index)), isEnabled: () => OnWork() && index < _work.Lanes.Count);
        }
    }

    // Point Terminal.Gui's own Quit binding at our quit key: removing it leaves PopoverImpl binding Key.Empty, which throws.
    private void SyncQuitKey()
    {
        var key = _commands.KeyFor("quit");
        if (key != Key.Empty)
            Application.SetDefaultKeyBinding(Command.Quit, Bind.All(key));
    }

    private bool HasApp() => App is not null;

    private void ToggleHold()
    {
        if (_pending is not null || Selected() is not { } pane)
            return;
        _progress = pane.Held ? "Letting it start again…" : "Pausing this role…";
        ShowMessage();
        _pending = _run([pane.Held ? "resume" : "pause", pane.Team, pane.Role]);
    }

    private void PassNow()
    {
        _failure = null;
        _said = null;
        _pass.Start();
        ShowMessage();
    }

    private string UnlessHeld(string label, string resume) => Selected() is { Held: true } ? resume : label;

    private string UnlessStopped(string label, string resume) => Selected() is { Held: true } or { RunHeld: true } ? resume : label;

    /// <summary>With several Dev runs, acts on the shown run's task alone; otherwise on the role, as with one run.</summary>
    private void ToggleInterrupt()
    {
        if (_pending is not null || Selected() is not { } pane || !(pane.Running || pane.Held || pane.RunHeld))
            return;
        var task = pane is { Held: true, RunHeld: false } ? null : pane.RunTask;
        var resume = pane.Held || pane.RunHeld;
        if (!resume && pane.SessionId is not null)
        {
            _handOver?.Invoke(new AttachHandover(pane.Team, pane.Role, task?.Number));
            return;
        }
        if (!resume && task is not null && !_confirmStop(task))
            return;
        _progress = resume ? "Letting it start again…" : "Interrupting…";
        ShowMessage();
        string[] target = task is null ? [pane.Team, pane.Role] : [pane.Team, pane.Role, task.Number.ToString()];
        _pending = _run([resume ? "resume" : "stop", .. target]);
    }

    /// <summary>Writes every line of the shown session to a file and hands the terminal to your editor with it.</summary>
    private void OpenInEditor()
    {
        if (Selected() is not { LogPath: { } log } pane)
            return;
        var file = Path.Combine(Path.GetTempPath(), $"a-team-{pane.Team}-{pane.Role}-{_clock.GetUtcNow():yyyyMMddHHmmss}.log");
        try
        {
            File.WriteAllLines(file, SessionLog.Whole(log));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _failure = $"couldn't write the log for your editor: {e.Message}";
            ShowMessage();
            return;
        }
        _handOver?.Invoke(new EditorHandover(
            pane.Team, pane.Role, pane.Place, file, EditorHandover.Command(Environment.GetEnvironmentVariable)));
    }

    /// <summary>No dialog is on top of the window and no menu is open over it.</summary>
    private bool Uncovered => (IsModal || !IsRunning) && !_menu.Bar.IsOpen();

    /// <summary>Reads every team's gates at once. A second go while one is running is refused, not queued.</summary>
    private void ReadWaiting()
    {
        _askedAt = _clock.GetUtcNow();
        if (_reading is null)
        {
            _activityRead = Activity();
            _reading = Task.WhenAll(_teamNames.Select(team => _readWaiting(team)));
        }
        ShowMessage();
        ShowLoading();
    }

    private string[] Activity() => [.. _panes.Select(pane => pane.Activity)];

    /// <summary>The last read landed, began under five minutes ago, and no run has started or finished since.</summary>
    private bool UpToDate() =>
        _askedAt is { } asked && _readAt >= asked && _clock.GetUtcNow() - asked < ReadEvery
        && _activityRead.SequenceEqual(Activity());

    /// <summary>There are no cards to look at until the first read lands; a later read leaves the ones already
    /// on screen where they are.</summary>
    private void ShowLoading() => _loading.Visible = _area == Area.Work && _reading is not null && _work.Unread;

    private void ToggleOnlyMine()
    {
        _work.ShowOnlyMine(!_work.OnlyMine);
        _settings.WriteOnlyMine(_work.OnlyMine);
        _menu.Refresh();
        ShowMessage();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Hands the terminal to <c>a-team try</c> for the card's PR, from its own row or the PR's.</summary>
    private void Try()
    {
        if (_work.Selected is { Triable: true } item)
            HandOverTry(item);
    }

    private void HandOverTry(WaitingItem item, ReaderPlace? reader = null) =>
        _handOver?.Invoke(new TryHandover(item, _work.SelectedCard is null, _work.Items, _readAt, reader));

    /// <summary>Back from a try, the cards as they were with no re-read; from an attach, the grid. Either way, what
    /// went wrong if it failed.</summary>
    private void Resume(Handover handover)
    {
        _resume = handover;
        _failure = handover.Failure;
        if (handover is TeamsChanged changed)
            _left = changed.Left;
        if (handover is TryHandover tried)
        {
            _readAt = _askedAt = tried.ReadAt;
            _work.Show(tried.Items);
            ReadTrend();
            if (tried.Reader is { } place)
            {
                _reopen = (tried.Item, place, _failure);
                _failure = null;
            }
        }
        ShowMessage();
    }

    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        if (newIsRunning)
        {
            FocusResumed();
            App?.Invoke(ReopenReader);
        }
    }

    /// <summary>Back from a try started in the reader, the same reader, where it was.</summary>
    internal void ReopenReader()
    {
        if (_reopen is not { } reopen)
            return;
        _reopen = null;
        ShowBody(reopen.Item, reopen.Place.Body, reopen.Place.Url, reopen.Place.Top, reopen.Failure);
    }

    internal void FocusResumed()
    {
        if (_resume is not { } resume)
            return;
        _resume = null;
        switch (resume)
        {
            case TryHandover tried when !_work.Focus(tried.Item, tried.OnPr):
                _work.FocusFirstCard();
                break;
            case EditorHandover edited when _panes.FindIndex(pane => pane.Team == edited.Team && pane.Role == edited.Role) is >= 0 and var index:
                SetExpanded(index);
                _panes[index].SetFocus();
                _panes[index].Restore(edited.Place);
                break;
            case AttachHandover attached when _panes.FindIndex(pane => pane.Team == attached.Team && pane.Role == attached.Role) is >= 0 and var index:
                Select(index);
                break;
        }
    }

    private void OpenSelected()
    {
        if (_work.SelectedUrl is { Length: > 0 } url)
            _openUrl(url);
    }

    /// <summary>The team of the selected column in Work, or of the selected agent on the dashboard.</summary>
    internal string? SelectedTeam() => _area == Area.Work ? _work.Team : Selected()?.Team;

    private void OpenBoard()
    {
        if (SelectedTeam() is not { } team)
            return;
        if (_teams.BoardUrl(team) is not { } url)
        {
            _failure = $"{team}'s config names no project board";
            ShowMessage();
            return;
        }
        _openUrl(url);
        _failure = null;
        _said = $"Opened {team}'s board";
        _saidOn = _work.Selected;
        ShowMessage();
    }

    /// <summary>Opens the reader with the ranks under it, starting on the card's own.</summary>
    private void SetPriority()
    {
        if (_work.SelectedCard is { } item)
            ReadRanking(item);
    }

    private void ReadSelected() => ReadSelected(declining: false);

    /// <summary>A card in Triage is waiting for a rank, so it opens with the ranks under it.</summary>
    private void ReadSelected(bool declining)
    {
        if ((declining ? _work.SelectedCard : _work.Selected) is not { } item)
            return;
        if (_work.InTriage && _work.SelectedCard is { } card)
        {
            ReadRanking(card, declining);
            return;
        }
        var url = _work.SelectedUrl;
        ReadBody(item, (read, body) => ShowBody(read, body, url, declining: declining), forReader: true);
    }

    private void ReadRanking(WaitingItem item, bool declining = false)
    {
        var url = _work.SelectedUrl;
        ReadBody(item, (read, body) => ShowBody(read, body, url, rank: Priorities.Starting(read), declining: declining),
            forReader: true);
    }

    private void ReadBody(WaitingItem item, Action<WaitingItem, IssueBody> then, bool forReader = false)
    {
        if (_pending is not null || _readingBody is not null)
            return;
        _failure = null;
        _progress = $"Reading #{item.Number}…";
        ShowMessage();
        _readingBody = (item, Read(item, forReader), then);
    }

    /// <summary>The reads start at once; a conversation or history that won't read still leaves the body to show.
    /// A question is shown as asked, so only its history is read.</summary>
    private async Task<IssueBody> Read(WaitingItem item, bool forReader)
    {
        var question = forReader && item.Question.Length > 0;
        var body = question ? null : _readBody(item);
        var conversation = forReader && !question ? _readConversation?.Invoke(item) : null;
        var history = forReader ? _readHistory?.Invoke(item) : null;
        var read = body is null ? new IssueBody(item.Question)
            : await Settled(body, reading => IssueBody.Of(reading, item.Number),
                new IssueBody(Failure: $"couldn't read #{item.Number}")).ConfigureAwait(false);
        if (read.Failure is not null)
            return read;
        if (conversation is not null)
            read = read.With(await Settled(conversation, reading => Conversation.Of(reading, item.Number),
                new Conversation([], $"couldn't read the conversation on #{item.Number}")).ConfigureAwait(false));
        if (history is not null)
            read = read with
            {
                History = await Settled(history, reading => History.Of(reading, item.Number),
                    new History([], Failure: $"couldn't read #{item.Number}'s history")).ConfigureAwait(false),
            };
        return read;
    }

    private static async Task<T> Settled<T>(Task<Reading> reading, Func<Reading, T> of, T otherwise)
    {
        try
        {
            return of(await reading.ConfigureAwait(false));
        }
        catch (Exception)
        {
            return otherwise;
        }
    }

    /// <summary>Nothing to read opens no dialog: the bar says why and you stay on the board. With
    /// <paramref name="rank"/> it opens anyway, saying why, since you came to rank it.</summary>
    private void ShowBody(WaitingItem item, IssueBody body, string? url, int top = 0, string? failure = null,
        Rank? rank = null, bool declining = false)
    {
        if (rank is null && body.Failure is { Length: > 0 } unread)
            _failure = unread;
        else if (rank is null && string.IsNullOrWhiteSpace(body.Text))
            _failure = $"#{item.Number} has no description";
        else
        {
            _approvable = item.Approvable ? item : null;
            _shown = item;
            var chosen = _showBody(item, body, () =>
            {
                if (url is { Length: > 0 })
                    _openUrl(url);
            }, _approvable is null ? null : () => _commands.Execute("work.approve"),
                item.Acceptable ? new ReaderCommand(_commands.KeyFor("work.accept"), "accept", Accept, item.Unacceptable.Length == 0) : null,
                new ReaderComment(_commands.KeyFor("work.comment"), "comment", body => Post(item, body), _clock,
                    item.Declinable ? new ReaderDecline(_commands.KeyFor("work.decline"), reason => Decline(item, reason), declining) : null),
                item.Triable ? new ReaderTry(_commands.KeyFor("work.try"), (shown, at) => _triedFrom = (shown, at), top, failure) : null,
                rank);
            _approvable = null;
            _shown = null;
            if (_declined == item)
            {
                Declined(item);
                return;
            }
            if (chosen is { } ranked && ranked != Priorities.Of(item))
                SetRank(item, ranked);
            if (_triedFrom is { } from)
            {
                _triedFrom = null;
                HandOverTry(item, new ReaderPlace(from.Body, url, from.Top));
            }
        }
    }

    /// <summary>Adds an idea to the team whose lane you're in, or the first team's, and reads Work again to show it.</summary>
    private void NewIdea()
    {
        var team = _area == Area.Work && _work.Current is { } lane ? lane.Team : _teamNames[0];
        if (_newIdea?.Invoke(_teamNames, team) is not { } added)
            return;
        _failure = null;
        _added = $"#{added.Number} added to {added.Team}'s ideas";
        ReadWaiting();
    }

    /// <summary>Approves the pitch the reader is showing. Like a rank, the card stays put until the board takes it.</summary>
    private void Approve()
    {
        if (_approvable is not { } item || _pending is not null)
            return;
        _approving = item;
        _progress = $"Approving #{item.Number}…";
        ShowMessage();
        _pending = _run(["board", item.Team, "approve", "you", item.Number.ToString()]);
    }

    private void Approved(WaitingItem item)
    {
        _work.Approved(item);
        _said = $"#{item.Number} approved";
        _saidOn = _work.Selected;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>The item the reader is showing, or else the one selected on the board, when it's In review.</summary>
    private WaitingItem? Acceptable() =>
        (_shown ?? (_area == Area.Work ? _work.Selected : null)) is { Acceptable: true } item ? item : null;

    /// <summary>Merges the task's PR or closes the pitch once you've said so, or says why it can't yet. True once
    /// it's under way.</summary>
    private bool Accept()
    {
        if (Acceptable() is not { } item || _pending is not null)
            return false;
        if (item.Unacceptable is { Length: > 0 } why)
        {
            _failure = why;
            ShowMessage();
            return false;
        }
        if (!_confirmAccept(item))
            return false;
        _accepting = item;
        _failure = null;
        _progress = item.Pitch ? $"Closing #{item.Number}…" : $"Merging PR #{item.Pr}…";
        ShowMessage();
        _pending = _run(["board", item.Team, "accept", "you", item.Number.ToString()]);
        return true;
    }

    private Task<string?> Post(WaitingItem item, string body) => WithFile(item, "comment", body);

    /// <summary>Closes the item as not planned with your reason. The reader closes on it, and the card leaves.</summary>
    private async Task<string?> Decline(WaitingItem item, string reason)
    {
        var refused = await WithFile(item, "decline", reason).ConfigureAwait(false);
        if (refused is null or { Length: 0 })
            _declined = item;
        return refused;
    }

    private void Declined(WaitingItem item)
    {
        _declined = null;
        _work.Leave(item);
        _failure = null;
        _said = $"#{item.Number} declined";
        _saidOn = _work.Selected;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private async Task<string?> WithFile(WaitingItem item, string command, string text)
    {
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, text).ConfigureAwait(false);
            return await _run(["board", item.Team, command, "you", item.Number.ToString(), file]).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private void Merged(WaitingItem item)
    {
        _work.Leave(item);
        _said = item.Pitch ? $"accepted #{item.Number}" : $"merged PR #{item.Pr}";
        _saidOn = _work.Selected;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Writes the rank. The board decides what the field will take, so an unknown value comes back as a
    /// refusal rather than being guessed at here.</summary>
    private void SetRank(WaitingItem item, Rank rank)
    {
        if (_pending is not null)
            return;
        _ranking = (item, rank);
        _progress = "Setting…";
        ShowMessage();
        _pending = _run(["board", item.Team, "priority", "you", item.Number.ToString(), Priorities.Value(rank)]);
    }

    /// <summary>What the board took: the card moves to the column its new Priority puts it in, and the bar says
    /// so until the selection moves off the card the selection was left on.</summary>
    private void Ranked(WaitingItem item, Rank rank)
    {
        _work.Ranked(item, rank);
        _said = $"#{item.Number} · set to {rank}";
        _saidOn = _work.Selected;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Picked up by the next refresh, so a command runs off the draw loop and reports back on it.</summary>
    private void Settle()
    {
        if (_pending is { IsCompleted: true } finished)
        {
            _pending = null;
            _progress = null;
            _failure = finished.Status == TaskStatus.RanToCompletion
                ? finished.Result
                : finished.Exception?.GetBaseException().Message ?? "the command didn't finish";
            // Nothing moves until the board has taken it: a refused write leaves the card as it was.
            if (_ranking is { } ranking)
            {
                _ranking = null;
                if (_failure is null or { Length: 0 })
                    Ranked(ranking.Item, ranking.Rank);
            }
            if (_approving is { } approving)
            {
                _approving = null;
                if (_failure is null or { Length: 0 })
                    Approved(approving);
            }
            if (_accepting is { } accepting)
            {
                _accepting = null;
                if (_failure is null or { Length: 0 })
                    Merged(accepting);
            }
        }

        if (_pass.Finished() is { } passed)
        {
            if (passed.Scheme == Schemes.Error)
                _failure = passed.Text;
            else
            {
                _said = passed.Text;
                _saidOn = _work.Selected;
            }
        }

        if (_readingBody is { Read.IsCompleted: true } body)
        {
            _readingBody = null;
            _progress = null;
            body.Then(body.Item, body.Read.Result);
        }

        if (_reading is not { IsCompleted: true } read)
            return;
        _reading = null;
        var readings = read.Status == TaskStatus.RanToCompletion ? read.Result : null;
        var failure = readings is null
            ? read.Exception?.GetBaseException().Message ?? "the read didn't finish"
            : readings.Select(reading => reading.Failure).FirstOrDefault(line => line is { Length: > 0 });
        if (failure is { Length: > 0 })
        {
            // Nothing is cleared: the cards and the stamp stay as they were.
            _failure = failure;
            return;
        }
        _failure = null;
        _readAt = _clock.GetUtcNow();
        _work.Show([.. readings!.SelectMany(reading => WaitingItem.Parse(reading.Output))]);
        if (_area == Area.Work && _work.Selected is null)
        {
            if (_left is not { } left || !_work.Focus(left))
                _work.FocusFirstCard();
            _left = null;
        }
        // Said once the cards are laid out again: laying them out moves the selection, which clears what's said.
        if (_added is { } added)
        {
            _added = null;
            _said = added;
            _saidOn = _work.Selected;
        }
        ReadTrend();
    }

    /// <summary>Reads the record after the cards, so it counts the read that has just been taken.</summary>
    private void ReadTrend()
    {
        if (_readTrend is not null && _trending is null)
            _trending = Task.WhenAll(_teamNames.Select(team => _readTrend(team)));
    }

    /// <summary>A trend that couldn't be read leaves the title with what's waiting now.</summary>
    private void SettleTrend()
    {
        if (_trending is not { IsCompleted: true } read)
            return;
        _trending = null;
        var trends = read.Status == TaskStatus.RanToCompletion ? read.Result.Select(WorkTrend.Of).ToList() : null;
        _trends = trends is null || trends.Contains(null) ? [] : [.. trends.OfType<WorkTrend>()];
    }

    private void ShowTitle()
    {
        var title = WorkTrend.Title(_readAt is null ? null : _work.Items.Count, _trends, _clock.GetUtcNow());
        if (_title.Text != title)
            _title.Text = title;
    }

    internal string WorkTitle => _title.Text;

    /// <summary>What went wrong, then what's running, then the region focus is in.</summary>
    private void ShowMessage()
    {
        if (_said is { Length: > 0 } && _work.Selected != _saidOn)
        {
            _said = null;
            _saidOn = null;
        }
        var (text, scheme) =
            _failure is { Length: > 0 } ? (_failure, Schemes.Error)
            : _copied is { } copied ? copied
            : _reading is not null ? ("Reading…", Schemes.Accent)
            : _progress is { Length: > 0 } ? (_progress, Schemes.Accent)
            : _pass.Progress is { } passing ? (passing, Schemes.Accent)
            : _said is { Length: > 0 } ? (_said, Schemes.Accent)
            : _area == Area.Work && _work.Selected is { Reason.Length: > 0 } card ? (card.Line, Schemes.Base)
            : _area == Area.Work && _work.Region is { } region ? (region, Schemes.Base)
            : ("", Schemes.Base);
        var stamp = _area == Area.Work ? Stamped(_readAt, _clock.GetUtcNow()) : "";
        var filter = _area == Area.Work ? _work.OnlyMine ? MyItems : AllItems : "";
        _status.ShowState(stamp, filter);
        if (_status.Message.Says == text)
            return;
        if (text.Length == 0)
            Hush();
        else
            Say(text, scheme);
    }

    private void Say(string message, Schemes scheme)
    {
        _status.Say(message, scheme);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void Hush()
    {
        if (_status.Message.Lines == 0)
            return;
        _status.Message.Clear();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private int Foot() => DispatchLines + 2 + StatusLines;

    private int WorkHeight() => Math.Max(0, Viewport.Height - MenuLines - TitleLines - StatusLines);

    private void Show(Area area)
    {
        if (_area == area)
            return;
        if (_area == Area.Work)
            _left = _work.Place;
        _area = area;
        _settings.WriteArea(area);
        _failure = null;
        if (_dispatcherExpanded)
            SetDispatcherExpanded(false);
        _agents.Visible = _dispatchFrame.Visible = area == Area.Dashboard;
        _work.Visible = _title.Visible = area == Area.Work;
        _menu.Show(area);
        if (area == Area.Work)
        {
            if (!UpToDate())
                ReadWaiting();
            if (_left is not { } left || !_work.Focus(left))
                _work.FocusFirstCard();
        }
        else
            _panes.FirstOrDefault()?.SetFocus();
        ShowMessage();
        ShowLoading();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void Copy(Func<AgentPane, LogCopy> copy)
    {
        if (Selected() is not { } pane)
            return;
        var copied = copy(pane);
        var clipboard = _clipboard ?? App?.Clipboard;
        _copied = clipboard is { IsSupported: true } && clipboard.TrySetClipboardData(copied.Text)
            ? (copied.Said, Schemes.Accent)
            : ("there's no clipboard to copy to", Schemes.Error);
        ShowMessage();
    }

    private void OpenCommands()
    {
        if (App is { } app)
            CommandsDialog.Show(app, _commands);
    }

    private void GoToTeam()
    {
        if (App is { } app && CommandsDialog.Pick(app, "Go to team", _teamNames) is { } team)
            _work.Pick(team);
    }

    private void OpenHelp()
    {
        if (App is { } app)
            HelpDialog.Show(app, _commands);
    }

    private void OpenAbout()
    {
        if (App is { } app)
            AboutDialog.Show(app, _version);
    }

    /// <summary>What's waiting now is Work's count, once it has read one.</summary>
    private void OpenTrends() =>
        _showTrends?.Invoke([.. _teamNames.Select(team => (team, _readAt is null ? (int?)null : _work.Items.Count(item => item.Team == team)))]);

    private void OpenSettings(string? page = null, bool newTeam = false)
    {
        if (App is not { } app)
            return;
        var before = _teams.Names();
        var removed = SettingsDialog.Show(app, _settings, _commands, ShowIcons, _auto, _teams, page, _start, newTeam, _showGuide, _checks);
        if (_teams.Names().Except(before).Any() || RolesChanged())
        {
            _handOver?.Invoke(new TeamsChanged(_area, _area == Area.Work ? _work.Place : _left));
            return;
        }
        Forget(removed);
        SyncQuitKey();
        _menu.Refresh();
    }

    /// <summary>Whether a team on the grid now runs other roles than the panes it has.</summary>
    private bool RolesChanged() =>
        _teamNames.Intersect(_teams.Names()).Any(team =>
            !_teams.Roles(team).SequenceEqual(_panes.Where(pane => pane.Team == team).Select(pane => pane.Role)));

    /// <summary>Takes removed teams off the grid and out of the Work area.</summary>
    internal void Forget(IReadOnlyList<string> teams)
    {
        if (!teams.Any(_teamNames.Contains))
            return;
        if (_expanded is not null)
            SetExpanded(null);
        foreach (var pane in _panes.Where(pane => teams.Contains(pane.Team)).ToList())
        {
            _panes.Remove(pane);
            _agents.Remove(pane);
            pane.Dispose();
            if (_lastSelected == pane)
                _lastSelected = null;
        }
        _teamNames.RemoveAll(teams.Contains);
        _work.Forget(teams);
        _laidOutOver = Size.Empty;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>The vocabulary the panes and the cards draw their icons from, together, with Auto resolved here
    /// so no view has to know what this terminal answered.</summary>
    private void ShowIcons(IconStyle style)
    {
        var drawn = Icons.Resolve(style, _auto);
        _drawn = drawn;
        _work.ShowIcons(drawn);
        foreach (var pane in _panes)
            pane.ShowIcons(drawn);
    }

    private void Expand()
    {
        if (_onDispatcher)
        {
            SetDispatcherExpanded(true);
            return;
        }
        var index = SelectedIndex();
        if (index >= 0 && _expanded is null)
            SetExpanded(index);
    }

    private void Collapse()
    {
        if (_dispatcherExpanded)
            SetDispatcherExpanded(false);
        else
            SetExpanded(null);
    }

    private void SetDispatcherExpanded(bool expanded)
    {
        _dispatcherExpanded = expanded;
        _dispatch.Visible = !expanded;
        _dispatchAll.Visible = expanded;
        _agents.Visible = _area == Area.Dashboard && !expanded;
        if (expanded)
            ShowWholeLog();
        ShowDispatcher(_clock.GetUtcNow());
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Scrolls the selected pane, or the dispatcher's whole log when that is what's expanded.</summary>
    private void Scroll(Action<AgentPane> pane, Action<LogView> log)
    {
        if (_dispatcherExpanded)
            log(_dispatchAll);
        else if (Selected() is { } selected)
            pane(selected);
    }

    /// <summary>Takes the frame from wherever the reader was, expanded in place of an expanded agent.</summary>
    private void SelectDispatcher()
    {
        var expanded = _expanded is not null;
        if (expanded)
            SetExpanded(null);
        _dispatchFrame.SetFocus();
        if (expanded)
            SetDispatcherExpanded(true);
    }

    private void SetExpanded(int? index)
    {
        _expanded = index;
        for (var i = 0; i < _panes.Count; i++)
        {
            _panes[i].Visible = index is null || index == i;
            _panes[i].Selects = index == i;
        }
        var selected = SelectedIndex();
        if (index is not null)
            ScrollTo(0);
        else if (selected >= 0)
            ScrollIntoView(selected);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Through every agent, then the dispatcher frame as the last stop, and round again.</summary>
    private void Step(int step)
    {
        var stops = _panes.Count + 1;
        var current = _onDispatcher ? _panes.Count : SelectedIndex();
        var next = current < 0
            ? step > 0 ? 0 : _panes.Count
            : (current + step + stops) % stops;
        if (next == _panes.Count)
            SelectDispatcher();
        else
            Select(next);
    }

    private void Select(int index)
    {
        if (_dispatcherExpanded)
        {
            SetDispatcherExpanded(false);
            SetExpanded(index);
        }
        else if (_expanded is not null)
            SetExpanded(index);
        else
            ScrollIntoView(index);
        _panes[index].SetFocus();
    }

    private Rectangle Cell(int index) => _expanded is { } only
        ? only == index ? new Rectangle(Point.Empty, _agents.Viewport.Size) : Rectangle.Empty
        : AgentGrid.Cell(Grid, index, GridContent());

    private (string Team, string Role)[] Grid => [.. _panes.Select(pane => (pane.Team, pane.Role))];

    /// <summary>The grid tiles a content area tall enough for every cell's floor; the agent area scrolls over it.</summary>
    private Size GridContent()
    {
        var area = _agents.Viewport.Size;
        if (_expanded is not null)
            return area;
        return area with { Height = Math.Max(area.Height, AgentGrid.Rows(Grid) * MinCellHeight) };
    }

    private void FitGrid()
    {
        // A second pass: the scroll bar takes a column off the area the first one measured.
        for (var pass = 0; pass < 2 && _agents.GetContentSize() != GridContent(); pass++)
            _agents.SetContentSize(GridContent());
        ScrollTo(_agents.Viewport.Y);
        if (_laidOutOver == _agents.Viewport.Size)
            return;
        _laidOutOver = _agents.Viewport.Size;
        var selected = SelectedIndex();
        if (selected >= 0)
            ScrollIntoView(selected);
    }

    private void ScrollIntoView(int index)
    {
        var cell = Cell(index);
        var top = _agents.Viewport.Y;
        var height = _agents.Viewport.Height;
        if (cell.Top < top)
            ScrollTo(cell.Top);
        else if (cell.Bottom > top + height)
            ScrollTo(Math.Min(cell.Top, cell.Bottom - height));
    }

    private void ScrollTo(int top)
    {
        var limit = Math.Max(0, GridContent().Height - _agents.Viewport.Height);
        top = Math.Clamp(top, 0, limit);
        if (_agents.Viewport.Y != top)
            _agents.Viewport = _agents.Viewport with { Y = top };
    }

    private void MoveSelection(int rowStep, int columnStep)
    {
        if (_onDispatcher)
        {
            if (rowStep < 0 && !_dispatcherExpanded)
                Select(_lastSelected is { } last && _panes.IndexOf(last) is >= 0 and var index ? index : _panes.Count - 1);
            return;
        }
        if (rowStep != 0 && Selected() is { } pane && pane.MoveRun(rowStep))
            return;
        if (_expanded is not null)
            return;
        var current = SelectedIndex();
        if (current < 0)
        {
            _panes[0].SetFocus();
            return;
        }
        var grid = Grid;
        var rows = AgentGrid.Rows(grid);
        var slot = AgentGrid.Slot(grid, current);
        if (rowStep > 0 && slot.Y == rows - 1)
        {
            SelectDispatcher();
            return;
        }
        Select(AgentGrid.At(grid, Math.Clamp(slot.Y + rowStep, 0, rows - 1), slot.X + columnStep));
    }

    private int SelectedIndex() => Selected() is { } pane ? _panes.IndexOf(pane) : -1;

    /// <summary>The focused pane, or the last one focused while something else, like an open menu, has focus.</summary>
    private AgentPane? Selected() => _onDispatcher
        ? null
        : _panes.FirstOrDefault(pane => pane.HasFocus) ?? (_area == Area.Dashboard ? _lastSelected : null);

    private static string Version() =>
        typeof(DashboardWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "";

    private static string? ReadText(string path)
    {
        try { return File.ReadAllText(path).Trim(); }
        catch (IOException) { return null; }
    }

    private static IReadOnlyList<LogLine> ReadTail(string path, int count)
    {
        try
        {
            return [.. File.ReadLines(path).TakeLast(count).Select(DispatchLine.Read)];
        }
        catch (IOException) { return [new LogLine("(the dispatcher hasn't run yet)", LogLineKind.Prose)]; }
    }
}
