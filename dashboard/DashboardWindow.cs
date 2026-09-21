using System.Drawing;
using System.Reflection;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

public sealed class DashboardWindow : Window
{
    private const int DispatchLines = 4;
    private const int MinCellHeight = 5;
    private static readonly Key Settings = new Key(',').WithCtrl;
    private static readonly Key ToolCalls = new('t');
    private readonly List<AgentPane> _panes = [];
    private readonly int _columns;
    private readonly string _version = Version();
    private readonly View _agents;
    private readonly FrameView _dispatchFrame;
    private readonly TextView _dispatch;
    private readonly string _dispatchLog;
    private readonly string _nextPass;
    private readonly DashboardSettings _settings;
    private int? _expanded;
    private Size _laidOutOver;

    public DashboardWindow(IReadOnlyList<(string Team, string Role)> agents, string stateRoot, DashboardSettings settings)
    {
        _settings = settings;
        Title = Hints(_version, expanded: false);
        _dispatchLog = Path.Combine(stateRoot, "dispatch.log");
        _nextPass = Path.Combine(stateRoot, "next-pass");

        _columns = AgentGrid.Columns(agents);
        _agents = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(DispatchLines + 2),
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
            Y = Pos.AnchorEnd(DispatchLines + 2),
            Width = Dim.Fill(),
            Height = DispatchLines + 2,
            CanFocus = false,
        };
        _dispatch = new TextView { Width = Dim.Fill(), Height = Dim.Fill(), ReadOnly = true, CanFocus = false };
        _dispatchFrame.Add(_dispatch);
        Add(_dispatchFrame);
    }

    internal IReadOnlyList<AgentPane> Panes => _panes;

    internal View Dispatcher => _dispatchFrame;

    internal View Agents => _agents;

    internal int? ExpandedAgent => _expanded;

    internal static string Hints(string version, bool expanded) => expanded
        ? $"a-team {version} · Tab: next agent · PgUp/PgDn/Home/End: scroll · t: tool calls · Ctrl+,: settings · Esc: back"
        : $"a-team {version} · Tab/arrows: select agent · Enter: expand · PgUp/PgDn/Home/End: scroll · t: tool calls · Ctrl+,: settings · Esc: quit";

    public void Refresh()
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? nextCheck = long.TryParse(ReadText(_nextPass), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
        foreach (var pane in _panes)
            pane.Refresh(now, nextCheck);

        var tail = ReadTail(_dispatchLog, DispatchLines);
        if (_dispatch.Text != tail)
            _dispatch.Text = tail;
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Settings)
            return OpenSettings();
        if (key == Key.Enter)
            return Expand();
        if (key == Key.Esc)
            return Collapse();
        if (key == Key.Tab)
            return Step(+1);
        if (key == Key.Tab.WithShift)
            return Step(-1);
        if (key == Key.CursorRight)
            return MoveSelection(0, +1);
        if (key == Key.CursorLeft)
            return MoveSelection(0, -1);
        if (key == Key.CursorDown)
            return MoveSelection(+1, 0);
        if (key == Key.CursorUp)
            return MoveSelection(-1, 0);

        var pane = Selected();
        if (pane is null)
            return base.OnKeyDown(key);
        if (key == Key.PageUp)
            pane.Page(-1);
        else if (key == Key.PageDown)
            pane.Page(+1);
        else if (key == Key.Home)
            pane.Home();
        else if (key == Key.End)
            pane.End();
        else if (key == ToolCalls)
            pane.ToggleToolCalls();
        else
            return base.OnKeyDown(key);
        return true;
    }

    private bool OpenSettings()
    {
        if (App is null)
            return false;
        SettingsDialog.Show(App, _settings);
        return true;
    }

    private bool Expand()
    {
        var index = SelectedIndex();
        if (index < 0)
            return false;
        if (_expanded is null)
            SetExpanded(index);
        return true;
    }

    private bool Collapse()
    {
        if (_expanded is null)
            return false;
        SetExpanded(null);
        return true;
    }

    private void SetExpanded(int? index)
    {
        _expanded = index;
        for (var i = 0; i < _panes.Count; i++)
            _panes[i].Visible = index is null || index == i;
        Title = Hints(_version, index is not null);
        var selected = SelectedIndex();
        if (index is not null)
            ScrollTo(0);
        else if (selected >= 0)
            ScrollIntoView(selected);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private bool Step(int step)
    {
        if (_panes.Count == 0)
            return false;
        var current = SelectedIndex();
        Select(current < 0
            ? step > 0 ? 0 : _panes.Count - 1
            : (current + step + _panes.Count) % _panes.Count);
        return true;
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

    private bool MoveSelection(int rowStep, int columnStep)
    {
        if (_panes.Count == 0)
            return false;
        if (_expanded is not null)
            return true;
        var current = SelectedIndex();
        if (current < 0)
        {
            _panes[0].SetFocus();
            return true;
        }
        var rows = (_panes.Count + _columns - 1) / _columns;
        var row = Math.Clamp(current / _columns + rowStep, 0, rows - 1);
        var column = Math.Clamp(current % _columns + columnStep, 0, _columns - 1);
        Select(Math.Min(row * _columns + column, _panes.Count - 1));
        return true;
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
