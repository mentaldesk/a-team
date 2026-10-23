using System.Drawing;
using System.Reflection;
using AssistantPane = MentalDesk.Tui.Assistant.AssistantPane;
using MentalDesk.Tui.Commands;
using MentalDesk.Tui.Keys;
using MentalDesk.Tui.Links;
using Terminal.Gui;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;

namespace ATeam.Dashboard;

/// <summary>The areas the app opens in: the agents, everything waiting on the reviewer, or every team's board.</summary>
public enum Area
{
    Dashboard,
    Work,
    Overseer,
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
    private const string CopyLines = "log.copyLines";
    private const string LoadingText = "Loading…";
    private const int LoadingPadding = 8;
    private const int LoadingHeight = 5;
    private const int AlertGap = 3;
    private const int AssistantColumns = 48;

    // Cmd+C, once the Mac keys are installed.
    private static readonly Key MacCopy = Key.C.WithCtrl;
    private readonly List<AgentPane> _panes = [];
    private readonly CommandRegistry _commands = new();
    private readonly string _version = Version();
    private readonly AppMenu _menu;
    private readonly StatusBar _status = new();
    private readonly MoveAlert _alert = new();
    private readonly View _agents;
    private readonly FrameView _dispatchFrame;
    private readonly LogView _dispatch;
    private readonly LogView _dispatchAll;
    private readonly WorkView _work;
    private readonly OverseerView _overseer;
    private readonly Func<string, Task<Reading>>? _readBoard;
    private Task<Reading[]>? _boarding;
    private DateTimeOffset? _boardReadAt;
    private IReadOnlyDictionary<string, string> _limits;
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
    private readonly Func<WaitingItem, Task<Reading>>? _readDocument;
    private readonly Func<WaitingItem, Task<Reading>>? _readHistory;
    private readonly Func<string, Task<Reading>>? _readTrend;
    private readonly Action<IReadOnlyList<(string Team, int? Waiting)>>? _showTrends;
    private readonly Action<IReadOnlyList<string>, Action<string, SpendingItem>>? _showSpending;
    private readonly Action<string>? _showDecisions;
    private readonly Func<IReadOnlyList<string>, string, string, Attaching?, (string Team, int Number)?>? _newIdea;
    private readonly Func<Key, Attaching>? _attaching;
    private readonly Func<string, LinkOutcome> _openUrl;
    private readonly Func<WaitingItem, IssueBody, Func<LinkOutcome?>, Func<bool>?, ReaderCommand?, ReaderComment?, ReaderTry?, ReaderEdit?, Key, ReaderIdea?, Func<Key, bool>, Rank?, Rank?> _showBody;
    private readonly Func<WaitingItem, bool> _confirmAccept;
    private readonly Func<WaitingItem, bool> _confirmApprove;
    private readonly Func<WaitingItem, string?> _askHold;
    private readonly Func<RunTask, bool> _confirmStop;
    private readonly Action<string> _showGuide;
    private readonly Action<Handover>? _handOver;
    private readonly IconStyle _auto;
    private readonly FrameView _loading;
    private readonly Label _title;
    private readonly TimeProvider _clock;
    private readonly Attention _attention;
    private readonly WhatsNew _news;
    private readonly Opened _opened;
    private readonly Func<string, DateTimeOffset, Task<Reading>>? _readMoves;
    private (IReadOnlyList<string> Teams, Task<Reading[]> Read)? _moving;
    private (DateTime, DateTime)? _recordStamp;
    private readonly Dictionary<string, (Task<Reading> Read, DateTimeOffset Asked)> _teamReads = [];
    private DateTimeOffset _readingAsked;
    private NewItem? _goingTo;
    private Area _area;
    private readonly WriteQueue _writes;
    private readonly Func<Write?, IReadOnlyList<Write>, bool> _confirmQuit;
    private WaitingItem? _approvable;
    private WaitingItem? _shown;
    private WaitingItem? _leaving;
    private string? _said;
    private (string Text, WaitingItem? On)? _already;
    private string? _added;
    private ReaderIdea? _idea;
    private ReaderImage? _image;
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
    private (WaitingItem Item, ReaderPlace Place, string? Failure, string? Said, bool Reread)? _reopen;
    private (IssueBody Body, int Top)? _editFrom;
    private (string Text, Schemes Scheme)? _reported;
    private readonly IClipboard? _clipboard;
    private readonly MacKeys? _macKeys;
    private readonly AssistantPane? _assistant;

