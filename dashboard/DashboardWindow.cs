using System.Drawing;
using System.Reflection;
using Terminal.Gui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;

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
    private const int DispatchLines = 4;
    private const int MinCellHeight = 5;
    private const string AllItems = "All items";
    private const string MyItems = "My items";
    private readonly List<AgentPane> _panes = [];
    private readonly CommandRegistry _commands = new();
    private readonly int _columns;
    private readonly string _version = Version();
    private readonly AppMenu _menu;
    private readonly Label _stamp;
    private readonly View _agents;
    private readonly FrameView _dispatchFrame;
    private readonly LogView _dispatch;
    private readonly WorkView _work;
    private readonly IReadOnlyList<string> _teamNames;
    private readonly MessageBar _message = new();
    private readonly string _dispatchLog;
    private readonly string _nextPass;
    private readonly DashboardSettings _settings;
    private readonly TeamConfigs _teams;
    private readonly Func<string, string, Task<string?>> _run;
    private readonly Func<string, Task<Reading>> _readWaiting;
    private readonly Action<string> _openUrl;
    private readonly IconStyle _auto;
    private Area _area;
    private Task<string?>? _pending;
    private Task<Reading[]>? _reading;
    private DateTimeOffset? _readAt;
    private string? _failure;
    private string? _progress;
    private int? _expanded;
    private Size _laidOutOver;

    public DashboardWindow(
        IReadOnlyList<(string Team, string Role)> agents,
        string stateRoot,
        DashboardSettings settings,
        TeamConfigs teams,
        Func<string, string, Task<string?>> run,
        Func<string, Task<Reading>> readWaiting,
        Action<string> openUrl,
        Area area,
        IconStyle auto)
    {
        _settings = settings;
        _auto = auto;
        _teams = teams;
        _run = run;
        _readWaiting = readWaiting;
        _openUrl = openUrl;
        _area = area;
        _dispatchLog = Path.Combine(stateRoot, "dispatch.log");
        _nextPass = Path.Combine(stateRoot, "next-pass");
        _teamNames = [.. agents.Select(agent => agent.Team).Distinct()];

        _columns = AgentGrid.Columns(agents);
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
            var index = i;
            var pane = new AgentPane(team, role, Path.Combine(stateRoot, team, role), expandToolCalls)
            {
                X = Pos.Func(_ => Cell(index).X, this),
                Y = Pos.Func(_ => Cell(index).Y, this),
                Width = Dim.Func(_ => Cell(index).Width, this),
                Height = Dim.Func(_ => Cell(index).Height, this),
            };
            _panes.Add(pane);
            _agents.Add(pane);
        }

        _dispatchFrame = new FrameView
        {
            Title = "dispatcher",
            X = 0,
            Y = Pos.Func(_ => Math.Max(0, Viewport.Height - Foot()), this),
            Width = Dim.Fill(),
            Height = DispatchLines + 2,
            CanFocus = false,
            Visible = area == Area.Dashboard,
        };
        _dispatch = new LogView { Width = Dim.Fill(), Height = Dim.Fill(), Elides = true };
        _dispatchFrame.Add(_dispatch);
        Add(_dispatchFrame);

        _work = new WorkView(_teamNames)
        {
            X = 0,
            Y = MenuLines,
            Width = Dim.Fill(),
            Height = Dim.Func(_ => Math.Max(0, Viewport.Height - MenuLines - _message.Lines), this),
            Visible = area == Area.Work,
        };
        _work.FocusChanged += ShowMessage;
        _work.ShowOnlyMine(settings.ReadOnlyMine());
        Add(_work);
        ShowIcons(settings.ReadIcons());

        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);
        Add(_message);

        RegisterCommands();
        _commands.Apply(settings.ReadKeys());
        SyncQuitKey();
        _menu = new AppMenu(_commands);
        _menu.Bar.X = 0;
        _menu.Bar.Y = 0;
        Add(_menu.Bar);
        _stamp = new Label { X = Pos.AnchorEnd(), Y = 0, CanFocus = false };
        Add(_stamp);

        Title = Hints(_version, CurrentMode, _commands);
        if (_area == Area.Work)
            ReadWaiting();
    }

    internal IReadOnlyList<AgentPane> Panes => _panes;

    internal View Dispatcher => _dispatchFrame;

    internal LogView DispatchLog => _dispatch;

    internal View Agents => _agents;

    internal WorkView Work => _work;

    internal MessageBar Message => _message;

    internal Label Stamp => _stamp;

    internal MenuBar Menu => _menu.Bar;

    internal IReadOnlyList<(string Id, MenuItem Item)> MenuItems => _menu.Items;

    internal Area CurrentArea => _area;

    internal int? ExpandedAgent => _expanded;

    internal CommandRegistry Commands => _commands;

    internal static string Hints(string version, Mode mode, CommandRegistry commands) =>
        $"a-team {version} · {commands.Hints(mode)}";

    public void Refresh()
    {
        Settle();
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? nextCheck = long.TryParse(ReadText(_nextPass), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
        var paused = _panes.Select(pane => pane.Team).Distinct().ToDictionary(team => team, _teams.IsPaused);
        foreach (var pane in _panes)
            pane.Refresh(now, nextCheck, paused[pane.Team]);

        var tail = ReadTail(_dispatchLog, DispatchLines);
        if (!_dispatch.Lines.SequenceEqual(tail))
            _dispatch.Lines = tail;

        var stamp = _area == Area.Work ? Stamped(_readAt, now) : "";
        if (_stamp.Text != stamp)
            _stamp.Text = stamp;
        _menu.Refresh();
        ShowMessage();
    }

    /// <summary>When the Work area was last read, for the header.</summary>
    internal static string Stamped(DateTimeOffset? at, DateTimeOffset now) =>
        at is { } read ? $"read {AgentPane.Ago(now - read)} ago " : "";

    /// <summary>While a menu is open it owns the keyboard: its own keys would otherwise run a command as well.</summary>
    protected override bool OnKeyDown(Key key) =>
        (!_menu.Bar.IsOpen() && _commands.Press(key)) || base.OnKeyDown(key);

    private Mode CurrentMode =>
        _area == Area.Work ? Mode.Work : _expanded is null ? Mode.Grid : Mode.Expanded;

    private void RegisterCommands()
    {
        var scroll = new Hint("scroll", Mode.Expanded);
        bool OnDashboard() => _area == Area.Dashboard;
        bool OnWork() => _area == Area.Work;
        bool AnyAgents() => OnDashboard() && _panes.Count > 0;
        bool Selection() => OnDashboard() && Selected() is not null;
        _commands
            .Register("agent.next", "Select the next agent", () => Step(+1), Key.Tab, isEnabled: AnyAgents)
            .Register("agent.previous", "Select the previous agent", () => Step(-1), Key.Tab.WithShift, isEnabled: AnyAgents)
            .Register("agent.right", "Select the agent to the right", () => MoveSelection(0, +1), Key.CursorRight, isEnabled: AnyAgents)
            .Register("agent.left", "Select the agent to the left", () => MoveSelection(0, -1), Key.CursorLeft, isEnabled: AnyAgents)
            .Register("agent.down", "Select the agent below", () => MoveSelection(+1, 0), Key.CursorDown, isEnabled: AnyAgents)
            .Register("agent.up", "Select the agent above", () => MoveSelection(-1, 0), Key.CursorUp, isEnabled: AnyAgents)
            .Register("agent.expand", "Expand the selected agent", () => Expand(), Key.Enter, new Hint("expand", Mode.Grid), Selection)
            .Register("log.pageUp", "Scroll the log up", () => Selected()?.Page(-1), Key.PageUp, scroll, Selection)
            .Register("log.pageDown", "Scroll the log down", () => Selected()?.Page(+1), Key.PageDown, scroll, Selection)
            .Register("log.top", "Jump to the top of the log", () => Selected()?.Home(), Key.Home, isEnabled: Selection)
            .Register("log.bottom", "Jump to the bottom of the log", () => Selected()?.End(), Key.End, isEnabled: Selection)
            .Register("log.toolCalls", "Show tool calls in full", () => Selected()?.ToggleToolCalls(), new Key('t'), isEnabled: Selection)
            .Register("work.right", "Select the column to the right", () => _work.MoveColumn(+1), Key.CursorRight, isEnabled: OnWork)
            .Register("work.left", "Select the column to the left", () => _work.MoveColumn(-1), Key.CursorLeft, isEnabled: OnWork)
            .Register("work.down", "Select the card below", () => _work.MoveCard(+1), Key.CursorDown, isEnabled: OnWork)
            .Register("work.up", "Select the card above", () => _work.MoveCard(-1), Key.CursorUp, isEnabled: OnWork)
            .Register("work.open", "Open the selected issue or PR on GitHub", OpenSelected, Key.Enter, new Hint("open", Mode.Work), () => OnWork() && _work.SelectedUrl is { Length: > 0 })
            .Register("work.mine", "Show only what's your move", ToggleOnlyMine, new Key('m'), new Hint("only mine", Mode.Work), OnWork)
            .Register("work.refresh", "Read what's waiting again", ReadWaiting, new Key('r'), new Hint("refresh", Mode.Work), OnWork)
            .Register("view.dashboard", "Dashboard", () => Show(Area.Dashboard), new Key('d'))
            .Register("view.work", "Work", () => Show(Area.Work), new Key('w'))
            .Register("team.pause", PauseLabel, TogglePause)
            .Register("commands", "Commands", OpenCommands, Key.E.WithCtrl, isEnabled: HasApp)
            .Register("settings", "Settings", OpenSettings, new Key('s'), isEnabled: HasApp)
            .Register("help", "Keys", OpenHelp, Key.F1, isEnabled: HasApp)
            .Register("about", "About", OpenAbout, isEnabled: HasApp)
            .Register("agent.collapse", "Back to the agent grid", () => SetExpanded(null), Key.Esc, new Hint("back", Mode.Expanded), () => OnDashboard() && _expanded is not null)
            .Register("work.back", "Back to the Dashboard", () => Show(Area.Dashboard), Key.Esc, new Hint("dashboard", Mode.Work), OnWork)
            .Register("quit", "Quit", () => App?.RequestStop(), new Key('q'));
    }

    // Point Terminal.Gui's own Quit binding at our quit key: removing it leaves PopoverImpl binding Key.Empty, which throws.
    private void SyncQuitKey()
    {
        var key = _commands.KeyFor("quit");
        if (key != Key.Empty)
            Application.SetDefaultKeyBinding(Command.Quit, Bind.All(key));
    }

    private bool HasApp() => App is not null;

    /// <summary>The team the pause command acts on: the selected agent's, or the first one's.</summary>
    private AgentPane? Target() => Selected() ?? _panes.FirstOrDefault();

    private string PauseLabel() =>
        Target() is { } pane ? $"{(pane.Paused ? "Resume" : "Pause")} {pane.Team}" : "Pause a team";

    private void TogglePause()
    {
        if (_pending is not null || Target() is not { } pane)
            return;
        _progress = pane.Paused ? "Resuming…" : "Pausing…";
        ShowMessage();
        _pending = _run(pane.Paused ? "resume" : "pause", pane.Team);
    }

    /// <summary>Reads every team's gates at once. A second go while one is running is refused, not queued.</summary>
    private void ReadWaiting()
    {
        _reading ??= Task.WhenAll(_teamNames.Select(team => _readWaiting(team)));
        ShowMessage();
    }

    private void ToggleOnlyMine()
    {
        _work.ShowOnlyMine(!_work.OnlyMine);
        _settings.WriteOnlyMine(_work.OnlyMine);
        ShowMessage();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void OpenSelected()
    {
        if (_work.SelectedUrl is { Length: > 0 } url)
            _openUrl(url);
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
        _readAt = DateTimeOffset.UtcNow;
        _work.Show([.. readings!.SelectMany(reading => WaitingItem.Parse(reading.Output))]);
        if (_area == Area.Work && _work.Selected is null)
            _work.FocusFirstCard();
    }

    /// <summary>What went wrong, then what's running, then the region focus is in.</summary>
    private void ShowMessage()
    {
        var (text, scheme) =
            _failure is { Length: > 0 } ? (_failure, Schemes.Error)
            : _reading is not null ? ("Reading…", Schemes.Accent)
            : _progress is { Length: > 0 } ? (_progress, Schemes.Accent)
            : _area == Area.Work && _work.Selected is { Reason.Length: > 0 } card ? (card.Line, Schemes.Base)
            : _area == Area.Work && _work.Region is { } region ? (region, Schemes.Base)
            : ("", Schemes.Base);
        var status = _area == Area.Work ? (_work.OnlyMine ? MyItems : AllItems) : "";
        if (_message.Says == text && _message.Status == status)
            return;
        _message.ShowStatus(status);
        if (text.Length == 0)
            Hush();
        else
            Say(text, scheme);
    }

    private void Say(string message, Schemes scheme)
    {
        _message.Show(message, scheme);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void Hush()
    {
        if (_message.Lines == 0)
            return;
        _message.Clear();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private int Foot() => DispatchLines + 2 + _message.Lines;

    private void Show(Area area)
    {
        if (_area == area)
            return;
        _area = area;
        _settings.WriteArea(area);
        _failure = null;
        _agents.Visible = _dispatchFrame.Visible = area == Area.Dashboard;
        _work.Visible = area == Area.Work;
        if (area == Area.Work)
        {
            ReadWaiting();
            _work.FocusFirstCard();
        }
        else
            _panes.FirstOrDefault()?.SetFocus();
        Title = Hints(_version, CurrentMode, _commands);
        ShowMessage();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void OpenCommands()
    {
        if (App is { } app)
            CommandsDialog.Show(app, _commands);
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

    private void OpenSettings()
    {
        if (App is not { } app)
            return;
        SettingsDialog.Show(app, _settings, _commands, ShowIcons, _auto);
        SyncQuitKey();
        _menu.Refresh();
        Title = Hints(_version, CurrentMode, _commands);
    }

    /// <summary>The vocabulary the panes and the cards draw their icons from, together, with Auto resolved here
    /// so no view has to know what this terminal answered.</summary>
    private void ShowIcons(IconStyle style)
    {
        var drawn = Icons.Resolve(style, _auto);
        _work.ShowIcons(drawn);
        foreach (var pane in _panes)
            pane.ShowIcons(drawn);
    }

    private void Expand()
    {
        var index = SelectedIndex();
        if (index >= 0 && _expanded is null)
            SetExpanded(index);
    }

    private void SetExpanded(int? index)
    {
        _expanded = index;
        for (var i = 0; i < _panes.Count; i++)
            _panes[i].Visible = index is null || index == i;
        Title = Hints(_version, CurrentMode, _commands);
        var selected = SelectedIndex();
        if (index is not null)
            ScrollTo(0);
        else if (selected >= 0)
            ScrollIntoView(selected);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void Step(int step)
    {
        var current = SelectedIndex();
        Select(current < 0
            ? step > 0 ? 0 : _panes.Count - 1
            : (current + step + _panes.Count) % _panes.Count);
    }

    private void Select(int index)
    {
        if (_expanded is not null)
            SetExpanded(index);
        else
            ScrollIntoView(index);
        _panes[index].SetFocus();
    }

    private Rectangle Cell(int index) => _expanded is { } only
        ? only == index ? new Rectangle(Point.Empty, _agents.Viewport.Size) : Rectangle.Empty
        : AgentGrid.Cell(index, _panes.Count, _columns, GridContent());

    /// <summary>The grid tiles a content area tall enough for every cell's floor; the agent area scrolls over it.</summary>
    private Size GridContent()
    {
        var area = _agents.Viewport.Size;
        if (_expanded is not null)
            return area;
        var rows = (_panes.Count + _columns - 1) / _columns;
        return area with { Height = Math.Max(area.Height, rows * MinCellHeight) };
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
        if (_expanded is not null)
            return;
        var current = SelectedIndex();
        if (current < 0)
        {
            _panes[0].SetFocus();
            return;
        }
        var rows = (_panes.Count + _columns - 1) / _columns;
        var row = Math.Clamp(current / _columns + rowStep, 0, rows - 1);
        var column = Math.Clamp(current % _columns + columnStep, 0, _columns - 1);
        Select(Math.Min(row * _columns + column, _panes.Count - 1));
    }

    private int SelectedIndex() => Selected() is { } pane ? _panes.IndexOf(pane) : -1;

    private AgentPane? Selected() => _panes.FirstOrDefault(pane => pane.HasFocus);

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
