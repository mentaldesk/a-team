using System.Collections.ObjectModel;
using System.Drawing;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>Everything waiting on the reviewer: a swimlane per team, a column per gate.</summary>
public sealed class WorkView : View
{
    internal static readonly (string Name, string Status)[] Gates = [("Pitches", "Pitched"), ("Review", "In review")];

    private readonly List<WorkLane> _lanes = [];

    public WorkView(IReadOnlyList<string> teams)
    {
        CanFocus = true;
        VerticalScrollBar.VisibilityMode = ScrollBarVisibilityMode.Auto;
        foreach (var team in teams)
        {
            var index = _lanes.Count;
            var lane = new WorkLane(team, FocusMoved)
            {
                X = 0,
                Y = Pos.Func(_ => Top(index), this),
                Width = Dim.Fill(),
            };
            _lanes.Add(lane);
            Add(lane);
        }
        SubViewLayout += (_, _) => Fit();
    }

    internal IReadOnlyList<WorkLane> Lanes => _lanes;

    /// <summary>Raised when the keyboard moves between columns, so the window can name the region it's in.</summary>
    internal event Action? FocusChanged;

    /// <summary>The card the keyboard is on, or null when no column has focus.</summary>
    internal WaitingItem? Selected => FocusedColumn()?.Selected;

    /// <summary>The region focus is in, for the message bar: the gate and the team.</summary>
    internal string? Region => FocusedColumn() is { } column ? $"{column.Gate} · {column.Team}" : null;

    /// <summary>Lays the cards out again, keeping the columns a team has even when they're empty.</summary>
    public void Show(IReadOnlyList<WaitingItem> items)
    {
        foreach (var lane in _lanes)
            lane.Show(items);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Focus starts on the first card in the first team's first column that has one.</summary>
    public void FocusFirstCard()
    {
        var columns = _lanes.SelectMany(lane => lane.Columns).ToList();
        (columns.Find(column => column.Count > 0) ?? columns.FirstOrDefault())?.FocusCards();
        ShowFocus();
    }

    private void FocusMoved()
    {
        ShowFocus();
        FocusChanged?.Invoke();
    }

    internal void ShowFocus()
    {
        var focused = FocusedColumn();
        foreach (var column in _lanes.SelectMany(lane => lane.Columns))
            column.ShowFocus(column == focused);
    }

    private WorkColumn? FocusedColumn() =>
        MostFocused is { } view ? _lanes.SelectMany(lane => lane.Columns).FirstOrDefault(column => column.Holds(view)) : null;

    private int Top(int index) => _lanes.Take(index).Sum(lane => lane.Rows + WorkLane.Chrome);

    private int Total() => _lanes.Sum(lane => lane.Rows + WorkLane.Chrome);

    private void Fit()
    {
        // A second pass: the scroll bar takes a column off the area the first one measured.
        for (var pass = 0; pass < 2 && GetContentSize() != Content(); pass++)
            SetContentSize(Content());
        ShowFocus();
    }

    private Size Content() => Viewport.Size with { Height = Math.Max(Viewport.Height, Total()) };
}

/// <summary>One team's swimlane: the team's name, and a column per gate under it.</summary>
public sealed class WorkLane : View
{
    /// <summary>The rows a lane spends on anything but cards: a blank one, the team's name, and the column's frame.</summary>
    internal const int Chrome = 4;

    private readonly List<WorkColumn> _columns = [];
    private readonly Label _header;
    private int _laidOutOver = -1;

    internal WorkLane(string team, Action focusChanged)
    {
        Team = team;
        CanFocus = true;
        Height = Dim.Func(_ => Rows + Chrome, this);
        _header = new Label { X = 0, Y = 1, Width = Dim.Fill(), CanFocus = false };
        Add(_header);
        for (var i = 0; i < WorkView.Gates.Length; i++)
        {
            var index = i;
            var column = new WorkColumn(team, WorkView.Gates[i].Name, WorkView.Gates[i].Status, focusChanged)
            {
                X = Pos.Func(_ => Split(index), this),
                Y = 2,
                Width = Dim.Func(_ => Split(index + 1) - Split(index), this),
                Height = Dim.Fill(),
            };
            _columns.Add(column);
            Add(column);
        }
        SubViewLayout += (_, _) => Fit();
    }

