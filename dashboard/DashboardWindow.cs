using System.Drawing;
using System.Reflection;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

public sealed class DashboardWindow : Window
{
    private const int DispatchLines = 4;
    private const int MinCellHeight = 5;
    private readonly List<AgentPane> _panes = [];
    private readonly CommandRegistry _commands = new();
    private readonly int _columns;
    private readonly string _version = Version();
    private readonly View _agents;
    private readonly FrameView _dispatchFrame;
    private readonly TextView _dispatch;
    private readonly MessageBar _message = new();
    private readonly string _dispatchLog;
    private readonly string _nextPass;
    private readonly DashboardSettings _settings;
    private readonly TeamConfigs _teams;
    private readonly Func<string, string, Task<string?>> _run;
    private Task<string?>? _pending;
    private int? _expanded;
    private Size _laidOutOver;

    public DashboardWindow(
        IReadOnlyList<(string Team, string Role)> agents,
        string stateRoot,
        DashboardSettings settings,
        TeamConfigs teams,
        Func<string, string, Task<string?>> run)
    {
        _settings = settings;
        _teams = teams;
        _run = run;
        _dispatchLog = Path.Combine(stateRoot, "dispatch.log");
        _nextPass = Path.Combine(stateRoot, "next-pass");

        _columns = AgentGrid.Columns(agents);
        _agents = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Func(_ => Math.Max(0, Viewport.Height - Foot()), this),
            CanFocus = true,
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
        };
        _dispatch = new TextView { Width = Dim.Fill(), Height = Dim.Fill(), ReadOnly = true, CanFocus = false };
        _dispatchFrame.Add(_dispatch);
        Add(_dispatchFrame);

        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);
        Add(_message);

        RegisterCommands();
        Title = Hints(_version, expanded: false, _commands);
    }

    internal IReadOnlyList<AgentPane> Panes => _panes;

    internal View Dispatcher => _dispatchFrame;

    internal View Agents => _agents;

    internal MessageBar Message => _message;

    internal int? ExpandedAgent => _expanded;

    internal CommandRegistry Commands => _commands;

    internal static string Hints(string version, bool expanded, CommandRegistry commands) =>
        $"a-team {version} · {commands.Hints(expanded ? Mode.Expanded : Mode.Grid)}";

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
        if (_dispatch.Text != tail)
            _dispatch.Text = tail;
    }

    protected override bool OnKeyDown(Key key) => _commands.Press(key) || base.OnKeyDown(key);

    private void RegisterCommands()
    {
        var scroll = new Hint("PgUp/PgDn", "scroll", Mode.Expanded);
        bool AnyAgents() => _panes.Count > 0;
        bool Selection() => Selected() is not null;
        _commands
            .Register("agent.next", "Select the next agent", () => Step(+1), Key.Tab, isEnabled: AnyAgents)
            .Register("agent.previous", "Select the previous agent", () => Step(-1), Key.Tab.WithShift, isEnabled: AnyAgents)
            .Register("agent.right", "Select the agent to the right", () => MoveSelection(0, +1), Key.CursorRight, isEnabled: AnyAgents)
            .Register("agent.left", "Select the agent to the left", () => MoveSelection(0, -1), Key.CursorLeft, isEnabled: AnyAgents)
            .Register("agent.down", "Select the agent below", () => MoveSelection(+1, 0), Key.CursorDown, isEnabled: AnyAgents)
            .Register("agent.up", "Select the agent above", () => MoveSelection(-1, 0), Key.CursorUp, isEnabled: AnyAgents)
            .Register("agent.expand", "Expand the selected agent", () => Expand(), Key.Enter, new Hint("Enter", "expand", Mode.Grid), Selection)
            .Register("log.pageUp", "Scroll the log up", () => Selected()?.Page(-1), Key.PageUp, scroll, Selection)
            .Register("log.pageDown", "Scroll the log down", () => Selected()?.Page(+1), Key.PageDown, scroll, Selection)
            .Register("log.top", "Jump to the top of the log", () => Selected()?.Home(), Key.Home, isEnabled: Selection)
            .Register("log.bottom", "Jump to the bottom of the log", () => Selected()?.End(), Key.End, isEnabled: Selection)
            .Register("log.toolCalls", "Show tool calls in full", () => Selected()?.ToggleToolCalls(), new Key('t'), isEnabled: Selection)
            .Register("team.pause", PauseLabel, TogglePause)
            .Register("commands", "Commands", OpenCommands, Key.E.WithCtrl, new Hint("Ctrl+E", "commands", Mode.Grid), HasApp)
            .Register("settings", "Settings", OpenSettings, new Key('s'), isEnabled: HasApp)
            .Register("help", "Help", OpenHelp, new Key('?'), new Hint("?", "help"), HasApp)
            .Register("agent.collapse", "Back to the agent grid", () => SetExpanded(null), Key.Esc, new Hint("Esc", "back", Mode.Expanded), () => _expanded is not null)
            .Register("quit", "Quit", () => App?.RequestStop(), hint: new Hint("Esc", "quit", Mode.Grid));
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
        Say(pane.Paused ? "Resuming…" : "Pausing…", Schemes.Accent);
        _pending = _run(pane.Paused ? "resume" : "pause", pane.Team);
    }

    /// <summary>Picked up by the next refresh, so a command runs off the draw loop and reports back on it.</summary>
    private void Settle()
    {
        if (_pending is not { IsCompleted: true } finished)
            return;
        _pending = null;
        var failure = finished.Status == TaskStatus.RanToCompletion
            ? finished.Result
            : finished.Exception?.GetBaseException().Message ?? "the command didn't finish";
        if (failure is { Length: > 0 })
            Say(failure, Schemes.Error);
        else
            Hush();
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

    private void OpenSettings()
    {
        if (App is { } app)
            SettingsDialog.Show(app, _settings);
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
        Title = Hints(_version, index is not null, _commands);
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

    private static string ReadTail(string path, int count)
    {
        try
        {
            return string.Join('\n', File.ReadLines(path).TakeLast(count));
        }
        catch (IOException) { return "(the dispatcher hasn't run yet)"; }
    }
}
