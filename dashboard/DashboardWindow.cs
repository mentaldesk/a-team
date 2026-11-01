using System.Globalization;
using System.Reflection;
using AssistantPane = MentalDesk.Tui.Assistant.AssistantPane;
using MentalDesk.Tui;
using MentalDesk.Tui.Chrome;
using MentalDesk.Tui.Commands;
using MentalDesk.Tui.Keys;
using MentalDesk.Tui.Links;
using Terminal.Gui;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;

namespace ATeam.Dashboard;

/// <summary>The areas the app opens in: everything waiting on the reviewer, or every team's board.</summary>
public enum Area
{
    Work,
    Overseer,
}

public sealed class DashboardWindow : Window
{
    private const int MenuLines = 1;
    private const int StatusLines = 1;
    private const int TitleLines = 1;
    private const int BrokeLines = 20;
    private const string BrokeRule = "── the dispatcher's own output since then ──";
    private const int TitleMargin = 4;
    private const string AllItems = "All items";
    private const string MyItems = "My items";
    private const string CopyLines = "log.copyLines";
    private const int AlertGap = 3;
    private const int AssistantColumns = 48;
    private const int WideColumns = 140;

    // Cmd+C, once the Mac keys are installed.
    private static readonly Key MacCopy = Key.C.WithCtrl;
    private readonly List<RoleWatch> _watches = [];
    private readonly CommandRegistry _commands = new();
    private readonly string _version = Version();
    private readonly AppMenu _menu;
    private readonly AppStatusBar _bar = new();
    private readonly MoveAlert _alert = new();
    private readonly FrameView _dispatchFrame;
    private readonly LogView _dispatchAll;
    private readonly WorkView _work;
    private readonly OverseerView _overseer;
    private readonly Func<string, Task<Reading>>? _readBoard;
    private TeamReads? _boarding;
    private readonly Func<string, Task<Reading>>? _syncBoard;
    private TeamReads? _syncing;
    private readonly Dictionary<string, DateTimeOffset> _syncedAt = [];
    private readonly Dictionary<string, DateTimeOffset> _boardReadAt = [];
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
    private string? _added;
    private ReaderIdea? _idea;
    private ReaderImage? _image;
    private TeamReads? _reading;
    private readonly Dictionary<string, DateTimeOffset> _teamReadAt = [];
    private Task<Reading[]>? _trending;
    private IReadOnlyList<WorkTrend> _trends = [];
    private (WaitingItem Item, Task<IssueBody> Read, Action<WaitingItem, IssueBody> Then)? _readingBody;
    private DateTimeOffset? _readAt;
    private DateTimeOffset? _askedAt;
    private readonly Dictionary<string, StatusMessage> _readFailures = [];
    private readonly Dictionary<string, StatusMessage> _boardFailures = [];
    private readonly Dictionary<string, StatusMessage> _checkFailures = [];
    private StatusMessage? _passFailed;
    private string? _progress;
    private bool _dispatcherOpen;
    private IReadOnlyList<string> _broke = [];
    private (long Length, DateTime Written) _wholeRead;
    private IReadOnlyList<LogLine> _whole = [];
    private IconStyle _drawn = IconStyle.Unicode;
    private Handover? _resume;
    private Place? _left;
    private string[] _activityRead = [];
    private (IssueBody Body, int Top)? _triedFrom;
    private (WaitingItem Item, ReaderPlace Place, string? Failure, string? Said, bool Reread)? _reopen;
    private (IssueBody Body, int Top)? _editFrom;
    private readonly IClipboard? _clipboard;
    private readonly MacKeys? _macKeys;
    private readonly AssistantPane? _assistant;
    private readonly CardPanel _card;
    private readonly TeamPanel _team = new() { Visible = false };
    private readonly Tabs _side;
    private readonly View _detailsTab;
    private readonly View _assistantTab;
    private CardRun? _cardRun;
    private bool _sessionFull;
    private bool _sessionFromRoles;
    private (string Team, int? Number, string Role, LogPlace? Full)? _backTo;

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
        AssistantSetup? assistant = null,
        Func<string, Task<Reading>>? syncBoard = null)
    {
        _syncBoard = syncBoard;
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
        _bar.Clock = _clock;
        _bar.MessagesRequested += (_, _) => _commands.Execute("messages");
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

        foreach (var (team, role) in agents)
            _watches.Add(new RoleWatch(team, role, Path.Combine(stateRoot, team, role)));

        _dispatchFrame = new FrameView
        {
            Title = "dispatcher",
            X = 0,
            Y = MenuLines,
            Width = Dim.Func(_ => BesideAssistant(), this),
            Height = Dim.Func(_ => Math.Max(0, Viewport.Height - MenuLines - StatusLines), this),
            CanFocus = true,
            Visible = false,
        };
        _dispatchAll = new LogView { Width = Dim.Fill(), Height = Dim.Fill(), Elides = true, Scrolls = true, SchemeName = BaseScheme };
        _dispatchFrame.Add(_dispatchAll);
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
            Height = Dim.Func(_ => Math.Max(0, Viewport.Height - MenuLines - StatusLines - TeamFoot()), this),
            Visible = area == Area.Overseer,
        };
        _overseer.SelectionChanged += () =>
        {
            ShowSelection();
            Arrange();
        };
        Add(_overseer);
        _card = new CardPanel(settings.ReadExpandToolCalls()) { Visible = false };
        _detailsTab = new View { Title = "Details", CanFocus = true, Width = Dim.Fill(), Height = Dim.Fill() };
        _assistantTab = new View { Title = "Assistant", CanFocus = true, Width = Dim.Fill(), Height = Dim.Fill() };
        _side = new Tabs
        {
            X = Pos.Func(_ => BesideAssistant(), this),
            Y = MenuLines,
            Width = Dim.Func(_ => PanelWidth(), this),
            Height = Dim.Func(_ => Math.Max(0, Viewport.Height - MenuLines - StatusLines), this),
            CanFocus = true,
            Visible = false,
        };
        _side.Add(_detailsTab, _assistantTab);
        Add(_card, _team, _side);
        ShowIcons(settings.ReadIcons());
        if (assistant is not null)
            Add(_assistant = NewAssistant(assistant));

        _bar.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - StatusLines), this);
        Add(_bar);

        RegisterCommands();
        _commands.Keys.FocusedScope = () => Region;
        KeyOverrides = settings.KeyOverrides(_commands);
        KeyOverrides.Load();
        SyncQuitKey();
        ShowKeysHint();
        _menu = new AppMenu(_commands, _area, new Dictionary<string, Func<bool>>
        {
            ["view.work"] = () => _area == Area.Work,
            ["view.overseer"] = () => _area == Area.Overseer,
            ["assistant"] = () => _assistant is { Visible: true },
        });
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
        Arrange();
        FrameChanged += (_, _) =>
        {
            if (App is { } app)
                app.Invoke(Arrange);
            else
                Arrange();
        };
    }

    internal IReadOnlyList<RoleWatch> Watches => _watches;

    internal View Dispatcher => _dispatchFrame;

    internal LogView WholeDispatchLog => _dispatchAll;

    internal bool DispatcherOpen => _dispatcherOpen;

    internal WorkView Work => _work;

    internal OverseerView Overseer => _overseer;

    internal AppStatusBar Status => _bar;

    internal MoveAlert Alert => _alert;

    internal MenuBar Menu => _menu.Bar;

    internal IReadOnlyList<(string Id, DimmedMenuItem Item)> MenuItems => _menu.Items;

    internal IReadOnlyList<MenuBarItem> Menus => _menu.Menus;

    internal Area CurrentArea => _area;


    internal CommandRegistry Commands => _commands;

    internal KeyOverrides KeyOverrides { get; }

    internal AssistantPane? Assistant => _assistant;

    internal CardPanel Card => _card;

    internal TeamPanel Team => _team;

    /// <summary>Whether the Assistant and the card's details share the panel beside Overseer, as tabs.</summary>
    internal bool Tabbed => _side.Visible;

    internal bool DetailsInFront => Tabbed && _side.Value == _detailsTab;

    internal bool SessionFull => _sessionFull;

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
        SettleSyncs();
        SettleTrend();
        ShowTitle();
        var now = _clock.GetUtcNow();
        if (_area == Area.Work && Uncovered && (_askedAt is not { } asked || now - asked >= ReadEvery))
            ReadWaiting();
        RefreshWatches(now);

        _broke = DispatcherState.Broke(_stateRoot, BrokeLines);
        ShowPassFailed();
        if (_dispatcherOpen)
        {
            ShowWholeLog();
            ShowDispatcher(now);
        }

        ShowOverseer(now);
        ShowSelection();
        Arrange();
        ShowNews();
        ShowAsking();
        _menu.Refresh();
        ShowMessage();
    }

    private void RefreshWatches(DateTimeOffset now)
    {
        DateTimeOffset? nextCheck = long.TryParse(ReadText(_nextPass), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
        var paused = _watches.Select(watch => watch.Team).Distinct().ToDictionary(team => team, _teams.IsPaused);
        _checks?.Follow(paused.Keys);
        ShowChecks();
        var passing = DispatcherState.Passing(_stateRoot);
        foreach (var watch in _watches)
            watch.Refresh(now, nextCheck, paused[watch.Team], _teams.IsHeld(watch.Team, watch.Role), _checks?.Fatal(watch.Team), passing);
    }

    /// <summary>A team's failed check stays on the bar as news until a later check passes.</summary>
    private void ShowChecks()
    {
        foreach (var (team, failed) in _checks?.Answered() ?? [])
        {
            if (_checkFailures.Remove(team, out var was))
                _bar.Withdraw(was);
            if (failed is not null)
                _checkFailures[team] = Announce(failed, Severity.Error);
        }
    }

    /// <summary>A pass that broke before it could log why stays on the bar until a later pass logs again.</summary>
    private void ShowPassFailed()
    {
        if (_broke.Count > 0 == _passFailed is not null)
            return;
        if (_passFailed is { } was)
            _bar.Withdraw(was);
        _passFailed = _broke.Count > 0 ? Announce($"dispatcher: last pass failed · {_broke[^1]}", Severity.Error) : null;
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

    /// <summary>A team's own read replaces only its cards.</summary>
    private void SettleTeamReads()
    {
        foreach (var (team, (read, asked)) in _teamReads.Where(entry => entry.Value.Read.IsCompleted).ToList())
        {
            _teamReads.Remove(team);
            SettleTeam(team, TeamReads.Of(read), asked);
        }
    }

    /// <summary>One team's read: its cards replace only its own, and one that failed says so in its tab, or on the
    /// bar where it keeps cards already on screen.</summary>
    private void SettleTeam(string team, Reading read, DateTimeOffset asked)
    {
        if (_readFailures.Remove(team, out var was))
            _bar.Withdraw(was);
        if (read.Failure is { Length: > 0 } failure)
        {
            if (_work.Fail(team, FirstLine(failure), RetryHint("work.retry"), () => RetryTeam(team)))
                _readFailures[team] = Announce($"{team}: {failure}", Severity.Error);
            return;
        }
        var items = WaitingItem.Parse(read.Output);
        Sync(team);
        _teamReadAt[team] = _clock.GetUtcNow();
        _news.Read(team, items, asked);
        _work.Show(team, items);
        _work.ShowRed(RedMain.Parse(read.Output), team);
        GoToWaiting();
    }

    private void RetryTeam(string team)
    {
        _work.Reading(team);
        ReadTeam(team);
        ShowMessage();
    }

    private string RetryHint(string id) => $"{_commands.KeysFor(id)} retry";

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
        if (_broke.Count > 0)
            state = state.Failed();
        var width = _dispatchFrame.Frame.Width > 0 ? _dispatchFrame.Frame.Width - TitleMargin : int.MaxValue;
        var title = state.Title(width);
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
        at is { } read ? $"read {Ages.Ago(now - read)} ago" : "";

    /// <summary>While a menu is open it owns the keyboard: its own keys would otherwise run a command as well.</summary>
    protected override bool OnKeyDown(Key key)
    {
        _bar.KeyPressed();
        if (key == MacCopy && !_menu.Bar.IsOpen() && _commands.IsEnabled(CopyLines))
            return _commands.Execute(CopyLines);
        if (InAssistant && _commands.PendingChord is null && IsTyping(key))
            return base.OnKeyDown(key);
        if (InAssistant && key == Key.Esc)
        {
            _assistant!.Stop();
            return true;
        }
        return (!_menu.Bar.IsOpen() && Press(key)) || base.OnKeyDown(key);
    }

    /// <summary>Partway through a chord, the app hands its next key here before any view, so a question box doesn't type it.</summary>
    internal bool Chording(Key key) => _commands.PendingChord is not null && !_menu.Bar.IsOpen() && Press(key);

    /// <summary>Runs or carries on a chord, showing it on the status bar while it's partway, and saying so when a key
    /// ends one without finishing it.</summary>
    private bool Press(Key key)
    {
        var typed = _commands.PendingChord;
        var handled = _commands.Press(key, out var cancelled);
        _bar.SetChord(_commands.PendingChord);
        if (cancelled)
            Reply(key == Key.Esc ? $"{typed} cancelled" : $"{typed} {KeyChord.Display(key)} isn't a key");
        return handled;
    }

    private void RegisterCommands()
    {
        bool OnWork() => _area == Area.Work;
        bool OnOverseer() => _area == Area.Overseer;
        bool Scrollable() => _sessionFull || _dispatcherOpen;
        bool Reading() => Reader() is not null;
        string[] details = ["Show the session full-screen", "Show the whole lane"];
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
            .Register("pane.next", "Next pane", SwitchPane, Key.F6, isEnabled: () => _assistant is { Visible: true } || PanelShown)
            .Register("pane.previous", "Previous pane", SwitchPane, Key.F6.WithShift, isEnabled: () => _assistant is { Visible: true } || PanelShown)
            .Register("log.lineDown", "Select the next line of the log", () => Reader()?.MoveLine(+1, extend: false), Key.CursorDown, isEnabled: Reading, scopes: [Regions.Log])
            .Register("log.lineUp", "Select the line above in the log", () => Reader()?.MoveLine(-1, extend: false), Key.CursorUp, isEnabled: Reading, scopes: [Regions.Log])
            .Register("log.extendDown", "Extend the selection down", () => Reader()?.MoveLine(+1, extend: true), Key.CursorDown.WithShift, isEnabled: Reading, scopes: [Regions.Log])
            .Register("log.extendUp", "Extend the selection up", () => Reader()?.MoveLine(-1, extend: true), Key.CursorUp.WithShift, isEnabled: Reading, scopes: [Regions.Log])
            .Register("log.pageUp", "Scroll the log up", () => Scroll(session => session.Page(-1), log => log.Page(-1)), Key.PageUp, isEnabled: Scrollable, scopes: [Regions.Log])
            .Register("log.pageDown", "Scroll the log down", () => Scroll(session => session.Page(+1), log => log.Page(+1)), Key.PageDown, isEnabled: Scrollable, scopes: [Regions.Log])
            .Register("log.top", "Jump to the top of the log", () => Scroll(session => session.Home(), log => log.Home()), Key.Home, isEnabled: Scrollable, scopes: [Regions.Log])
            .Register("log.bottom", "Jump to the bottom of the log", () => Scroll(session => session.End(), log => log.End()), Key.End, isEnabled: Scrollable, scopes: [Regions.Log])
            .Register("log.toolCalls", "Show tool calls in full", () => Reader()?.ToggleToolCalls(), chord: "Ctrl+T T", isEnabled: Reading, scopes: [Regions.Log])
            .Register(CopyLines, () => "Copy the selected lines", () => Copy(session => session.CopySelection()), new Key('l'), isEnabled: Reading,
                menuLabel: () => "Copy selected lines", scopes: [Regions.Log])
            .Register("log.copyAll", () => "Copy the whole log", () => Copy(session => session.CopyAll()), new Key('a'), isEnabled: Reading,
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
                () => WriteVision(SelectedTeam()), isEnabled: () => _handOver is not null && SelectedTeam() is not null, name: "Write the vision with me")
            .Register("team.visionReview", () => SelectedTeam() is { } team ? $"Review the vision · {team}" : "Review the vision",
                () => ReviewVision(SelectedTeam()), isEnabled: () => SelectedTeam() is not null, name: "Review the vision")
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
            .Register("work.team", () => "Go to team…", GoToTeam, isEnabled: OnWork, menuLabel: () => "Team…", scopes: [Regions.Work], chord: "Ctrl+G T")
            .Register("work.new", "New idea", NewIdea, Key.N.WithCtrl, isEnabled: NewIdeaEnabled)
            .Register("work.mine", () => "Show only what's your move", ToggleOnlyMine, chord: "Ctrl+T M", isEnabled: OnWork,
                menuLabel: () => mine[_work.OnlyMine ? 1 : 0], scopes: [Regions.Work], menuLabels: mine)
            .Register("work.retry", () => "Read this team again", () => RetryTeam(_work.Current!.Team), new Key('r'),
                isEnabled: () => OnWork() && _work.Failed, scopes: [Regions.Work])
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
                isEnabled: () => OnOverseer() && (_overseer.Board.OnMore || _cardRun is not null || Chosen() is { LogPath: not null }),
                scopes: [Regions.Overseer, Regions.Team], menuLabels: details)
            .Register("overseer.fold", () => "Fold the lane", () => _overseer.Fold(), Key.Esc,
                isEnabled: () => OnOverseer() && _overseer.CanFold, scopes: [Regions.Overseer])
            .Register("overseer.github", () => "Open on GitHub", OpenChip, new Key('g'),
                isEnabled: () => OnOverseer() && _overseer.Board.SelectedCard is { Url.Length: > 0 }, scopes: [Regions.Overseer])
            .Register("overseer.retry", () => "Read the boards that failed again", () => RetryBoards(_overseer.FailedTeams), new Key('r'),
                isEnabled: () => OnOverseer() && _overseer.Failed, scopes: [Regions.Overseer])
            .Register("overseer.refresh", () => "Read every board again", ReadBoards, Key.F5, isEnabled: OnOverseer,
                menuLabel: () => "Refresh", scopes: [Regions.Overseer])
            .Register("team.down", "Choose the role below", () => MoveRole(+1), Key.CursorDown, isEnabled: () => _team.HasFocus, scopes: [Regions.Team])
            .Register("team.up", "Choose the role above", () => MoveRole(-1), Key.CursorUp, isEnabled: () => _team.HasFocus, scopes: [Regions.Team])
            .Register("view.work", "Work", () => Show(Area.Work), chord: "Ctrl+G W")
            .Register("view.overseer", "Overseer", () => Show(Area.Overseer), chord: "Ctrl+G O")
            .Register("assistant", () => "Assistant", ToggleAssistant, isEnabled: () => _assistant is not null, chord: "Ctrl+G A")
            .Register("view.details", () => "Details", FocusDetails, isEnabled: () => PanelShown, chord: "Ctrl+G I")
            .Register("view.dispatcher", "Dispatcher log", OpenDispatcher, isEnabled: () => !_dispatcherOpen)
            .Register("work.whatsNew", () => "Go to what's new", GoToNew, isEnabled: () => _news.Newest is not null, menuLabel: () => "What's new", chord: "Ctrl+G N")
            .Register("agent.hold", () => Chosen() is { Held: true } ? hold[1] : hold[0], ToggleHold, new Key('h'),
                isEnabled: () => OnOverseer() && _overseer.Board.OnHeading && Chosen() is not null,
                scopes: [Regions.Overseer, Regions.Team, Regions.Log], menuLabels: hold)
            .Register("agent.interrupt", () => _overseer.Board.OnHeading
                    ? UnlessStopped("Interrupt the chosen role", "Let the chosen role start again")
                    : UnlessStopped("Interrupt the agent on this card", "Let the card's agent start again"), ToggleInterrupt, new Key('i'),
                isEnabled: () => OnOverseer() && (_overseer.Board.OnHeading
                    ? Chosen() is { Running: true } or { Held: true }
                    : _cardRun is { State.Running: true } || CardHeld()),
                menuLabel: () => UnlessStopped(interrupt[0], interrupt[1]), scopes: [Regions.Overseer, Regions.Team, Regions.Log], menuLabels: interrupt)
            .Register("dispatch.pass", "Run a dispatch pass now", PassNow)
            .Register("commands", "Commands", OpenCommands, Key.E.WithCtrl, isEnabled: HasApp)
            .Register("settings", "Settings", () => OpenSettings(), chord: "Ctrl+,", isEnabled: HasApp)
            .Register("teams", "Teams", () => OpenSettings(SettingsDialog.TeamsPage), isEnabled: HasApp)
            .Register("teams.new", "New team", () => OpenSettings(SettingsDialog.TeamsPage, newTeam: true), isEnabled: HasApp)
            .Register("help", () => "Show keys", OpenKeys, Key.F1, isEnabled: HasApp, menuLabel: () => "Show _keys")
            .Register("guide", "Guide", () => _showGuide(_area == Area.Work ? GuideDialog.Work : GuideDialog.Overseer))
            .Register("messages", () => "Messages", OpenMessages, isEnabled: HasApp, menuLabel: () => "Messages…")
            .Register("about", "About", OpenAbout, isEnabled: HasApp)
            .Register("trends", "Trends", OpenTrends, isEnabled: () => _showTrends is not null)
            .Register("spending", "Spending", OpenSpending, isEnabled: () => _showSpending is not null)
            .Register("decisions", () => SelectedTeam() is { } team ? $"Decisions… · {team}" : "Decisions…",
                () => _showDecisions?.Invoke(SelectedTeam()!), isEnabled: () => _showDecisions is not null && SelectedTeam() is not null,
                menuLabel: () => "Decisions…")
            .Register("agent.collapse", () => _dispatcherOpen ? "Back from the dispatcher's log" : "Back to Overseer", Collapse, Key.Esc,
                isEnabled: () => _dispatcherOpen || _sessionFull, menuLabel: () => "Back", scopes: [Regions.Log])
            .Register("quit", "Quit", Quit, Key.Q.WithCtrl);
        for (var tab = 0; tab < 9; tab++)
        {
            var index = tab;
            _commands.Register($"work.team{index + 1}",
                () => index < _work.Lanes.Count ? $"Go to {_work.Lanes[index].Team}" : $"Go to team {index + 1}",
                () => _work.PickTab(index), new Key((char)('1' + index)), isEnabled: () => OnWork() && index < _work.Lanes.Count,
                scopes: [Regions.Work], name: $"Go to team {index + 1}");
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
        Reply(already.Already);
        return true;
    }

    /// <summary>Queues <paramref name="write"/> behind any already under way, so nothing waits on it.</summary>
    private void Queue(Write write)
    {
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
        var keys = _commands.Keys.For("help").FirstOrDefault()?.Display;
        _bar.Hints.Show(keys is null ? [] : [($"Press {keys} for help", () => _commands.Execute("help"))]);
    }

    /// <summary>Pauses the role chosen in the team's panel, or lets it start again.</summary>
    private void ToggleHold()
    {
        if (Chosen() is not { } watch || Already(RoleKey(watch)))
            return;
        Queue(new Write(WriteKind.Hold, RoleKey(watch), [watch.Held ? "resume" : "pause", watch.Team, watch.Role],
            watch.Held ? "Letting it start again…" : "Pausing this role…",
            $"{watch.Team}'s {watch.Role} is already being {(watch.Held ? "let start again" : "paused")}",
            $"{(watch.Held ? "letting" : "pausing")} {watch.Team}'s {watch.Role}{(watch.Held ? " start again" : "")}"));
    }

    private void PassNow()
    {
        _pass.Start();
        ShowMessage();
    }

    private static string RoleKey(RoleWatch watch) => $"{watch.Team}/{watch.Role}";

    private string UnlessStopped(string label, string resume) =>
        (_overseer.Board.OnHeading ? Chosen() is { Held: true } : CardHeld()) ? resume : label;

    private void ToggleInterrupt()
    {
        if (_overseer.Board.OnHeading)
            InterruptRole();
        else
            InterruptCard();
    }

    /// <summary>The chosen role's run, as on its card: a run with a conversation hands you to it, to come back to the
    /// team's panel, and one too new for one is stopped, asking first where it's a task's. A held role starts again.</summary>
    private void InterruptRole()
    {
        if (Chosen() is not { } watch || Already(RoleKey(watch)))
            return;
        var who = $"{watch.Team}'s {watch.Role}";
        if (!watch.Held && !watch.Running)
            return;
        if (!watch.Held && watch.Runs.Count > 1)
        {
            Reply($"{who} is on {watch.Runs.Count} cards: interrupt one from its card", Severity.Error);
            return;
        }
        var task = watch.Held ? null : watch.Runs.FirstOrDefault()?.Task;
        if (!watch.Held && watch.SessionId is not null)
        {
            _handOver?.Invoke(new AttachHandover(watch.Team, watch.Role, task?.Number));
            return;
        }
        if (!watch.Held && task is not null && !_confirmStop(task))
            return;
        string[] target = task is null ? [watch.Team, watch.Role] : [watch.Team, watch.Role, task.Number.ToString(CultureInfo.InvariantCulture)];
        Queue(new Write(WriteKind.Interrupt, RoleKey(watch), [watch.Held ? "resume" : "stop", .. target],
            watch.Held ? "Letting it start again…" : "Interrupting…",
            $"{who} is already being {(watch.Held ? "let start again" : "interrupted")}",
            $"{(watch.Held ? "letting" : "interrupting")} {who}{(watch.Held ? " start again" : "")}"));
    }

    /// <summary>The card's run, stopped or resumed on its own where it's a task's, or else its role's. A run with a
    /// conversation hands you to it, to come back to the card.</summary>
    private void InterruptCard()
    {
        if (_overseer.Board.SelectedCard is not { } card || _cardRun is not { } run || Already($"{card.Team}/{run.Role}"))
            return;
        var resume = CardHeld();
        if (!resume && !run.State.Running)
            return;
        int? task = run.OnTask ? card.Number : null;
        if (!resume && _card.SessionId is not null)
        {
            _handOver?.Invoke(new AttachHandover(card.Team, run.Role, task) { Card = card.Number });
            return;
        }
        if (!resume && task is not null && !_confirmStop(new RunTask(card.Number, card.Title)))
            return;
        string[] target = task is null ? [card.Team, run.Role] : [card.Team, run.Role, card.Number.ToString(CultureInfo.InvariantCulture)];
        var who = $"{card.Team}'s {run.Role}";
        Queue(new Write(WriteKind.Interrupt, $"{card.Team}/{run.Role}", [resume ? "resume" : "stop", .. target],
            resume ? "Letting it start again…" : "Interrupting…",
            $"{who} is already being {(resume ? "let start again" : "interrupted")}",
            $"{(resume ? "letting" : "interrupting")} {who}{(resume ? " start again" : "")}"));
    }

    /// <summary>The card's run is stopped by you: on its own, or with its role held where it isn't a task's.</summary>
    private bool CardHeld() =>
        _overseer.Board.SelectedCard is { } card && _cardRun is { } run
        && (run.Held || (!run.OnTask && _teams.IsHeld(card.Team, run.Role)));

    /// <summary>Writes every line of the shown session to a file and hands the terminal to your editor with it.</summary>
    private void OpenInEditor()
    {
        if (!_sessionFull || _card.LogPath is not { } path)
            return;
        if (_overseer.Board.SelectedCard is { } card && _cardRun is { } run)
            Edit(card.Team, run.Role, path, _card.Place, card.Number);
        else if (Chosen() is { } watch)
            Edit(watch.Team, watch.Role, path, _card.Place, null);
    }

    private void Edit(string team, string role, string log, LogPlace place, int? card)
    {
        var file = Path.Combine(Path.GetTempPath(), $"a-team-{team}-{role}-{_clock.GetUtcNow():yyyyMMddHHmmss}.log");
        try
        {
            File.WriteAllLines(file, SessionLog.Whole(log));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Reply($"couldn't write the log for your editor: {e.Message}", Severity.Error);
            return;
        }
        _handOver?.Invoke(new EditorHandover(team, role, place, file, EditorHandover.Command(Environment.GetEnvironmentVariable)) { Card = card });
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
            _reading = new TeamReads(_teamNames, _readWaiting);
            foreach (var team in _teamNames)
                _work.Reading(team);
        }
        ShowTitle();
        ShowMessage();
    }

    private string[] Activity() => [.. _watches.Select(watch => watch.Activity)];

    /// <summary>Reads every team's whole board at once, and what's waiting for Work's reasons with it. A second go
    /// while one is running is refused, not queued.</summary>
    private void ReadBoards()
    {
        if (_readBoard is null)
            return;
        if (_boarding is null)
        {
            _boarding = new TeamReads(_teamNames, _readBoard);
            foreach (var team in _teamNames)
                _overseer.Reading(team);
        }
        if (_reading is null)
            ReadWaiting();
        ShowMessage();
    }

    /// <summary>Reads again only the boards that couldn't be read.</summary>
    private void RetryBoards(IReadOnlyList<string> teams)
    {
        if (_readBoard is null)
            return;
        _boarding ??= new TeamReads([], _readBoard);
        foreach (var team in teams)
        {
            _overseer.Reading(team);
            _boarding.Add(team);
        }
        ShowMessage();
    }

    /// <summary>Each board as it lands: one that failed says so in its lane, or on the bar where it keeps chips
    /// already on screen, and leaves the other lanes and their stamps as they were.</summary>
    private void SettleBoards()
    {
        if (_boarding is not { } boarding)
            return;
        foreach (var (team, read) in boarding.Landed())
        {
            if (_boardFailures.Remove(team, out var was))
                _bar.Withdraw(was);
            if (read.Failure is { Length: > 0 } failure)
            {
                if (_overseer.Fail(team, FirstLine(failure), RetryHint("overseer.retry"), () => RetryBoards([team])))
                    _boardFailures[team] = Announce($"{team}: {failure}", Severity.Error);
                continue;
            }
            _boardReadAt[team] = _clock.GetUtcNow();
            Sync(team);
            _overseer.Show(team, BoardCard.Parse(read.Output), MirrorLag.Parse(read.Output));
            BackToCard(team);
        }
        if (boarding.Done)
            _boarding = null;
    }

    /// <summary>How soon after a team's last sync with GitHub another read of it syncs again.</summary>
    internal static readonly TimeSpan SyncEvery = TimeSpan.FromMinutes(1);

    /// <summary>Brings <paramref name="team"/>'s record up to date with GitHub behind what's on screen, which the
    /// reads take from the record as it stands.</summary>
    private void Sync(string team)
    {
        var now = _clock.GetUtcNow();
        if (_syncBoard is null || _syncedAt.TryGetValue(team, out var at) && now - at < SyncEvery)
            return;
        _syncedAt[team] = now;
        _syncing ??= new TeamReads([], _syncBoard);
        _syncing.Add(team);
    }

    /// <summary>A team synced is read again, Overseer's lane only where it has been read.</summary>
    private void SettleSyncs()
    {
        if (_syncing is not { } syncing)
            return;
        foreach (var (team, read) in syncing.Landed())
        {
            if (read.Failure is { Length: > 0 })
                continue;
            ReadTeam(team);
            if (_readBoard is not null && _boardReadAt.ContainsKey(team))
                (_boarding ??= new TeamReads([], _readBoard)).Add(team);
        }
        if (syncing.Done)
            _syncing = null;
    }

    /// <summary>Back from a conversation or the editor, on the card you left once its board is read, or straight away on
    /// the team's panel with the role chosen, and its session full-screen where it was.</summary>
    private void BackToCard(string team)
    {
        if (_backTo is not { } back || back.Team != team)
            return;
        _backTo = null;
        if (back.Number is { } number ? !_overseer.Select(back.Team, number) : !_overseer.SelectHeading(back.Team))
            return;
        if (back.Number is null)
        {
            ShowSelection();
            _team.Choose(back.Role);
        }
        if (back.Full is not { } place)
            return;
        ShowSelection();
        OpenSession();
        _card.Restore(place);
    }

    /// <summary>What Overseer draws besides the cards: the limits, and which cards and roles the agents are on.</summary>
    private void ShowOverseer(DateTimeOffset now)
    {
        if (_area != Area.Overseer)
            return;
        var running = _watches.Where(watch => watch.Running).ToList();
        _overseer.Show(new OverseerState(
            now,
            _limits,
            running.SelectMany(watch => watch.Working.Select(number => (Card: (watch.Team, number), watch.Role)))
                .DistinctBy(worked => worked.Card).ToDictionary(worked => worked.Card, worked => worked.Role),
            running.GroupBy(watch => watch.Team).ToDictionary(team => team.Key, team => (IReadOnlyList<string>)[.. team.Select(watch => watch.Role)]),
            (team, number) => _work.Items.FirstOrDefault(item => item.Team == team && item.Number == number))
        {
            Holding = Holds.Holders(_work.Items),
        });
    }

    private void OverseerEnter()
    {
        if (_overseer.Board.OnMore)
            _overseer.Unfold();
        else if (_cardRun is not null || Chosen() is { LogPath: not null })
            OpenSession();
        ShowMessage();
    }

    /// <summary>The selected card's session, or the chosen role's latest, full-screen in place of the lanes.</summary>
    private void OpenSession()
    {
        _sessionFromRoles = _team.HasFocus;
        _sessionFull = true;
        _card.Full = true;
        ShowSelection();
        Arrange();
        _card.SetFocus();
        ShowMessage();
    }

    private void CloseSession()
    {
        _sessionFull = false;
        _card.Full = false;
        Arrange();
        if (_sessionFromRoles && _team.Visible)
            _team.SetFocus();
        else
            _overseer.SetFocus();
        ShowMessage();
    }

    /// <summary>The team's panel for a selected lane heading, or else the selected card's.</summary>
    private void ShowSelection()
    {
        if (_area == Area.Overseer && _overseer.Board.OnHeading)
            ShowTeam();
        else
            ShowCard();
    }

    /// <summary>The selected card's details and its run, in the panel beside the lanes or behind the full-screen log.</summary>
    private void ShowCard()
    {
        if (_overseer.Board.SelectedCard is not { } card)
        {
            _cardRun = null;
            if (_sessionFull)
                CloseSession();
            return;
        }
        var now = _clock.GetUtcNow();
        _cardRun = CardRun.For(_stateRoot, card.Team, _watches.Where(watch => watch.Team == card.Team).Select(watch => watch.Role), card.Number,
            card.LastRun);
        _card.Show(card, OverseerView.Describe(card, _overseer.Board.Cards, _overseer.State), _cardRun,
            CardRun.Says(_cardRun, now, CardHeld()), CardHint());
    }

    /// <summary>The selected heading's team: a line per role and anything stopping it running, and the chosen role's
    /// latest session behind the full-screen log.</summary>
    private void ShowTeam()
    {
        _cardRun = null;
        var team = _overseer.Board.Selected!.Value.Team;
        _team.Show(team,
            [.. _watches.Where(watch => watch.Team == team).Select(watch => (watch.Role, Icons.Field(Icons.For(watch.Status), _drawn), watch.Line))],
            _checks?.Fatal(team) is { } problem ? RoleWatch.Misconfigured(problem) : null);
        _team.ShowHint(TeamHint());
        if (!_sessionFull)
            return;
        if (Chosen() is not { } chosen)
        {
            CloseSession();
            return;
        }
        var name = OverseerView.RoleName(chosen.Role);
        _card.Show($"{team} · {name}", chosen.LogPath, $"{name} · {chosen.Line}");
    }

    /// <summary>The role chosen in the team's panel, while a lane heading is selected.</summary>
    private RoleWatch? Chosen() =>
        _area == Area.Overseer && _overseer.Board.Selected is { Heading: true } heading && _team.Team == heading.Team && _team.Role is { } role
            ? _watches.FirstOrDefault(watch => watch.Team == heading.Team && watch.Role == role)
            : null;

    private void MoveRole(int step)
    {
        _team.MoveRole(step);
        ShowTeam();
        ShowMessage();
    }

    /// <summary>The keys that work on the chosen role, as they're bound now.</summary>
    private string TeamHint()
    {
        List<string> keys = [];
        if (Tabbed)
            keys.Add($"{KeyHint("assistant")} Assistant");
        if (!_team.HasFocus)
            keys.Add($"{KeyHint("pane.next")} choose a role");
        if (Chosen() is { } watch)
        {
            if (watch.LogPath is not null)
                keys.Add($"{KeyHint("overseer.details")} latest log");
            keys.Add($"{KeyHint("agent.hold")} {(watch.Held ? "let it start again" : "pause")}");
            if (watch.Running && !watch.Held)
                keys.Add($"{KeyHint("agent.interrupt")} interrupt");
        }
        return string.Join(" · ", keys);
    }

    /// <summary>The keys that work on the card from the panel, as they're bound now.</summary>
    private string CardHint()
    {
        List<string> keys = [];
        if (Tabbed)
            keys.Add($"{KeyHint("assistant")} Assistant");
        if (_cardRun is not null)
            keys.Add($"{KeyHint("overseer.details")} full log");
        if (_commands.IsEnabled("agent.interrupt"))
            keys.Add($"{KeyHint("agent.interrupt")} {(CardHeld() ? "let it start again" : "interrupt")}");
        if (_overseer.Board.SelectedCard is { Url.Length: > 0 })
            keys.Add($"{KeyHint("overseer.github")} GitHub");
        return string.Join(" · ", keys);
    }

    /// <summary>A letter key as you type it, as the guide writes it.</summary>
    private string KeyHint(string id) => _commands.KeysFor(id, Regions.Overseer) is { Length: 1 } letter ? letter.ToLowerInvariant() : _commands.KeysFor(id, Regions.Overseer);

    private void OpenChip()
    {
        if (_overseer.Board.SelectedCard is { Url.Length: > 0 } card)
            Open(card.Url);
    }

    private void Open(string url)
    {
        Reply(Copying.Said(_openUrl(url)));
    }

    /// <summary>The last read landed, began under five minutes ago, and no run has started or finished since.</summary>
    private bool UpToDate() =>
        _askedAt is { } asked && _readAt >= asked && _clock.GetUtcNow() - asked < ReadEvery
        && _activityRead.SequenceEqual(Activity());

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
            Reply($"couldn't write {document.Path} for your editor: {e.Message}", Severity.Error);
            return;
        }
        _attention.Did("edited");
        _attention.HandOver();
        _handOver(new DocumentHandover(item, document, file, EditorHandover.Command(Environment.GetEnvironmentVariable),
            _work.SelectedCard is null, _work.Items, _readAt, reader));
    }

    /// <summary>Back from a try, the cards as they were with no re-read; from an attach or the editor, Overseer. Either way, what
    /// went wrong if it failed.</summary>
    private void Resume(Handover handover)
    {
        _resume = handover;
        var failure = handover.Failure;
        if (handover is TeamsChanged changed)
            _left = changed.Left;
        if (handover is VisionHandover vision)
            _left = vision.Left;
        if (handover is AttachHandover attached)
            _backTo = (attached.Team, attached.Card, attached.Role, null);
        if (handover is EditorHandover editor)
            _backTo = (editor.Team, editor.Card, editor.Role, editor.Place);
        if (handover is TryHandover tried)
        {
            _attention.TakeBack();
            if (tried.Reader is null)
                _attention.Close();
            _readAt = _askedAt = tried.ReadAt;
            Stamp(tried.ReadAt);
            _work.Show(tried.Items);
            ReadTrend();
            if (tried.Reader is { } place)
            {
                _reopen = (tried.Item, place, failure, null, false);
                failure = null;
            }
        }
        if (handover is DocumentHandover edited)
        {
            _attention.TakeBack();
            _readAt = _askedAt = edited.ReadAt;
            Stamp(edited.ReadAt);
            _work.Show(edited.Items);
            ReadTrend();
            _reopen = (edited.Item, edited.Reader, failure, edited.Said, edited.Committed);
            failure = null;
        }
        if (_backTo is { Number: null } role)
        {
            RefreshWatches(_clock.GetUtcNow());
            BackToCard(role.Team);
        }
        if (failure is { Length: > 0 })
            Reply(failure, Severity.Error);
        ShowMessage();
    }

    /// <summary>Cards handed back from before a restart were all read at once.</summary>
    private void Stamp(DateTimeOffset? readAt)
    {
        if (readAt is not { } at)
            return;
        foreach (var team in _teamNames)
            _teamReadAt[team] = at;
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
        }
    }

    private void OpenSelected()
    {
        if (_work.SelectedUrl is { Length: > 0 } url)
            Open(url);
    }

    /// <summary>The team of the selected column in Work, or of the selected chip or heading in Overseer.</summary>
    internal string? SelectedTeam() => _area == Area.Work ? _work.Team : _overseer.Board.Selected?.Team;

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
        _handOver?.Invoke(new VisionHandover(team, _area, _area == Area.Work ? _work.Place : _left));
    }

    private void OpenBoard()
    {
        if (SelectedTeam() is not { } team)
            return;
        if (_teams.BoardUrl(team) is not { } url)
        {
            Reply($"{team}'s config names no project board", Severity.Error);
            return;
        }
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
            Reply(unread, Severity.Error);
        else if (rank is null && string.IsNullOrWhiteSpace(body.Text))
            Reply($"#{item.Number} has no description", Severity.Error);
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
        Announce($"#{item.Number} approved");
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
            Reply(why, Severity.Error);
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
        Announce($"#{item.Number} declined");
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
        Announce($"#{item.Number} back in the Dev's queue");
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void Shelved(WaitingItem item, int weeks)
    {
        _leaving = null;
        _work.Leave(item);
        Announce($"#{item.Number} back on {ReaderShelve.BackOn(_clock.GetUtcNow(), weeks)}");
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
        Announce(item.Document ? $"merged #{item.Number}" : item.Pitch ? $"accepted #{item.Number}" : $"merged PR #{item.Pr}");
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

    /// <summary>What the board took: the card moves to the column its new Priority puts it in, and the bar says so.</summary>
    private void Ranked(WaitingItem item, Rank rank)
    {
        _work.Ranked(item, rank);
        Announce($"#{item.Number} · set to {rank}");
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Picked up by the next refresh, so a command runs off the draw loop and reports back on it.</summary>
    private void Settle()
    {
        if (_pass.Finished() is { } passed)
            Announce(passed.Text, SeverityOf(passed.Scheme));

        if (_readingBody is { Read.IsCompleted: true } body)
        {
            _readingBody = null;
            _progress = null;
            body.Then(body.Item, body.Read.Result);
        }

        if (_reading is not { } reading)
            return;
        foreach (var (team, read) in reading.Landed())
            SettleTeam(team, read, _readingAsked);
        if (!reading.Done)
            return;
        _reading = null;
        if (!reading.AnyRead)
            return;
        _readAt = _clock.GetUtcNow();
        if (_area == Area.Work && _work.Selected is null)
        {
            if (_left is not { } left || !_work.Focus(left))
                _work.FocusFirstCard();
            _left = null;
        }
        GoToWaiting();
        if (_added is { } added)
        {
            _added = null;
            Announce(added);
        }
        ReadTrend();
    }

    /// <summary>After the reader has closed, so a write it queued that's already done lands on the same refresh.</summary>
    private void SettleWrites()
    {
        var settled = _writes.Settle();
        if (settled.Count == 0)
            return;
        _work.ShowWriting(Writing());
        foreach (var (write, refused) in settled.Where(done => done.Failure is not { Length: > 0 }))
            Settled(write);
        var refusals = settled.Where(done => done.Failure is { Length: > 0 })
            .Select(done => $"{done.Write.Queued}: {done.Failure}").ToList();
        if (refusals.Count > 0)
            Announce(string.Join(" · ", refusals), Severity.Error);
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
                Announce($"vision review asked for · {write.Team}: the Lead starts it now");
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
        if (_reading is not null && !_work.Unread)
            title += " · Reading…";
        if (_title.Text != title)
            _title.Text = title;
    }

    internal string WorkTitle => _title.Text;

    /// <summary>Where you are and when it was read, then what's running or what's selected, under any message.</summary>
    private void ShowMessage()
    {
        var resting =
            _progress is { Length: > 0 } ? _progress
            : _writes.Status() is { Length: > 0 } writing ? writing
            : _pass.Progress is { } passing ? passing
            : _dispatcherOpen ? ""
            : _area == Area.Work && _work.Selected is { } card && (card.Reason.Length > 0 || card.Holding.Count > 0) ? card.Line
            : _area == Area.Work && _work.Region is { } region ? region
            : _area == Area.Overseer && _overseer.Region is { } chip ? chip
            : "";
        var stamp = _dispatcherOpen ? "" : _area switch
        {
            Area.Work => Stamped(_work.Current is { } lane && _teamReadAt.TryGetValue(lane.Team, out var read) ? read : null, _clock.GetUtcNow()),
            _ => Stamped(BoardReadAt(), _clock.GetUtcNow()),
        };
        var place = InAssistant ? "Assistant" : _dispatcherOpen ? "Dispatcher log" : _area switch
        {
            Area.Work => _work.OnlyMine ? MyItems : AllItems,
            _ => "Overseer",
        };
        var focus = string.Join(" · ", new[] { place, stamp }.Where(part => part.Length > 0));
        if (_bar.FocusWord != focus)
            _bar.SetFocusWord(focus);
        if (_bar.RestingText != resting)
            _bar.RestingText = resting;
    }

    /// <summary>When the selected lane's board was last read, or the latest board where none is selected.</summary>
    private DateTimeOffset? BoardReadAt() =>
        _overseer.Board.Selected is { } place && _boardReadAt.TryGetValue(place.Team, out var read) ? read
        : _boardReadAt.Count > 0 ? _boardReadAt.Values.Max() : null;

    private void Reply((string Text, Schemes Scheme) said) => Reply(said.Text, SeverityOf(said.Scheme));

    private StatusMessage Reply(string text, Severity severity = Severity.Info) => _bar.ShowMessage(FirstLine(text), severity);

    private StatusMessage Announce(string text, Severity severity = Severity.Info) => _bar.ShowNews(FirstLine(text), severity);

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";

    private static Severity SeverityOf(Schemes scheme) => scheme == Schemes.Error ? Severity.Error : Severity.Info;

    private void OpenMessages()
    {
        if (App is not { } app)
            return;
        using var dialog = new MessagesDialog(_bar).Routed();
        DialogHost.Run(app, dialog);
    }

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
        _dispatcherOpen = false;
        if (_sessionFull)
        {
            _sessionFull = false;
            _card.Full = false;
        }
        Arrange();
        _menu.Show(area);
        if (area == Area.Work)
        {
            if (!UpToDate())
                ReadWaiting();
            if (_left is not { } left || !_work.Focus(left))
                _work.FocusFirstCard();
        }
        else
        {
            ReadBoards();
            ShowOverseer(_clock.GetUtcNow());
            _overseer.SetFocus();
        }
        ShowAsking();
        ShowMessage();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Claude Code down the right of whichever area is showing, working in the state folder.</summary>
    private AssistantPane NewAssistant(AssistantSetup setup)
    {
        var pane = setup.Pane(_stateRoot, action => App?.Invoke(action), () => App);
        Place(pane);
        pane.Visible = false;
        pane.HasFocusChanged += (_, _) => ShowMessage();
        return pane;
    }

    private int AssistantWidth() => _assistant is { Visible: true } && !Tabbed ? Math.Min(AssistantColumns, Viewport.Width / 2) : 0;

    private int BesideAssistant() => Math.Max(0, Viewport.Width - AssistantWidth() - PanelWidth());

    /// <summary>Whether the window is wide enough for the card's panel beside Overseer's lanes.</summary>
    internal static bool Wide(int width) => width >= WideColumns;

    /// <summary>About two fifths of the window.</summary>
    internal static int PanelWidth(int width) => width * 2 / 5;

    /// <summary>The selection's panel is beside the lanes: Overseer, wide, and no log full-screen.</summary>
    private bool Docked => _area == Area.Overseer && Wide(Viewport.Width) && !_sessionFull && !_dispatcherOpen;

    private int PanelWidth() => Docked ? PanelWidth(Viewport.Width) : 0;

    private bool OnHeading => _area == Area.Overseer && _overseer.Board.OnHeading;

    /// <summary>Rows the team's panel takes at the foot of the lanes, where the window is too narrow for it beside them.</summary>
    private int TeamFoot() =>
        OnHeading && !_sessionFull && !_dispatcherOpen && !Wide(Viewport.Width) ? _team.Rows : 0;

    /// <summary>The selection has a panel of its own, beside the lanes or, for a team, at their foot.</summary>
    private bool PanelShown => Docked || TeamFoot() > 0;

    /// <summary>The card's or the team's panel, whichever the selection has.</summary>
    private View Panel => OnHeading ? _team : _card;

    /// <summary>Puts the selection's panel beside the lanes, in a tab beside the Assistant while that's open too, a
    /// team's at their foot where it's narrow, a session over the lanes full-screen, or nowhere; and the Assistant back
    /// on its own wherever they don't share.</summary>
    private void Arrange()
    {
        var tabbed = Docked && _assistant is { Visible: true };
        var panel = Panel;
        var other = panel == _team ? (View)_card : _team;
        View? focused = _assistant is { HasFocus: true } ? _assistant : _card.HasFocus ? _card : _team.HasFocus ? _team : null;
        var assistantFront = tabbed && (_side.Visible ? _side.Value == _assistantTab : _assistant!.HasFocus || !panel.HasFocus);
        Move(other, this);
        Move(panel, tabbed ? _detailsTab : this);
        if (_assistant is not null)
            Move(_assistant, tabbed ? _assistantTab : this);
        if (_side.Visible != tabbed)
        {
            _side.Visible = tabbed;
            if (tabbed)
                _side.Value = assistantFront ? _assistantTab : _detailsTab;
        }
        if (tabbed)
            Fill(panel);
        if (_card.SuperView == this)
        {
            _card.X = _sessionFull ? 0 : Pos.Func(_ => BesideAssistant(), this);
            _card.Y = MenuLines;
            _card.Width = Dim.Func(_ => _sessionFull ? BesideAssistant() : PanelWidth(), this);
            _card.Height = Dim.Func(_ => Math.Max(0, Viewport.Height - MenuLines - StatusLines), this);
        }
        if (_team.SuperView == this)
        {
            _team.X = Pos.Func(_ => Docked ? BesideAssistant() : 0, this);
            _team.Y = Pos.Func(_ => Docked ? MenuLines : Math.Max(0, Viewport.Height - StatusLines - TeamFoot()), this);
            _team.Width = Dim.Func(_ => Docked ? PanelWidth() : BesideAssistant(), this);
            _team.Height = Dim.Func(_ => Docked ? Math.Max(0, Viewport.Height - MenuLines - StatusLines) : TeamFoot(), this);
        }
        var overseer = _area == Area.Overseer && !_dispatcherOpen;
        _card.Visible = overseer && (_sessionFull || (Docked && !OnHeading));
        _team.Visible = overseer && OnHeading && !_sessionFull;
        _overseer.Visible = overseer && !_sessionFull;
        _overseer.Beside = Docked;
        _work.Visible = _title.Visible = _area == Area.Work && !_dispatcherOpen;
        _dispatchFrame.Visible = _dispatcherOpen;
        if (_assistant is not null)
        {
            if (tabbed)
                Fill(_assistant);
            else
                Place(_assistant);
        }
        if (focused is { HasFocus: false, Visible: true } && focused.SuperView is { Visible: true })
        {
            if (focused == _assistant)
                _assistant!.FocusInput();
            else
                focused.SetFocus();
        }
        SetNeedsLayout();
    }

    private static void Move(View view, View into)
    {
        if (view.SuperView == into)
            return;
        view.SuperView?.Remove(view);
        into.Add(view);
    }

    private static void Fill(View view)
    {
        view.X = 0;
        view.Y = 0;
        view.Width = Dim.Fill();
        view.Height = Dim.Fill();
    }

    private void Place(AssistantPane pane)
    {
        pane.X = Pos.Func(_ => Viewport.Width - AssistantWidth(), this);
        pane.Y = MenuLines;
        pane.Width = Dim.Func(_ => AssistantWidth(), this);
        pane.Height = Dim.Func(_ => Math.Max(0, Viewport.Height - MenuLines - StatusLines), this);
    }

    /// <summary>Ctrl+G I: the card's details or the team's roles in front, with focus in them.</summary>
    private void FocusDetails()
    {
        if (!PanelShown)
            return;
        if (Tabbed)
            _side.Value = _detailsTab;
        Panel.SetFocus();
        ShowSelection();
        ShowMessage();
    }

    private bool InAssistant => _assistant is { Visible: true, HasFocus: true };

    /// <summary>A key the question box would type, which mustn't reach a single-letter command instead.</summary>
    private static bool IsTyping(Key key) => !key.IsCtrl && !key.IsAlt && key.AsRune.Value >= ' ';

    /// <summary>Opens the pane with focus in its question box and the agent starting, or closes it. Sharing the panel
    /// with the card's details, it first comes to the front.</summary>
    private void ToggleAssistant()
    {
        if (_assistant is not { } pane)
            return;
        if (Tabbed && _side.Value != _assistantTab)
        {
            _side.Value = _assistantTab;
            pane.FocusInput();
            ShowSelection();
            ShowMessage();
            return;
        }
        pane.Visible = !pane.Visible;
        Arrange();
        if (pane.Visible)
        {
            if (Tabbed)
                _side.Value = _assistantTab;
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

    /// <summary>Between the area and the panel beside it: the Assistant, or in Overseer whichever of its tabs is in front.</summary>
    private void SwitchPane()
    {
        if (PanelShown && (!Tabbed || _side.Value == _detailsTab))
        {
            if (Panel.HasFocus)
                FocusArea();
            else
                Panel.SetFocus();
            ShowSelection();
        }
        else if (_assistant is not { Visible: true } pane)
            return;
        else if (pane.HasFocus)
            FocusArea();
        else
            pane.FocusInput();
        ShowMessage();
    }

    private void FocusArea()
    {
        if (_dispatcherOpen)
            _dispatchFrame.SetFocus();
        else if (_area == Area.Work)
            _work.SetFocus();
        else if (_sessionFull)
            _card.SetFocus();
        else
            _overseer.SetFocus();
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

    private AskedAbout? AskingAbout() =>
        _dispatcherOpen ? new AskedAbout(null, Dispatcher: true)
        : _area == Area.Work ? _work.Selected is { } item ? new AskedAbout(item.Team, item.Number) : _work.Team is { } team ? new AskedAbout(team) : null
        : _overseer.Board.SelectedCard is { } card ? new AskedAbout(card.Team, card.Number)
        : Chosen() is { } watch ? new AskedAbout(watch.Team, Role: watch.Role)
        : _overseer.Board.Selected is { } chip ? new AskedAbout(chip.Team) : null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _assistant?.Controller.Dispose();
        base.Dispose(disposing);
    }

    private void Copy(Func<CardPanel, LogCopy> copy)
    {
        if (Reader() is not { } pane)
            return;
        var copied = copy(pane);
        Reply(Copying.Said(Copying.Copy(_clipboard ?? App?.Clipboard, copied.Text), truncated: copied.Capped));
    }

    private void CopyBranch()
    {
        if (_work.Selected is not { Branch.Length: > 0 } card)
            return;
        Reply(Copying.Said(Copying.Copy(_clipboard ?? App?.Clipboard, card.Branch), card.Branch));
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

    internal KeySheet Keys => KeysSheet.For(_commands, Offered, Region);

    /// <summary>While the reader is open, only what works from it: the rest acts on the board underneath.</summary>
    private IReadOnlyList<CommandDescriptor> Offered =>
        _shown is null ? _commands.Enabled : [.. _commands.Enabled.Where(command => command.Id is "work.new" or "work.viewImage" or "help" or "commands")];

    /// <summary>Where the window is now, for the keys that work only there.</summary>
    internal CommandScope Region =>
        InAssistant ? Regions.Assistant
        : _dispatcherOpen || _sessionFull ? Regions.Log
        : _area == Area.Work ? Regions.Work
        : _team.HasFocus ? Regions.Team
        : Regions.Overseer;

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
        var (removed, vision) = SettingsDialog.Show(app, _settings, _commands, KeyOverrides, ShowIcons, _auto, _teams, page, _start, newTeam, _showGuide, _checks, _macKeys);
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

    /// <summary>Whether a team now runs other roles than the ones watched.</summary>
    private bool RolesChanged() =>
        _teamNames.Intersect(_teams.Names()).Any(team =>
            !_teams.Roles(team).SequenceEqual(_watches.Where(watch => watch.Team == team).Select(watch => watch.Role)));

    /// <summary>Takes removed teams out of Work and Overseer.</summary>
    internal void Forget(IReadOnlyList<string> teams)
    {
        if (!teams.Any(_teamNames.Contains))
            return;
        _watches.RemoveAll(watch => teams.Contains(watch.Team));
        _teamNames.RemoveAll(teams.Contains);
        _work.Forget(teams);
        _overseer.Forget(teams);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>The vocabulary the cards, the logs and the roles draw their icons from, together, with Auto resolved
    /// here so no view has to know what this terminal answered.</summary>
    private void ShowIcons(IconStyle style)
    {
        var drawn = Icons.Resolve(style, _auto);
        _drawn = drawn;
        _work.ShowIcons(drawn);
        _overseer.ShowIcons(drawn);
        _card.ShowIcons(drawn);
    }

    private void Collapse()
    {
        if (_dispatcherOpen)
            CloseDispatcher();
        else
            CloseSession();
    }

    /// <summary>View › Dispatcher log: the dispatcher's whole log full-screen over whatever area is showing.</summary>
    private void OpenDispatcher()
    {
        _dispatcherOpen = true;
        ShowWholeLog();
        ShowDispatcher(_clock.GetUtcNow());
        Arrange();
        _dispatchFrame.SetFocus();
        ShowMessage();
    }

    private void CloseDispatcher()
    {
        _dispatcherOpen = false;
        Arrange();
        FocusArea();
        ShowMessage();
    }

    /// <summary>Scrolls the session full-screen, or the dispatcher's log while that's open.</summary>
    private void Scroll(Action<CardPanel> session, Action<LogView> log)
    {
        if (_dispatcherOpen)
            log(_dispatchAll);
        else if (_sessionFull)
            session(_card);
    }

    /// <summary>The session whose lines can be selected and copied: the card's or the role's, full-screen.</summary>
    private CardPanel? Reader() => _area == Area.Overseer && _sessionFull && !_dispatcherOpen ? _card : null;

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
