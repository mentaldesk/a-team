using System.Globalization;
using System.Reflection;
using AssistantPane = MentalDesk.Tui.Assistant.AssistantPane;
using MentalDesk.Tui;
using MentalDesk.Tui.Chrome;
using MentalDesk.Tui.Commands;
using MentalDesk.Tui.Keys;
using MentalDesk.Tui.Links;
using MentalDesk.Tui.Shell;
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

public sealed partial class DashboardWindow : Window
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
    private TeamReads? _boarding;
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
    private readonly TeamChecks? _checks;
    private readonly DispatchPass _pass;
    private readonly Func<string, Task<Reading>> _readWaiting;
    private readonly Func<WaitingItem, Task<Reading>> _readBody;
    private readonly Dictionary<string, int> _unticked = [];
    private readonly Func<string, LinkOutcome> _openUrl;
    private readonly Func<WaitingItem, IssueBody, Func<LinkOutcome?>, Func<bool>?, ReaderCommand?, ReaderComment?, ReaderTry?, ReaderEdit?, Key, ReaderIdea?, Func<Key, bool>, Rank?, Rank?> _showBody;
    private readonly Func<WaitingItem, bool> _confirmAccept;
    private readonly Func<WaitingItem, bool> _confirmApprove;
    private readonly Func<WaitingItem, string?> _askHold;
    private readonly Func<WaitingItem, IReadOnlyList<string>, string?> _askFix;
    private readonly Func<RunTask, bool> _confirmStop;
    private readonly Action<string> _showGuide;
    private readonly PaneWidths _paneWidths;
    private readonly DashboardOptions _options;
    private readonly Func<WaitingItem, Task<bool>, Func<string, Task<string?>>, bool> _giveSecret;
    private bool _hintsForSecret;
    private readonly TerminalTitle _terminalTitle;
    private readonly IconStyle _auto;
    private readonly Label _title;
    private readonly TimeProvider _clock;
    private readonly Attention _attention;
    private readonly WhatsNew _news;
    private readonly Opened _opened;
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
    private IssueBody? _shownBody;
    private WaitingItem? _leaving;
    private string? _added;
    private ReaderIdea? _idea;
    private ReaderImage? _image;
    private TeamReads? _reading;
    private readonly Dictionary<string, Synced> _synced = [];
    private readonly Dictionary<string, DateTime> _detailSynced = [];
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
        DashboardOptions? options = null)
    {
        _options = options ?? new DashboardOptions();
        _terminalTitle = _options.TerminalTitle ?? new TerminalTitle(_ => { });
        _paneWidths = _options.PaneWidths ?? new PaneWidths();
        _writes = _options.Writes ?? new WriteQueue(run);
        _confirmQuit = _options.ConfirmQuit ?? ((_, _) => true);
        _limits = settings.ReadLimits();
        _pass = _options.Pass ?? new DispatchPass(stateRoot, "a-team");
        _checks = _options.Checks ?? (_options.Start is null ? null : new TeamChecks(_options.Start.Check, teams.Stamp));
        _showGuide = _options.ShowGuide ?? (_ => { });
        _clock = _options.Clock ?? TimeProvider.System;
        _bar.Clock = _clock;
        _bar.MessagesRequested += (_, _) => _commands.Execute("messages");
        _bar.UnreadErrorsChanged += (_, _) => _terminalTitle.UnseenErrors = _bar.UnreadErrors;
        _commands.Running += () => _bar.SetChord(_commands.PendingChord);
        _attention = _options.Attention ?? new Attention(_clock, _ => { });
        _news = _options.WhatsNew ?? new WhatsNew(_clock.GetUtcNow());
        _opened = new Opened(stateRoot);
        BorderStyle = LineStyle.None;
        _settings = settings;
        _auto = auto;
        _teams = teams;
        _run = run;
        _readWaiting = readWaiting;
        _readBody = readBody;
        _openUrl = openUrl;
        _showBody = showBody;
        _confirmAccept = _options.ConfirmAccept ?? (_ => false);
        _confirmApprove = _options.ConfirmApprove ?? (_ => false);
        _askHold = _options.AskHold ?? (_ => null);
        _askFix = _options.AskFix ?? ((_, _) => null);
        _giveSecret = _options.GiveSecret ?? ((_, _, _) => false);
        _confirmStop = _options.ConfirmStop ?? (_ => false);
        _area = area;
        _dispatchLog = Path.Combine(stateRoot, "dispatch.log");
        _nextPass = Path.Combine(stateRoot, "next-_options.Pass");
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
        if (_options.Assistant is not null)
            Add(_assistant = NewAssistant(_options.Assistant));

        _bar.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - StatusLines), this);
        Add(_bar);

        RegisterCommands();
        _commands.Keys.FocusedScope = () => Region;
        KeyOverrides = settings.KeyOverrides(_commands);
        KeyOverrides.Load();
        _paneWidths.Keys = id => _commands.Keys.For(id).Select(binding => binding.Chord);
        _paneWidths.WindowCommands = (_commands.KeyFor("commands"), OpenCommands);
        SyncQuitKey();
        ShowKeysHint();
        _menu = new AppMenu(_commands, _area, new Dictionary<string, Func<bool>>
        {
            ["view.work"] = () => _area == Area.Work,
            ["view.overseer"] = () => _area == Area.Overseer,
            ["_options.Assistant"] = () => _assistant is { Visible: true },
        });
        _menu.Bar.X = 0;
        _menu.Bar.Y = 0;
        _menu.Bar.SubViewsLaidOut += (_, _) => _alert.SetNeedsLayout();
        Add(_menu.Bar);
        _alert.X = Pos.Func(_ => Math.Max(MenuTitlesEnd() + AlertGap, Viewport.Width - _alert.Shown.GetColumns()), this);
        _alert.Width = Dim.Fill();
        _alert.Chosen += () => _commands.Execute("work.whatsNew");
        Add(_alert);

        if (_options.Resume is not null)
            Resume(_options.Resume);
        if (_options.Resume is null or TeamsChanged or VisionHandover && _area == Area.Work)
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
}