    public DashboardWindow(
        IReadOnlyList<(string Team, string Role)> agents,
        string stateRoot,
        DashboardSettings settings,
        TeamConfigs teams,
        Func<string[], Task<string?>> run,
        Func<string, Task<Reading>> readWaiting,
        Func<WaitingItem, Task<Reading>> readBody,
        Func<string, LinkOutcome> openUrl,
        Func<WaitingItem, IssueBody, Func<LinkOutcome?>, Func<bool>?, ReaderCommand?, ReaderComment?, ReaderTry?, ReaderEdit?, Key, ReaderIdea?, Func<Key, bool>, Rank?, Rank?> showBody,
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
        Func<IReadOnlyList<string>, string, string, Attaching?, (string Team, int Number)?>? newIdea = null,
        Func<string, Task<Reading>>? readBoard = null,
        Attention? attention = null,
        Func<WaitingItem, Task<Reading>>? readDocument = null,
        Func<string, DateTimeOffset, Task<Reading>>? readMoves = null,
        WhatsNew? whatsNew = null,
        WriteQueue? writes = null,
        Func<Write?, IReadOnlyList<Write>, bool>? confirmQuit = null,
        Func<Key, Attaching>? attaching = null,
        Action<IReadOnlyList<string>, Action<string, SpendingItem>>? showSpending = null,
        Action<string>? showDecisions = null,
        MacKeys? macKeys = null,
        Func<WaitingItem, bool>? confirmApprove = null,
        Func<WaitingItem, string?>? askHold = null,
        AssistantSetup? assistant = null)
    {
        _macKeys = macKeys;
        _attaching = attaching;
        _showSpending = showSpending;
        _showDecisions = showDecisions;
        _writes = writes ?? new WriteQueue(run);
        _confirmQuit = confirmQuit ?? ((_, _) => true);
        _readMoves = readMoves;
        _readBoard = readBoard;
        _readDocument = readDocument;
        _limits = settings.ReadLimits();
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
        _attention = attention ?? new Attention(_clock, _ => { });
        _news = whatsNew ?? new WhatsNew(_clock.GetUtcNow());
        _opened = new Opened(stateRoot);
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
        _confirmApprove = confirmApprove ?? (_ => false);
        _askHold = askHold ?? (_ => null);
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
            Width = Dim.Func(_ => BesideAssistant(), this),
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
            Width = Dim.Func(_ => BesideAssistant(), this),
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

        _title = new Label { X = 1, Y = MenuLines, Width = Dim.Func(_ => Math.Max(0, BesideAssistant() - 1), this), Text = "Work", Visible = area == Area.Work };
        Add(_title);
        _work = new WorkView(_teamNames)
        {
            X = 0,
            Y = MenuLines + TitleLines,
            Width = Dim.Func(_ => BesideAssistant(), this),
            Height = Dim.Func(_ => WorkHeight(), this),
            Visible = area == Area.Work,
        };
        _work.FocusChanged += ShowMessage;
        _work.ShowOnlyMine(settings.ReadOnlyMine());
        Add(_work);
        _overseer = new OverseerView(_teamNames)
        {
            X = 0,
            Y = MenuLines,
            Width = Dim.Func(_ => BesideAssistant(), this),
            Height = Dim.Func(_ => Math.Max(0, Viewport.Height - MenuLines - StatusLines), this),
            Visible = area == Area.Overseer,
        };
        Add(_overseer);
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
        if (assistant is not null)
            Add(_assistant = NewAssistant(assistant));

        _status.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - StatusLines), this);
        Add(_status);

        RegisterCommands();
        _commands.Apply(settings.ReadKeys());
        SyncQuitKey();
        ShowKeysHint();
        _menu = new AppMenu(_commands, _area);
        _menu.Bar.X = 0;
        _menu.Bar.Y = 0;
        _menu.Bar.SubViewsLaidOut += (_, _) => _alert.SetNeedsLayout();
        Add(_menu.Bar);
        _alert.X = Pos.Func(_ => Math.Max(MenuTitlesEnd() + AlertGap, Viewport.Width - _alert.Shown.GetColumns()), this);
        _alert.Width = Dim.Fill();
        _alert.Chosen += () => _commands.Execute("work.whatsNew");
        Add(_alert);

        if (resume is not null)
            Resume(resume);
        if (resume is null or TeamsChanged or VisionHandover && _area == Area.Work)
            ReadWaiting();
        if (_area == Area.Overseer)
            ReadBoards();
    }

    internal IReadOnlyList<AgentPane> Panes => _panes;

    internal View Dispatcher => _dispatchFrame;

    internal LogView DispatchLog => _dispatch;

    internal LogView WholeDispatchLog => _dispatchAll;

    internal bool DispatcherSelected => _onDispatcher;

    internal bool DispatcherExpanded => _dispatcherExpanded;

    internal View Agents => _agents;

    internal WorkView Work => _work;

    internal OverseerView Overseer => _overseer;

    internal MessageBar Message => _status.Message;

    internal StatusBar Status => _status;

    internal MoveAlert Alert => _alert;

    internal MenuBar Menu => _menu.Bar;

    internal IReadOnlyList<(string Id, DimmedMenuItem Item)> MenuItems => _menu.Items;

    internal IReadOnlyList<MenuBarItem> Menus => _menu.Menus;

    internal Area CurrentArea => _area;

    internal int? ExpandedAgent => _expanded;

    internal CommandRegistry Commands => _commands;

    internal AssistantPane? Assistant => _assistant;

    internal FrameView Loading => _loading;

    /// <summary>How long Work, in front with nothing over it, goes before it reads again by itself.</summary>
    internal static readonly TimeSpan ReadEvery = TimeSpan.FromMinutes(5);

    internal Attention Attention => _attention;

    internal WhatsNew News => _news;

    public void Refresh()
    {
        _attention.Browsing(_area == Area.Work ? _work.Current?.Team : null);
        Settle();
        SettleWrites();
        SettleTeamReads();
        SettleMoves();
        ReadMoves();
        SettleBoards();
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

        ShowOverseer(now);
        ShowNews();
        ShowAsking();
        _menu.Refresh();
        ShowMessage();
        ShowLoading();
    }

    /// <summary>Reads every team's moves since start whenever the record has been written to.</summary>
    private void ReadMoves()
    {
        if (_readMoves is null || _moving is not null)
            return;
        var stamp = (Written(Path.Combine(_stateRoot, "history.db")), Written(Path.Combine(_stateRoot, "history.db-wal")));
        if (stamp == _recordStamp)
            return;
        _recordStamp = stamp;
        IReadOnlyList<string> teams = [.. _teamNames];
        _moving = (teams, Task.WhenAll(teams.Select(team => _readMoves(team, _news.Since))));
    }

    private static DateTime Written(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;

    /// <summary>A team with something new is read again, so its card is there when you get to it.</summary>
    private void SettleMoves()
    {
        if (_moving is not { Read.IsCompleted: true } moving)
            return;
        _moving = null;
        if (moving.Read.Status != TaskStatus.RanToCompletion)
            return;
        for (var i = 0; i < moving.Teams.Count; i++)
            if (moving.Read.Result[i].Failure is null or { Length: 0 }
                && _news.Read(moving.Teams[i], Dashboard.Move.Parse(moving.Read.Result[i].Output)))
                ReadTeam(moving.Teams[i]);
    }

    private void ReadTeam(string team)
    {
        if (!_teamReads.ContainsKey(team))
            _teamReads[team] = (_readWaiting(team), _clock.GetUtcNow());
    }

    /// <summary>A team's own read replaces only its cards. One that fails leaves them as they were.</summary>
    private void SettleTeamReads()
    {
        foreach (var (team, (read, asked)) in _teamReads.Where(entry => entry.Value.Read.IsCompleted).ToList())
        {
            _teamReads.Remove(team);
            if (read.Status != TaskStatus.RanToCompletion || read.Result.Failure is { Length: > 0 })
                continue;
            var items = WaitingItem.Parse(read.Result.Output);
            _news.Read(team, items, asked);
            _work.Show([.. _work.Items.Where(item => item.Team != team), .. items]);
            GoToWaiting();
        }
    }

    private void ShowNews()
    {
        _alert.Show(_news.Alert(_drawn));
        _work.ShowNew(_news.Items);
    }

    /// <summary>Work, on the newest new card's tab with it selected; where Work hasn't read it yet, once it has.</summary>
    private void GoToNew()
    {
        if (_news.Newest is not { } newest)
            return;
        Show(Area.Work);
        _work.Pick(newest.Team);
        _goingTo = newest;
        GoToWaiting();
    }

    private void GoToWaiting()
    {
        if (_goingTo is not { } going || _area != Area.Work)
            return;
        if (_work.Items.FirstOrDefault(item => item.Team == going.Team && item.Number == going.Number) is not { } item)
            return;
        _goingTo = null;
        _work.Pick(going.Team);
        _work.Focus(item, false);
        ShowMessage();
    }

    /// <summary>Opening a card is seeing it.</summary>
    private void Seen(WaitingItem item)
    {
        _news.Seen(item.Team, item.Number, _clock.GetUtcNow());
        ShowNews();
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
        if (_reported is not null)
        {
            _reported = null;
            ShowMessage();
        }
        if (key == MacCopy && !_menu.Bar.IsOpen() && _commands.IsEnabled(CopyLines))
            return _commands.Execute(CopyLines);
        if (InAssistant && IsTyping(key))
            return base.OnKeyDown(key);
        if (InAssistant && key == Key.Esc)
        {
            _assistant!.Stop();
            return true;
        }
        return (!_menu.Bar.IsOpen() && _commands.Press(key)) || base.OnKeyDown(key);
    }

    private void RegisterCommands()
    {
        bool OnDashboard() => _area == Area.Dashboard;
        bool OnWork() => _area == Area.Work;
        bool OnOverseer() => _area == Area.Overseer;
        bool AnyAgents() => OnDashboard() && _panes.Count > 0;
        bool Selection() => OnDashboard() && Selected() is not null;
        bool Scrollable() => Selection() || (OnDashboard() && _dispatcherExpanded);
        bool OnGrid() => AnyAgents() && _expanded is null;
        bool Reading() => Selection() && _expanded is not null;
        string[] details = ["Show details", "Show the whole lane"];
        string[] hold = ["Pause this role", "Let this role start again"];
        string[] interrupt = ["Interrupt", "Let it start again"];
        string[] mine = ["Show only mine", "Show all"];
        string[] holdTask = ["Hold", "Release hold"];
        _commands
            .Register("assistant.send", "Send question", () => _assistant?.Send(), Key.Enter, isEnabled: () => InAssistant, scopes: [Regions.Assistant])
            .Register("assistant.stop", "Stop answering", () => _assistant?.Stop(), Key.Esc,
                isEnabled: () => InAssistant && _assistant!.Controller.IsAnswering, scopes: [Regions.Assistant])
            .Register("assistant.clear", "Start a new conversation", () => _assistant?.Clear(), Key.L.WithCtrl, isEnabled: () => InAssistant,
                scopes: [Regions.Assistant])
            .Register("pane.next", "Next pane", SwitchPane, Key.F6, isEnabled: () => _assistant is { Visible: true })
            .Register("pane.previous", "Previous pane", SwitchPane, Key.F6.WithShift, isEnabled: () => _assistant is { Visible: true })
            .Register("agent.next", "Select the next agent", () => Step(+1), Key.Tab, isEnabled: AnyAgents, scopes: [Regions.Agents, Regions.Log])
            .Register("agent.previous", "Select the previous agent", () => Step(-1), Key.Tab.WithShift, isEnabled: AnyAgents, scopes: [Regions.Agents, Regions.Log])
            .Register("agent.right", "Select the agent to the right", () => MoveSelection(0, +1), Key.CursorRight, isEnabled: AnyAgents, scopes: [Regions.Agents, Regions.Log])
            .Register("agent.left", "Select the agent to the left", () => MoveSelection(0, -1), Key.CursorLeft, isEnabled: AnyAgents, scopes: [Regions.Agents, Regions.Log])
            .Register("agent.down", "Select the agent below", () => MoveSelection(+1, 0), Key.CursorDown, isEnabled: OnGrid, scopes: [Regions.Agents])
            .Register("agent.up", "Select the agent above", () => MoveSelection(-1, 0), Key.CursorUp, isEnabled: OnGrid, scopes: [Regions.Agents])
            .Register("log.lineDown", "Select the next line of the log", () => Selected()?.MoveLine(+1, extend: false), Key.CursorDown, isEnabled: Reading, scopes: [Regions.Log])
            .Register("log.lineUp", "Select the line above in the log", () => Selected()?.MoveLine(-1, extend: false), Key.CursorUp, isEnabled: Reading, scopes: [Regions.Log])
            .Register("log.extendDown", "Extend the selection down", () => Selected()?.MoveLine(+1, extend: true), Key.CursorDown.WithShift, isEnabled: Reading, scopes: [Regions.Log])
            .Register("log.extendUp", "Extend the selection up", () => Selected()?.MoveLine(-1, extend: true), Key.CursorUp.WithShift, isEnabled: Reading, scopes: [Regions.Log])
            .Register("agent.expand", () => "Expand the selected agent", () => Expand(), Key.Enter,
                isEnabled: () => Selection() || (OnDashboard() && _onDispatcher && !_dispatcherExpanded),
                menuLabel: () => "Expand", inMenu: () => _expanded is null && !_dispatcherExpanded, scopes: [Regions.Agents])
            .Register("log.pageUp", "Scroll the log up", () => Scroll(pane => pane.Page(-1), log => log.Page(-1)), Key.PageUp, isEnabled: Scrollable, scopes: [Regions.Agents, Regions.Log])
            .Register("log.pageDown", "Scroll the log down", () => Scroll(pane => pane.Page(+1), log => log.Page(+1)), Key.PageDown, isEnabled: Scrollable, scopes: [Regions.Agents, Regions.Log])
            .Register("log.top", "Jump to the top of the log", () => Scroll(pane => pane.Home(), log => log.Home()), Key.Home, isEnabled: Scrollable, scopes: [Regions.Agents, Regions.Log])
            .Register("log.bottom", "Jump to the bottom of the log", () => Scroll(pane => pane.End(), log => log.End()), Key.End, isEnabled: Scrollable, scopes: [Regions.Agents, Regions.Log])
            .Register("log.toolCalls", "Show tool calls in full", () => Selected()?.ToggleToolCalls(), new Key('t'), isEnabled: Selection, scopes: [Regions.Agents, Regions.Log])
            .Register(CopyLines, () => "Copy the selected lines", () => Copy(pane => pane.CopySelection()), new Key('l'), isEnabled: Reading,
                menuLabel: () => "Copy selected lines", scopes: [Regions.Log])
            .Register("log.copyAll", () => "Copy the whole log", () => Copy(pane => pane.CopyAll()), new Key('L'), isEnabled: Reading,
                menuLabel: () => "Copy whole log", scopes: [Regions.Log])
            .Register("log.editor", () => "Open the whole log in your editor", OpenInEditor, new Key('e'), isEnabled: Reading,
                menuLabel: () => "Open whole log in editor", scopes: [Regions.Log])
            .Register("work.right", "Select the column to the right", () => _work.MoveColumn(+1), Key.CursorRight, isEnabled: OnWork, scopes: [Regions.Work])
            .Register("work.left", "Select the column to the left", () => _work.MoveColumn(-1), Key.CursorLeft, isEnabled: OnWork, scopes: [Regions.Work])
            .Register("work.down", "Select the card below", () => _work.MoveCard(+1), Key.CursorDown, isEnabled: OnWork, scopes: [Regions.Work])
            .Register("work.up", "Select the card above", () => _work.MoveCard(-1), Key.CursorUp, isEnabled: OnWork, scopes: [Regions.Work])
            .Register("work.read", "Open", ReadSelected, Key.Enter, isEnabled: () => OnWork() && _work.Selected is not null, onCard: true, scopes: [Regions.Work])
            .Register("work.priority", "Set priority", SetPriority, new Key('p'), isEnabled: () => OnWork() && _work.SelectedCard is not null, onCard: true, scopes: [Regions.Work])
            .Register("work.try", "Try", Try, new Key('t'), isEnabled: () => OnWork() && _work.Selected is { Triable: true }, onCard: true, scopes: [Regions.Work])
            .Register("work.copyBranch", "Copy branch name", CopyBranch, new Key('y'),
                isEnabled: () => OnWork() && _work.Selected is { Branch.Length: > 0 }, onCard: true, scopes: [Regions.Work])
            .Register("work.github", "Open on GitHub", OpenSelected, new Key('g'), isEnabled: () => OnWork() && _work.SelectedUrl is { Length: > 0 }, onCard: true, scopes: [Regions.Work])
            .Register("team.board", "Open team's board on GitHub", OpenBoard, new Key('b'), isEnabled: () => SelectedTeam() is not null)
            .Register("team.vision", () => SelectedTeam() is { } team ? $"Write the vision with me · {team}" : "Write the vision with me",
                () => WriteVision(SelectedTeam()), isEnabled: () => _handOver is not null && SelectedTeam() is not null)
            .Register("team.visionReview", () => SelectedTeam() is { } team ? $"Review the vision · {team}" : "Review the vision",
                () => ReviewVision(SelectedTeam()), isEnabled: () => SelectedTeam() is not null)
            .Register("work.approve", "Approve the pitch you're reading", () => Approve(), new Key('a'), isEnabled: () => _approvable is not null)
            .Register("work.accept", "Accept", () => Accept(), new Key('a'), isEnabled: () => Acceptable() is not null, onCard: true, scopes: [Regions.Work])
            .Register("work.comment", "Comment on the item you're reading", () => { }, new Key('c'), isEnabled: () => _shown is not null)
            .Register("work.edit", "Edit the document pitch you're reading", () => { }, new Key('e'), isEnabled: () => _shown is { Document: true })
            .Register("work.attach", "Attach image file to what you're writing", () => { }, Key.U.WithCtrl,
                isEnabled: () => _shown is not null && _attaching is not null)
            .Register("work.viewImage", () => "View image", () => _image?.View(), isEnabled: () => _image?.Viewable() == true)
            .Register("work.decline", "Decline", () => ReadSelected(declining: true), new Key('x'),
                isEnabled: () => OnWork() && _work.SelectedCard is { Declinable: true }, onCard: true, scopes: [Regions.Work])
            .Register("work.notNow", "Not now", () => ReadSelected(declining: false, shelving: true), new Key('l'),
                isEnabled: () => OnWork() && _work.SelectedCard is { Shelvable: true }, onCard: true, scopes: [Regions.Work])
            .Register("work.hold", () => holdTask[_work.SelectedCard is { Held: true } ? 1 : 0], HoldOrRelease, new Key('h'),
                isEnabled: () => OnWork() && _work.SelectedCard is { Holdable: true }, onCard: true, scopes: [Regions.Work], menuLabels: holdTask)
            .Register("work.nextTeam", () => "Next team", () => _work.MoveTeam(+1), Key.PageDown.WithCtrl, isEnabled: OnWork, scopes: [Regions.Work])
            .Register("work.previousTeam", () => "Previous team", () => _work.MoveTeam(-1), Key.PageUp.WithCtrl, isEnabled: OnWork, scopes: [Regions.Work])
            .Register("work.team", () => "Go to team…", GoToTeam, isEnabled: OnWork, scopes: [Regions.Work])
            .Register("work.new", "New idea", NewIdea, new Key('n'), isEnabled: NewIdeaEnabled)
            .Register("work.mine", () => "Show only what's your move", ToggleOnlyMine, new Key('m'), isEnabled: OnWork,
                menuLabel: () => mine[_work.OnlyMine ? 1 : 0], scopes: [Regions.Work], menuLabels: mine)
            .Register("work.refresh", () => "Read what's waiting again", ReadWaiting, Key.F5, isEnabled: OnWork,
                menuLabel: () => "Refresh", scopes: [Regions.Work])
            .Register("overseer.right", "Select the chip to the right", () => _overseer.MoveColumn(+1), Key.CursorRight, isEnabled: OnOverseer, scopes: [Regions.Overseer])
            .Register("overseer.left", "Select the chip to the left", () => _overseer.MoveColumn(-1), Key.CursorLeft, isEnabled: OnOverseer, scopes: [Regions.Overseer])
            .Register("overseer.down", "Select the chip below", () => _overseer.MoveRow(+1), Key.CursorDown, isEnabled: OnOverseer, scopes: [Regions.Overseer])
            .Register("overseer.up", "Select the chip above", () => _overseer.MoveRow(-1), Key.CursorUp, isEnabled: OnOverseer, scopes: [Regions.Overseer])
            .Register("overseer.nextLane", "Select the next team's lane", () => _overseer.MoveLane(+1), Key.Tab, isEnabled: OnOverseer, scopes: [Regions.Overseer])
            .Register("overseer.previousLane", "Select the previous team's lane", () => _overseer.MoveLane(-1), Key.Tab.WithShift, isEnabled: OnOverseer, scopes: [Regions.Overseer])
            .Register("overseer.pageDown", "Scroll the lanes down", () => _overseer.Page(+1), Key.PageDown, isEnabled: OnOverseer, scopes: [Regions.Overseer])
            .Register("overseer.pageUp", "Scroll the lanes up", () => _overseer.Page(-1), Key.PageUp, isEnabled: OnOverseer, scopes: [Regions.Overseer])
            .Register("overseer.details", () => details[_overseer.Board.OnMore ? 1 : 0], OverseerEnter, Key.Enter,
                isEnabled: () => OnOverseer() && _overseer.Board.Selected is not null, scopes: [Regions.Overseer], menuLabels: details)
            .Register("overseer.fold", () => "Fold the lane", () => _overseer.Fold(), Key.Esc,
                isEnabled: () => OnOverseer() && _overseer.CanFold, scopes: [Regions.Overseer])
            .Register("overseer.github", () => "Open on GitHub", OpenChip, new Key('g'),
                isEnabled: () => OnOverseer() && _overseer.Board.SelectedCard is { Url.Length: > 0 }, scopes: [Regions.Overseer])
            .Register("overseer.session", () => "Go to the agent's session", GoToSession, new Key('r'),
                isEnabled: () => OnOverseer() && SessionFor(_overseer.Board.SelectedCard) is not null, scopes: [Regions.Overseer])
            .Register("overseer.refresh", () => "Read every board again", ReadBoards, Key.F5, isEnabled: OnOverseer,
                menuLabel: () => "Refresh", scopes: [Regions.Overseer])
            .Register("view.dashboard", "Dashboard", () => Show(Area.Dashboard), new Key('d'))
            .Register("view.work", "Work", () => Show(Area.Work), new Key('w'))
            .Register("view.overseer", "Overseer", () => Show(Area.Overseer), new Key('o'))
            .Register("assistant", () => "Assistant", ToggleAssistant, isEnabled: () => _assistant is not null, menuLabel: () => "Ass_istant")
            .Register("work.whatsNew", "Go to what's new", GoToNew, new Key('u'), isEnabled: () => _news.Newest is not null)
            .Register("agent.hold", () => UnlessHeld("Pause selected agent's role", "Let selected agent's role start again"), ToggleHold, new Key('h'),
                isEnabled: () => OnDashboard() && Selected() is not null,
                menuLabel: () => UnlessHeld(hold[0], hold[1]), scopes: [Regions.Agents, Regions.Log], menuLabels: hold)
            .Register("agent.interrupt", () => UnlessStopped("Interrupt selected agent", "Let selected agent start again"), ToggleInterrupt, new Key('i'),
                isEnabled: () => OnDashboard() && Selected() is { Running: true } or { Held: true } or { RunHeld: true },
                menuLabel: () => UnlessStopped(interrupt[0], interrupt[1]), scopes: [Regions.Agents, Regions.Log], menuLabels: interrupt)
            .Register("dispatch.pass", "Run a dispatch pass now", PassNow)
            .Register("commands", "Commands", OpenCommands, Key.E.WithCtrl, isEnabled: HasApp)
            .Register("settings", "Settings", () => OpenSettings(), new Key('s'), isEnabled: HasApp)
            .Register("teams", "Teams", () => OpenSettings(SettingsDialog.TeamsPage), isEnabled: HasApp)
            .Register("teams.new", "New team", () => OpenSettings(SettingsDialog.TeamsPage, newTeam: true), isEnabled: HasApp)
            .Register("help", () => "Show keys", OpenKeys, Key.F1, isEnabled: HasApp, menuLabel: () => "Show _keys")
            .Register("guide", "Guide", () => _showGuide(_area switch
            {
                Area.Work => GuideDialog.Work,
                Area.Overseer => GuideDialog.Overseer,
                _ => GuideDialog.Dashboard,
            }))
            .Register("about", "About", OpenAbout, isEnabled: HasApp)
            .Register("trends", "Trends", OpenTrends, isEnabled: () => _showTrends is not null)
            .Register("spending", "Spending", OpenSpending, isEnabled: () => _showSpending is not null)
            .Register("decisions", () => SelectedTeam() is { } team ? $"Decisions… · {team}" : "Decisions…",
                () => _showDecisions?.Invoke(SelectedTeam()!), isEnabled: () => _showDecisions is not null && SelectedTeam() is not null,
                menuLabel: () => "Decisions…")
            .Register("agent.collapse", () => "Back to the agent grid", Collapse, Key.Esc,
                isEnabled: () => OnDashboard() && (_expanded is not null || _dispatcherExpanded), menuLabel: () => "Back to all agents",
                inMenu: () => _expanded is not null || _dispatcherExpanded, scopes: [Regions.Log])
            .Register("quit", "Quit", Quit, new Key('q'));
        for (var tab = 0; tab < 9; tab++)
        {
            var index = tab;
            _commands.Register($"work.team{index + 1}",
                () => index < _work.Lanes.Count ? $"Go to {_work.Lanes[index].Team}" : $"Go to team {index + 1}",
                () => _work.PickTab(index), new Key((char)('1' + index)), isEnabled: () => OnWork() && index < _work.Lanes.Count,
                scopes: [Regions.Work]);
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

    /// <summary>Writes that haven't started are your decisions, so quitting asks before it drops them.</summary>
    private void Quit()
    {
        if (_writes.Queued.Count > 0 && !_confirmQuit(_writes.Running, _writes.Queued))
            return;
        _writes.Drop();
        App?.RequestStop();
    }

    /// <summary>Says what's already under way on <paramref name="key"/>, if anything is.</summary>
    private bool Already(string key)
    {
        if (_writes.Holding(key) is not { } already)
            return false;
        _failure = null;
        _already = (already.Already, _work.Selected);
        ShowMessage();
        return true;
    }

    /// <summary>Queues <paramref name="write"/> behind any already under way, so nothing waits on it.</summary>
    private void Queue(Write write)
    {
        _failure = null;
        _already = null;
        _writes.Add(write);
        _work.ShowWriting(Writing());
        ShowMessage();
    }

    private IEnumerable<WaitingItem> Writing() =>
        [.. _writes.Running is { Item: { } running } ? [running] : Array.Empty<WaitingItem>(),
            .. _writes.Queued.Select(write => write.Item).OfType<WaitingItem>()];

    /// <summary>The status bar's one hint, as TuiCode words it: every other key is in the sheet it opens.</summary>
    private void ShowKeysHint()
    {
        var key = _commands.KeyFor("help");
        _status.Help(key == Key.Empty ? "" : $"Press {KeyNames.Short(key)} for help", () => _commands.Execute("help"));
    }

    private void ToggleHold()
    {
        if (Selected() is not { } pane || Already(RoleKey(pane)))
            return;
        Queue(new Write(WriteKind.Hold, RoleKey(pane), [pane.Held ? "resume" : "pause", pane.Team, pane.Role],
            pane.Held ? "Letting it start again…" : "Pausing this role…",
            $"{pane.Team}'s {pane.Role} is already being {(pane.Held ? "let start again" : "paused")}",
            $"{(pane.Held ? "letting" : "pausing")} {pane.Team}'s {pane.Role}{(pane.Held ? " start again" : "")}"));
    }

    private void PassNow()
    {
        _failure = null;
        _said = null;
        _pass.Start();
        ShowMessage();
    }

    private static string RoleKey(AgentPane pane) => $"{pane.Team}/{pane.Role}";

    private string UnlessHeld(string label, string resume) => Selected() is { Held: true } ? resume : label;

    private string UnlessStopped(string label, string resume) => Selected() is { Held: true } or { RunHeld: true } ? resume : label;

    /// <summary>With several Dev runs, acts on the shown run's task alone; otherwise on the role, as with one run.</summary>
    private void ToggleInterrupt()
    {
        if (Selected() is not { } pane || !(pane.Running || pane.Held || pane.RunHeld) || Already(RoleKey(pane)))
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
        string[] target = task is null ? [pane.Team, pane.Role] : [pane.Team, pane.Role, task.Number.ToString()];
        Queue(new Write(WriteKind.Interrupt, RoleKey(pane), [resume ? "resume" : "stop", .. target],
            resume ? "Letting it start again…" : "Interrupting…",
            $"{pane.Team}'s {pane.Role} is already being {(resume ? "let start again" : "interrupted")}",
            $"{(resume ? "letting" : "interrupting")} {pane.Team}'s {pane.Role}{(resume ? " start again" : "")}"));
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
            _readingAsked = _askedAt.Value;
            _activityRead = Activity();
            _reading = Task.WhenAll(_teamNames.Select(team => _readWaiting(team)));
        }
        ShowMessage();
        ShowLoading();
    }

    private string[] Activity() => [.. _panes.Select(pane => pane.Activity)];

    /// <summary>Reads every team's whole board at once, and what's waiting for Work's reasons with it. A second go
    /// while one is running is refused, not queued.</summary>
    private void ReadBoards()
    {
        if (_readBoard is null)
            return;
        _boarding ??= Task.WhenAll(_teamNames.Select(team => _readBoard(team)));
        if (_reading is null)
            ReadWaiting();
        ShowMessage();
        ShowLoading();
    }

    /// <summary>A read that failed leaves the chips and the stamp as they were.</summary>
    private void SettleBoards()
    {
        if (_boarding is not { IsCompleted: true } read)
            return;
        _boarding = null;
        var readings = read.Status == TaskStatus.RanToCompletion ? read.Result : null;
        var failure = readings is null
            ? read.Exception?.GetBaseException().Message ?? "the read didn't finish"
            : readings.Select(reading => reading.Failure).FirstOrDefault(line => line is { Length: > 0 });
        if (failure is { Length: > 0 })
        {
            _failure = failure;
            return;
        }
        _boardReadAt = _clock.GetUtcNow();
        _overseer.Show([.. readings!.SelectMany(reading => BoardCard.Parse(reading.Output))]);
    }

    /// <summary>What Overseer draws besides the cards: the limits, and which cards and roles the agents are on.</summary>
    private void ShowOverseer(DateTimeOffset now)
    {
        if (_area != Area.Overseer)
            return;
        var running = _panes.Where(pane => pane.Running).ToList();
        _overseer.Show(new OverseerState(
            now,
            _limits,
            running.SelectMany(pane => pane.Working.Select(number => (Card: (pane.Team, number), pane.Role)))
                .DistinctBy(worked => worked.Card).ToDictionary(worked => worked.Card, worked => worked.Role),
            running.GroupBy(pane => pane.Team).ToDictionary(team => team.Key, team => (IReadOnlyList<string>)[.. team.Select(pane => pane.Role)]),
            (team, number) => _work.Items.FirstOrDefault(item => item.Team == team && item.Number == number))
        {
            Holding = Holds.Holders(_work.Items),
        });
    }

    private void OverseerEnter()
    {
        _overseer.Enter();
        ShowMessage();
    }

    private void OpenChip()
    {
        if (_overseer.Board.SelectedCard is { Url.Length: > 0 } card)
            Open(card.Url);
    }

    private void Open(string url)
    {
        _reported = Copying.Said(_openUrl(url));
        ShowMessage();
    }

    /// <summary>The pane of the agent working on <paramref name="card"/>, if one is.</summary>
    private AgentPane? SessionFor(BoardCard? card) =>
        card is null ? null : _panes.FirstOrDefault(pane => pane.Team == card.Team && pane.Running && pane.Working.Contains(card.Number));

    /// <summary>The Dashboard, with the agent on the selected card expanded on the run that's on it.</summary>
    private void GoToSession()
    {
        if (SessionFor(_overseer.Board.SelectedCard) is not { } pane || _overseer.Board.SelectedCard is not { } card)
            return;
        Show(Area.Dashboard);
        var index = _panes.IndexOf(pane);
        SetExpanded(index);
        pane.SetFocus();
        pane.ShowTask(card.Number);
    }

    /// <summary>The last read landed, began under five minutes ago, and no run has started or finished since.</summary>
    private bool UpToDate() =>
        _askedAt is { } asked && _readAt >= asked && _clock.GetUtcNow() - asked < ReadEvery
        && _activityRead.SequenceEqual(Activity());

    /// <summary>There are no cards to look at until the first read lands; a later read leaves the ones already
    /// on screen where they are.</summary>
    private void ShowLoading() => _loading.Visible =
        _area == Area.Work && _reading is not null && _work.Unread
        || _area == Area.Overseer && _boarding is not null && _overseer.Unread;

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

    private void HandOverTry(WaitingItem item, ReaderPlace? reader = null)
    {
        if (_handOver is null)
            return;
        _attention.Open(item, WorkView.GateOf(item));
        Seen(item);
        _attention.Did("tried");
        _attention.HandOver();
        _handOver(new TryHandover(item, _work.SelectedCard is null, _work.Items, _readAt, reader));
    }

    /// <summary>Writes the document pitch's file for your editor, and hands the terminal to it.</summary>
    private void HandOverEdit(WaitingItem item, DocumentPitch document, ReaderPlace reader)
    {
        if (_handOver is null)
            return;
        var file = DocumentHandover.FileFor(item, document);
        try
        {
            File.WriteAllText(file, document.Text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _attention.Close();
            _failure = $"couldn't write {document.Path} for your editor: {e.Message}";
            ShowMessage();
            return;
        }
        _attention.Did("edited");
        _attention.HandOver();
        _handOver(new DocumentHandover(item, document, file, EditorHandover.Command(Environment.GetEnvironmentVariable),
            _work.SelectedCard is null, _work.Items, _readAt, reader));
    }

    /// <summary>Back from a try, the cards as they were with no re-read; from an attach, the grid. Either way, what
    /// went wrong if it failed.</summary>
    private void Resume(Handover handover)
    {
        _resume = handover;
        _failure = handover.Failure;
        if (handover is TeamsChanged changed)
            _left = changed.Left;
        if (handover is VisionHandover vision)
            _left = vision.Left;
        if (handover is TryHandover tried)
        {
            _attention.TakeBack();
            if (tried.Reader is null)
                _attention.Close();
            _readAt = _askedAt = tried.ReadAt;
            _work.Show(tried.Items);
            ReadTrend();
            if (tried.Reader is { } place)
            {
                _reopen = (tried.Item, place, _failure, null, false);
                _failure = null;
            }
        }
        if (handover is DocumentHandover edited)
        {
            _attention.TakeBack();
            _readAt = _askedAt = edited.ReadAt;
            _work.Show(edited.Items);
            ReadTrend();
            _reopen = (edited.Item, edited.Reader, _failure, edited.Said, edited.Committed);
            _failure = null;
        }
        ShowMessage();
    }

    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        if (newIsRunning)
        {
            FocusResumed();
            // Invoke would run it here, before the run loop starts, and the loop would then drop a try's RequestStop.
            App?.AddTimeout(TimeSpan.Zero, () =>
            {
                ReopenReader();
                return false;
            });
            App?.AddTimeout(OverseerView.SpinEvery, () =>
            {
                if (_area == Area.Overseer)
                    _overseer.Spin();
                return IsRunning;
            });
        }
    }

    /// <summary>Back from a try or an edit started in the reader, the same reader, where it was: read again where the
    /// edit was committed.</summary>
    internal void ReopenReader()
    {
        if (_reopen is not { } reopen)
            return;
        _reopen = null;
        if (reopen.Reread)
            ReadBody(reopen.Item, (read, body) => ShowBody(read, body, reopen.Place.Url, reopen.Place.Top, reopen.Failure, said: reopen.Said),
                forReader: true);
        else
            ShowBody(reopen.Item, reopen.Place.Body, reopen.Place.Url, reopen.Place.Top, reopen.Failure, said: reopen.Said);
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
            case DocumentHandover edited when !_work.Focus(edited.Item, edited.OnPr):
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
            case VisionHandover vision when _panes.FindIndex(pane => pane.Team == vision.Team && pane.Role == vision.Role) is >= 0 and var index:
                Select(index);
                break;
        }
    }

    private void OpenSelected()
    {
        if (_work.SelectedUrl is { Length: > 0 } url)
            Open(url);
    }

    /// <summary>The team of the selected column in Work, of the selected chip in Overseer, or of the selected agent
    /// on the dashboard.</summary>
    internal string? SelectedTeam() => _area switch
    {
        Area.Work => _work.Team,
        Area.Overseer => _overseer.Board.Selected?.Team,
        _ => Selected()?.Team,
    };

    /// <summary>Hands the terminal to the vision interview for <paramref name="team"/>, to come back where it is now.</summary>
    /// <summary>Asks for a review whatever the team's trigger, then runs a pass so the Lead starts on it now.</summary>
    private void ReviewVision(string? team)
    {
        if (team is null || Already($"{team}/vision"))
            return;
        Queue(new Write(WriteKind.VisionReview, $"{team}/vision", ["board", team, "vision-review", "you"],
            "Asking for a vision review…", $"A vision review for {team} is already being asked for",
            $"asking for {team}'s vision review", Team: team));
    }

    private void WriteVision(string? team)
    {
        if (team is null)
            return;
        _handOver?.Invoke(new VisionHandover(team, _area, _area == Area.Work ? _work.Place : _left,
            _area == Area.Dashboard ? Selected()?.Role : null));
    }

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
        _failure = null;
        Open(url);
    }

    /// <summary>Opens the reader with the ranks under it, starting on the card's own.</summary>
    private void SetPriority()
    {
        if (_work.SelectedCard is { } item)
            ReadRanking(item);
    }

    private void ReadSelected() => ReadSelected(declining: false);

    /// <summary>A card in Triage is waiting for a rank, so it opens with the ranks under it.</summary>
    private void ReadSelected(bool declining, bool shelving = false)
    {
        if ((declining || shelving ? _work.SelectedCard : _work.Selected) is not { } item)
            return;
        if (_work.InTriage && _work.SelectedCard is { } card)
        {
            ReadRanking(card, declining);
            return;
        }
        var url = _work.SelectedUrl;
        ReadBody(item, (read, body) => ShowBody(read, body, url, declining: declining, shelving: shelving), forReader: true);
    }

    private void ReadRanking(WaitingItem item, bool declining = false)
    {
        var url = _work.SelectedUrl;
        ReadBody(item, (read, body) => ShowBody(read, body, url, rank: Priorities.Starting(read), declining: declining),
            forReader: true);
    }

    private void ReadBody(WaitingItem item, Action<WaitingItem, IssueBody> then, bool forReader = false)
    {
        if (_readingBody is not null)
            return;
        _failure = null;
        _progress = $"Reading #{item.Number}…";
        ShowMessage();
        _readingBody = (item, Read(item, forReader), then);
    }

    /// <summary>The reads start at once; a conversation or history that won't read still leaves the body to show.</summary>
    private async Task<IssueBody> Read(WaitingItem item, bool forReader)
    {
        var body = _readBody(item);
        var conversation = forReader ? _readConversation?.Invoke(item) : null;
        var history = forReader ? _readHistory?.Invoke(item) : null;
        var opened = forReader ? _opened.Last(item.Team, item.Number) : null;
        var read = await Settled(body, reading => IssueBody.Of(reading, item.Number),
            new IssueBody(Failure: $"couldn't read #{item.Number}")).ConfigureAwait(false);
        if (read.Failure is not null)
            return read;
        read = read with { Opened = opened };
        if (conversation is not null)
            read = read.With(await Settled(conversation, reading => Conversation.Of(reading, item.Number),
                new Conversation([], $"couldn't read the conversation on #{item.Number}")).ConfigureAwait(false), opened);
        if (forReader && item.Document && _readDocument?.Invoke(item) is { } reading
            && await Settled(reading, read => read.Failure is null ? DocumentPitch.Parse(read.Output) : null, null).ConfigureAwait(false) is { } document)
            read = read.With(document);
        if (forReader)
            read = read.Headed(item.PrHeading);
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
    private void ShowBody(WaitingItem item, IssueBody body, string? url, int? top = null, string? failure = null,
        Rank? rank = null, bool declining = false, string? said = null, bool shelving = false)
    {
        if (rank is null && body.Failure is { Length: > 0 } unread)
            _failure = unread;
        else if (rank is null && string.IsNullOrWhiteSpace(body.Text))
            _failure = $"#{item.Number} has no description";
        else
        {
            _approvable = item.Approvable ? item : null;
            _shown = item;
            _attention.Open(item, WorkView.GateOf(item));
            Seen(item);
            _idea = NewIdeaEnabled() ? new ReaderIdea(_commands.KeyFor("work.new"), description => AddIdea(item.Team, description)) : null;
            _image = new ReaderImage();
            var chosen = _showBody(item, body, () => url is { Length: > 0 } ? _openUrl(url) : null, _approvable is null ? null : Approve,
                item.Acceptable ? new ReaderCommand(_commands.KeyFor("work.accept"), "accept", Accept, item.Unacceptable.Length == 0) : null,
                new ReaderComment(_commands.KeyFor("work.comment"), "comment", body => Post(item, body), _clock,
                    item.Declinable ? new ReaderDecline(_commands.KeyFor("work.decline"), reason => Decline(item, reason), declining) : null,
                    item.Shelvable ? new ReaderShelve(_commands.KeyFor("work.notNow"), (weeks, reason) => Shelve(item, weeks, reason), shelving) : null,
                    Attaching(), _image, item.Held ? new ReaderRelease(comment => Release(item, comment)) : null),
                item.Triable ? new ReaderTry(_commands.KeyFor("work.try"), (shown, at) => _triedFrom = (shown, at), top, failure) : null,
                item.Document ? new ReaderEdit(_commands.KeyFor("work.edit"), (shown, at) => _editFrom = (shown, at),
                    body.Document is { } document ? document.Uneditable(item.Number) : $"couldn't read what #{item.Number} changes",
                    top, said, failure) : null,
                _commands.KeyFor("work.copyBranch"),
                _idea,
                key => (key == _commands.KeyFor("help") || key == _commands.KeyFor("commands")) && _commands.Press(key),
                rank);
            _idea = null;
            _image = null;
            _opened.Open(item.Team, item.Number, _clock.GetUtcNow());
            _approvable = null;
            _shown = null;
            var reranked = chosen is { } ranked && ranked != Priorities.Of(item) ? chosen : null;
            if (reranked is not null)
                _attention.Did("ranked");
            if (_triedFrom is null && _editFrom is null)
                _attention.Close();
            if (_leaving == item)
            {
                _leaving = null;
                return;
            }
            if (reranked is { } to)
                SetRank(item, to);
            if (_triedFrom is { } from)
            {
                _triedFrom = null;
                HandOverTry(item, new ReaderPlace(from.Body, url, from.Top));
            }
            if (_editFrom is { } edit && body.Document is { Editable: true } edited)
            {
                _editFrom = null;
                HandOverEdit(item, edited, new ReaderPlace(edit.Body, url, edit.Top));
            }
        }
    }

    private Attaching? Attaching() => _attaching?.Invoke(_commands.KeyFor("work.attach"));

    private bool NewIdeaEnabled() => _newIdea is not null && _teamNames.Count > 0;

    /// <summary>Adds an idea to the team whose lane you're in, or the first team's, or from what the reader shows.</summary>
    private void NewIdea()
    {
        if (_idea?.Start is { } fromReader)
            fromReader();
        else
            AddIdea(_area == Area.Work && _work.Current is { } lane ? lane.Team : _teamNames[0]);
    }

    /// <summary>Reads Work again to show the idea added, and says which it was.</summary>
    private string? AddIdea(string team, string description = "")
    {
        if (_newIdea?.Invoke(_teamNames, team, description, Attaching()) is not { } added)
            return null;
        _failure = null;
        _added = $"#{added.Number} added to {added.Team}'s ideas";
        ReadWaiting();
        return _added;
    }

    /// <summary>Approves the pitch the reader is showing once you've said so. Like a rank, the card stays put until the
    /// board takes it. True once it's under way, or already was.</summary>
    private bool Approve()
    {
        if (_approvable is not { } item)
            return false;
        if (Already(Write.KeyOf(item)))
            return true;
        if (!_confirmApprove(item))
            return false;
        _attention.Did("approved");
        Queue(new Write(WriteKind.Approve, Write.KeyOf(item), ["board", item.Team, "approve", "you", item.Number.ToString()],
            $"Approving #{item.Number}…", $"#{item.Number} is already being approved", $"approving #{item.Number}", item));
        return true;
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
    /// it's under way, or already was.</summary>
    private bool Accept()
    {
        if (Acceptable() is not { } item)
            return false;
        if (Already(Write.KeyOf(item)))
            return true;
        if (item.Unacceptable is { Length: > 0 } why)
        {
            _failure = why;
            ShowMessage();
            return false;
        }
        if (!_confirmAccept(item))
            return false;
        _attention.Did("accepted");
        var (doing, being, queued) = item.Document ? ($"Merging #{item.Number}…", "merged", $"merging #{item.Number}")
            : item.Pitch ? ($"Closing #{item.Number}…", "closed", $"closing #{item.Number}")
            : ($"Merging PR #{item.Pr}…", "merged", $"merging PR #{item.Pr}");
        Queue(new Write(WriteKind.Accept, Write.KeyOf(item), ["board", item.Team, "accept", "you", item.Number.ToString()],
            doing, $"#{item.Number} is already being {being}", queued, item));
        return true;
    }

    private async Task<string?> Post(WaitingItem item, string body)
    {
        var refused = await WithFile(item, "comment", body).ConfigureAwait(false);
        if (refused is null or { Length: 0 })
            _attention.Did("commented");
        return refused;
    }

    /// <summary>Closes the item as not planned with your reason. The reader closes on it at once, and the card leaves
    /// once the board has taken it.</summary>
    private Task<string?> Decline(WaitingItem item, string reason)
    {
        if (_writes.Holding(Write.KeyOf(item)) is { } already)
            return Task.FromResult<string?>(already.Already);
        var file = Path.GetTempFileName();
        File.WriteAllText(file, reason);
        _attention.Did("declined");
        _leaving = item;
        Queue(new Write(WriteKind.Decline, Write.KeyOf(item), ["board", item.Team, "decline", "you", item.Number.ToString(), file],
            $"Declining #{item.Number}…", $"#{item.Number} is already being declined", $"declining #{item.Number}", item,
            Scratch: file));
        return Task.FromResult<string?>(null);
    }

    private void Declined(WaitingItem item)
    {
        _leaving = null;
        _work.Leave(item);
        _failure = null;
        _said = $"#{item.Number} declined";
        _saidOn = _work.Selected;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Shelves the pitch for <paramref name="weeks"/>, with your reason if you gave one. Like a decline, the
    /// reader closes on it at once, and the card leaves once the board has taken it.</summary>
    private Task<string?> Shelve(WaitingItem item, int weeks, string reason)
    {
        if (_writes.Holding(Write.KeyOf(item)) is { } already)
            return Task.FromResult<string?>(already.Already);
        var file = Path.GetTempFileName();
        File.WriteAllText(file, reason);
        _attention.Did("shelved");
        _leaving = item;
        Queue(new Write(WriteKind.Shelve, Write.KeyOf(item),
            ["board", item.Team, "shelve", "you", item.Number.ToString(), weeks.ToString(), file],
            $"Shelving #{item.Number}…", $"#{item.Number} is already being shelved", $"shelving #{item.Number}", item,
            Scratch: file, Weeks: weeks));
        return Task.FromResult<string?>(null);
    }

    /// <summary>Holds the selected Ready task with the reason you give, or takes the hold off one that's held.</summary>
    private void HoldOrRelease()
    {
        if (_work.SelectedCard is not { Holdable: true } item || Already(Write.KeyOf(item)))
            return;
        if (item.Held)
        {
            Release(item, "");
            return;
        }
        if (_askHold(item) is not { Length: > 0 } reason)
            return;
        var file = Path.GetTempFileName();
        File.WriteAllText(file, reason);
        _attention.Did("held");
        Queue(new Write(WriteKind.HoldTask, Write.KeyOf(item), ["board", item.Team, "hold", "you", item.Number.ToString(), file],
            $"Holding #{item.Number}…", $"#{item.Number} is already being held", $"holding #{item.Number}", item, Scratch: file));
    }

    /// <summary>Posts what you wrote, if anything, then takes the hold off, whoever set it. Like a decline, the reader
    /// closes on it at once, and the card leaves once the board has taken it.</summary>
    private Task<string?> Release(WaitingItem item, string comment)
    {
        if (_writes.Holding(Write.KeyOf(item)) is { } already)
            return Task.FromResult<string?>(already.Already);
        _attention.Did("released");
        if (_shown is not null)
            _leaving = item;
        var release = new Write(WriteKind.Release, Write.KeyOf(item), ["board", item.Team, "release", "you", item.Number.ToString()],
            $"Releasing #{item.Number}…", $"#{item.Number} is already being released", $"releasing #{item.Number}", item);
        if (string.IsNullOrWhiteSpace(comment))
        {
            Queue(release);
            return Task.FromResult<string?>(null);
        }
        return PostThenRelease(item, comment, release);
    }

    private async Task<string?> PostThenRelease(WaitingItem item, string comment, Write release)
    {
        var refused = await WithFile(item, "comment", comment).ConfigureAwait(false);
        if (refused is { Length: > 0 })
        {
            _leaving = null;
            return refused;
        }
        Queue(release);
        return null;
    }

    private void Held(WaitingItem item)
    {
        _added = $"#{item.Number} held";
        ReadWaiting();
    }

    private void Released(WaitingItem item)
    {
        _leaving = null;
        _work.Leave(item);
        _failure = null;
        _said = $"#{item.Number} back in the Dev's queue";
        _saidOn = _work.Selected;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void Shelved(WaitingItem item, int weeks)
    {
        _leaving = null;
        _work.Leave(item);
        _failure = null;
        _said = $"#{item.Number} back on {ReaderShelve.BackOn(_clock.GetUtcNow(), weeks)}";
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
        _said = item.Document ? $"merged #{item.Number}" : item.Pitch ? $"accepted #{item.Number}" : $"merged PR #{item.Pr}";
        _saidOn = _work.Selected;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Writes the rank. The board decides what the field will take, so an unknown value comes back as a
    /// refusal rather than being guessed at here.</summary>
    private void SetRank(WaitingItem item, Rank rank)
    {
        if (Already(Write.KeyOf(item)))
            return;
        Queue(new Write(WriteKind.Rank, Write.KeyOf(item), ["board", item.Team, "priority", "you", item.Number.ToString(), Priorities.Value(rank)],
            $"Setting #{item.Number} to {rank}…", $"#{item.Number} is already being ranked", $"setting #{item.Number} to {rank}",
            item, rank));
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
        foreach (var team in _teamNames)
            _news.Read(team, _work.Items, _readingAsked);
        if (_area == Area.Work && _work.Selected is null)
        {
            if (_left is not { } left || !_work.Focus(left))
                _work.FocusFirstCard();
            _left = null;
        }
        GoToWaiting();
        // Said once the cards are laid out again: laying them out moves the selection, which clears what's said.
        if (_added is { } added)
        {
            _added = null;
            _said = added;
            _saidOn = _work.Selected;
        }
        ReadTrend();
    }

    /// <summary>After the reader has closed, so a write it queued that's already done lands on the same refresh.</summary>
    private void SettleWrites()
    {
        var settled = _writes.Settle();
        if (settled.Count == 0)
            return;
        _already = null;
        _work.ShowWriting(Writing());
        foreach (var (write, refused) in settled.Where(done => done.Failure is not { Length: > 0 }))
            Settled(write);
        var refusals = settled.Where(done => done.Failure is { Length: > 0 })
            .Select(done => $"{done.Write.Queued}: {done.Failure}").ToList();
        if (refusals.Count > 0)
            _failure = string.Join(" · ", refusals);
    }

    /// <summary>Nothing moves until the board has taken it: a refused write leaves its card as it was.</summary>
    private void Settled(Write write)
    {
        switch (write.Kind)
        {
            case WriteKind.Accept:
                Merged(write.Item!);
                break;
            case WriteKind.Approve:
                Approved(write.Item!);
                break;
            case WriteKind.Rank:
                Ranked(write.Item!, write.Rank);
                break;
            case WriteKind.Decline:
                Declined(write.Item!);
                break;
            case WriteKind.Shelve:
                Shelved(write.Item!, write.Weeks);
                break;
            case WriteKind.HoldTask:
                Held(write.Item!);
                break;
            case WriteKind.Release:
                Released(write.Item!);
                break;
            case WriteKind.VisionReview:
                _pass.Start();
                _said = $"vision review asked for · {write.Team}: the Lead starts it now";
                _saidOn = _work.Selected;
                break;
        }
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
        var title = WorkTrend.Title(_readAt is null ? null : _work.Items.Count(item => !item.WaitsUpstream), _trends, _clock.GetUtcNow());
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
        if (_already is { } moved && _work.Selected != moved.On)
            _already = null;
        var (text, scheme) =
            _failure is { Length: > 0 } ? (_failure, Schemes.Error)
            : _reported is { } reported ? reported
            : _reading is not null ? ("Reading…", Schemes.Accent)
            : _progress is { Length: > 0 } ? (_progress, Schemes.Accent)
            : _already is { } already ? (already.Text, Schemes.Accent)
            : _writes.Status() is { Length: > 0 } writing ? (writing, Schemes.Accent)
            : _pass.Progress is { } passing ? (passing, Schemes.Accent)
            : _said is { Length: > 0 } ? (_said, Schemes.Accent)
            : _area == Area.Work && _work.Selected is { } card && (card.Reason.Length > 0 || card.Holding.Count > 0) ? (card.Line, Schemes.Base)
            : _area == Area.Work && _work.Region is { } region ? (region, Schemes.Base)
            : _area == Area.Overseer && _boarding is not null ? ("Reading…", Schemes.Accent)
            : _area == Area.Overseer && _overseer.Region is { } chip ? (chip, Schemes.Base)
            : ("", Schemes.Base);
        var stamp = _area switch
        {
            Area.Work => Stamped(_readAt, _clock.GetUtcNow()),
            Area.Overseer => Stamped(_boardReadAt, _clock.GetUtcNow()),
            _ => "",
        };
        var place = InAssistant ? "Assistant" : _area switch
        {
            Area.Work => _work.OnlyMine ? MyItems : AllItems,
            Area.Overseer => "Overseer",
            _ => "",
        };
        _status.ShowState(place, stamp);
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

    private int MenuTitlesEnd() =>
        _menu.Bar.Frame.X + _menu.Bar.SubViews.Where(view => view.Visible).Select(view => view.Frame.Right).DefaultIfEmpty(0).Max();

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
        _overseer.Visible = area == Area.Overseer;
        _menu.Show(area);
        if (area == Area.Work)
        {
            if (!UpToDate())
                ReadWaiting();
            if (_left is not { } left || !_work.Focus(left))
                _work.FocusFirstCard();
        }
        else if (area == Area.Overseer)
        {
            ReadBoards();
            ShowOverseer(_clock.GetUtcNow());
            _overseer.SetFocus();
        }
        else
            _panes.FirstOrDefault()?.SetFocus();
        ShowAsking();
        ShowMessage();
        ShowLoading();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Claude Code down the right of whichever area is showing, working in the state folder.</summary>
    private AssistantPane NewAssistant(AssistantSetup setup)
    {
        var pane = setup.Pane(_stateRoot, action => App?.Invoke(action), () => App);
        pane.X = Pos.Func(_ => BesideAssistant(), this);
        pane.Y = MenuLines;
        pane.Width = Dim.Func(_ => AssistantWidth(), this);
        pane.Height = Dim.Func(_ => Math.Max(0, Viewport.Height - MenuLines - StatusLines), this);
        pane.Visible = false;
        pane.HasFocusChanged += (_, _) => ShowMessage();
        return pane;
    }

    private int AssistantWidth() => _assistant is { Visible: true } ? Math.Min(AssistantColumns, Viewport.Width / 2) : 0;

    private int BesideAssistant() => Math.Max(0, Viewport.Width - AssistantWidth());

    private bool InAssistant => _assistant is { Visible: true, HasFocus: true };

    /// <summary>A key the question box would type, which mustn't reach a single-letter command instead.</summary>
    private static bool IsTyping(Key key) => !key.IsCtrl && !key.IsAlt && key.AsRune.Value >= ' ';

    /// <summary>Opens the pane with focus in its question box and the agent starting, or closes it.</summary>
    private void ToggleAssistant()
    {
        if (_assistant is not { } pane)
            return;
        pane.Visible = !pane.Visible;
        if (pane.Visible)
        {
            ShowAsking();
            pane.FocusInput();
            _ = pane.Controller.EnsureStartedAsync().ContinueWith(started => _ = started.Exception, TaskScheduler.Default);
        }
        else
            FocusArea();
        ShowMessage();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Between the area and the Assistant, the only two panes there are while it's open.</summary>
    private void SwitchPane()
    {
        if (_assistant is not { Visible: true } pane)
            return;
        if (pane.HasFocus)
            FocusArea();
        else
            pane.FocusInput();
        ShowMessage();
    }

    private void FocusArea()
    {
        switch (_area)
        {
            case Area.Work:
                _work.SetFocus();
                break;
            case Area.Overseer:
                _overseer.SetFocus();
                break;
            default:
                if (_onDispatcher)
                    _dispatchFrame.SetFocus();
                else
                    (Selected() ?? _panes.FirstOrDefault())?.SetFocus();
                break;
        }
    }

    /// <summary>What the next question is about, above the question box.</summary>
    private void ShowAsking()
    {
        if (_assistant is not { Visible: true } pane)
            return;
        var context = AssistantBrief.Context(_area, AskingAbout(), _teamNames.Count > 1);
        if (pane.NextContext != context)
            pane.NextContext = context;
    }

    private AskedAbout? AskingAbout() => _area switch
    {
        Area.Work => _work.Selected is { } item ? new AskedAbout(item.Team, item.Number) : _work.Team is { } team ? new AskedAbout(team) : null,
        Area.Overseer => _overseer.Board.SelectedCard is { } card ? new AskedAbout(card.Team, card.Number)
            : _overseer.Board.Selected is { } chip ? new AskedAbout(chip.Team) : null,
        _ => _onDispatcher ? new AskedAbout(null, Dispatcher: true) : Selected() is { } pane ? new AskedAbout(pane.Team, Role: pane.Role) : null,
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _assistant?.Controller.Dispose();
        base.Dispose(disposing);
    }

    private void Copy(Func<AgentPane, LogCopy> copy)
    {
        if (Selected() is not { } pane)
            return;
        var copied = copy(pane);
        _reported = Copying.Said(Copying.Copy(_clipboard ?? App?.Clipboard, copied.Text), truncated: copied.Capped);
        ShowMessage();
    }

    private void CopyBranch()
    {
        if (_work.Selected is not { Branch.Length: > 0 } card)
            return;
        _reported = Copying.Said(Copying.Copy(_clipboard ?? App?.Clipboard, card.Branch), card.Branch);
        ShowMessage();
    }

    private void OpenCommands()
    {
        if (App is { } app)
            CommandsDialog.Show(app, _commands, Offered);
    }

    private void GoToTeam()
    {
        if (App is { } app && CommandsDialog.Pick(app, "Go to team", _teamNames) is { } team)
            _work.Pick(team);
    }

    private void OpenKeys()
    {
        if (App is { } app)
            KeysSheet.Show(app, Keys);
    }

    internal KeySheet Keys => KeysSheet.For(Offered, Region);

    /// <summary>While the reader is open, only what works from it: the rest acts on the board underneath.</summary>
    private IReadOnlyList<CommandDescriptor> Offered =>
        _shown is null ? _commands.Enabled : [.. _commands.Enabled.Where(command => command.Id is "work.new" or "work.viewImage" or "help" or "commands")];

    /// <summary>Where the window is now, for the keys that work only there.</summary>
    internal CommandScope Region => InAssistant ? Regions.Assistant : _area switch
    {
        Area.Work => Regions.Work,
        Area.Overseer => Regions.Overseer,
        _ => _expanded is not null || _dispatcherExpanded ? Regions.Log : Regions.Agents,
    };

    private void OpenAbout()
    {
        if (App is { } app)
            AboutDialog.Show(app, _version);
    }

    /// <summary>What's waiting now is Work's count, once it has read one.</summary>
    private void OpenTrends() =>
        _showTrends?.Invoke([.. _teamNames.Select(team => (team, _readAt is null ? (int?)null : _work.Items.Count(item => item.Team == team && !item.WaitsUpstream)))]);

    private void OpenSpending() => _showSpending?.Invoke(_teamNames, OpenPitch);

    /// <summary>The reader opens over Spending once the pitch has been read.</summary>
    private void OpenPitch(string team, SpendingItem pitch)
    {
        var item = new WaitingItem(pitch.Number, pitch.Title, pitch.Status, pitch.Url, team, Pitch: true);
        ReadBody(item, (read, body) => ShowBody(read, body, pitch.Url), forReader: true);
    }

    private void OpenSettings(string? page = null, bool newTeam = false)
    {
        if (App is not { } app)
            return;
        var before = _teams.Names();
        var (removed, vision) = SettingsDialog.Show(app, _settings, _commands, ShowIcons, _auto, _teams, page, _start, newTeam, _showGuide, _checks, _macKeys);
        if (vision is not null && _teams.Names().Contains(vision))
        {
            WriteVision(vision);
            return;
        }
        if (_teams.Names().Except(before).Any() || RolesChanged())
        {
            _handOver?.Invoke(new TeamsChanged(_area, _area == Area.Work ? _work.Place : _left));
            return;
        }
        Forget(removed);
        _limits = _settings.ReadLimits();
        SyncQuitKey();
        ShowKeysHint();
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
        _overseer.Forget(teams);
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
        _overseer.ShowIcons(drawn);
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