    internal string Team { get; }

    internal IReadOnlyList<WorkColumn> Columns => _columns;

    /// <summary>How many card rows the lane gives each column: enough for its fullest, and never none.</summary>
    internal int Rows => Math.Max(1, _columns.Max(column => column.Count));

    internal string Header => _header.Text;

    internal void Show(IReadOnlyList<WaitingItem> items)
    {
        foreach (var column in _columns)
            column.Show([.. items.Where(item => item.Team == Team && item.Status == column.Status)]);
    }

    /// <summary>The team's name, then a rule to the right edge.</summary>
    internal static string Rule(string team, int width) =>
        $"{team} {new string('─', Math.Max(0, width - team.Length - 1))}";

    private void Fit()
    {
        if (_laidOutOver == Viewport.Width)
            return;
        _laidOutOver = Viewport.Width;
        _header.Text = Rule(Team, Viewport.Width);
    }

    private int Split(int index) => Viewport.Width * index / _columns.Count;
}

/// <summary>One gate's cards for one team: a frame titled with the count, holding a list of them.</summary>
public sealed class WorkColumn : FrameView
{
    private readonly Cards _cards = new() { X = 1, Y = 0, Width = Dim.Fill(1), Height = Dim.Fill(), CanFocus = true };
    private readonly FocusBorder _border;
    private IReadOnlyList<WaitingItem> _items = [];
    private int _laidOutOver = -1;

    internal WorkColumn(string team, string gate, string status, Action focusChanged)
    {
        Team = team;
        Gate = gate;
        Status = status;
        CanFocus = true;
        Title = Heading(gate, 0);
        _border = new FocusBorder(this);
        _cards.HasFocusChanged += (_, _) => focusChanged();
        Add(_cards);
        SubViewLayout += (_, _) => Fit();
    }

    internal string Team { get; }

    internal string Gate { get; }

    internal string Status { get; }

    internal int Count => _items.Count;

    internal WaitingItem? Selected =>
        _cards.SelectedItem is { } index && index >= 0 && index < _items.Count ? _items[index] : null;

    internal bool Shown => _border.Focused;

    internal void Show(IReadOnlyList<WaitingItem> items)
    {
        _items = items;
        Title = Heading(Gate, items.Count);
        _laidOutOver = -1;
        Fit();
        SetNeedsLayout();
    }

    internal void ShowFocus(bool focused) => _border.Show(focused);

    internal bool Holds(View view) => view == _cards || view == this;

    internal void FocusCards() => _cards.SetFocus();

    internal static string Heading(string gate, int count) => $"{gate} · {count}";

    /// <summary>A card: the issue's number, then as much of its title as the column has room for.</summary>
    internal static string Card(WaitingItem item, int width)
    {
        var text = $"#{item.Number}  {item.Title}";
        if (width <= 0 || text.Length <= width)
            return text;
        return width == 1 ? "…" : string.Concat(text.AsSpan(0, width - 1), "…");
    }

    /// <summary>Terminal.Gui's own ListView jumps to the item a letter starts, which would swallow the app's keys.
    /// Its key bindings — the arrows and the page keys — are invoked separately and still reach it.</summary>
    private sealed class Cards : ListView
    {
        protected override bool OnKeyDown(Key key) => false;
    }

    private void Fit()
    {
        var width = _cards.Viewport.Width;
        if (_laidOutOver == width)
            return;
        _laidOutOver = width;
        var selected = _cards.SelectedItem;
        _cards.SetSource(new ObservableCollection<string>(_items.Select(item => Card(item, width))));
        if (_items.Count > 0)
            _cards.SelectedItem = Math.Clamp(selected ?? 0, 0, _items.Count - 1);
    }
}
