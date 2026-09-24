using System.Drawing;
using System.Text;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>Everything waiting on the reviewer: a swimlane per team, holding the Ideas that can't be
/// pitched until they're ranked and then a column per gate, in the order the work moves through them.</summary>
public sealed class WorkView : View
{
    internal static readonly (string Name, string Status)[] Gates =
        [("Ideas", "Idea"), ("Pitches", "Pitched"), ("Review", "In review")];

    private readonly List<WorkLane> _lanes = [];
    private IReadOnlyList<WaitingItem> _items = [];

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

    /// <summary>The item the keyboard is on, whether it's on the item's own row or its PR's, or null when no
    /// column has focus.</summary>
    internal WaitingItem? Selected => FocusedColumn()?.SelectedItem;

    /// <summary>The page Enter opens: the issue's on a card, the PR's on the row under it.</summary>
    internal string? SelectedUrl => FocusedColumn()?.Selected?.Url;

    /// <summary>The region focus is in, for the message bar: the gate and the team.</summary>
    internal string? Region => FocusedColumn() is { } column ? $"{column.Gate} · {column.Team}" : null;

    /// <summary>Whether the cards that aren't the reviewer's move are hidden.</summary>
    internal bool OnlyMine { get; private set; }

    /// <summary>The vocabulary the cards wear their icons from.</summary>
    internal IconStyle Icons { get; private set; } = IconStyle.Unicode;

    /// <summary>Lays the cards out again, keeping the columns a team has even when they're empty.</summary>
    public void Show(IReadOnlyList<WaitingItem> items)
    {
        _items = items;
        Lay();
    }

    /// <summary>Hides everything that isn't the reviewer's move, or brings it all back, staying on the
    /// selected card when the filter still shows it.</summary>
    public void ShowOnlyMine(bool onlyMine)
    {
        if (OnlyMine == onlyMine)
            return;
        OnlyMine = onlyMine;
        var was = Selected;
        Lay();
        if (was is null || !Reselect(was))
            FocusFirstCard();
    }

    /// <summary>Draws the cards' icons from the vocabulary in effect.</summary>
    public void ShowIcons(IconStyle style)
    {
        if (Icons == style)
            return;
        Icons = style;
        foreach (var column in _lanes.SelectMany(lane => lane.Columns))
            column.ShowIcons(style);
    }

    /// <summary>Focus starts on the first card in the first team's first column that has one.</summary>
    public void FocusFirstCard()
    {
        var columns = _lanes.SelectMany(lane => lane.Columns).ToList();
        (columns.Find(column => column.Count > 0) ?? columns.FirstOrDefault())?.FocusCards();
        ShowFocus();
    }

    /// <summary>Left and right step between the columns of a lane, stopping at its edges.</summary>
    internal void MoveColumn(int step)
    {
        if (At() is not { } at)
        {
            FocusFirstCard();
            return;
        }
        Land(at.Lane, Math.Clamp(at.Gate + step, 0, _lanes[at.Lane].Columns.Count - 1), 0);
    }

    /// <summary>Up and down walk a column's rows, then carry on into the same column of the lane above or below.</summary>
    internal void MoveCard(int step)
    {
        if (At() is not { } at)
        {
            FocusFirstCard();
            return;
        }
        if (_lanes[at.Lane].Columns[at.Gate].MoveSelection(step))
        {
            ScrollIntoView(at.Lane, at.Gate);
            return;
        }
        var lane = at.Lane + step;
        if (lane >= 0 && lane < _lanes.Count)
            Land(lane, at.Gate, step);
    }

    private void Lay()
    {
        var shown = OnlyMine ? _items.Where(item => item.Mine).ToList() : _items;
        foreach (var lane in _lanes)
            lane.Show(shown);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Puts the selection back on a card, where the column it was in still has it.</summary>
    private bool Reselect(WaitingItem item) =>
        _lanes.SelectMany(lane => lane.Columns).Any(column => column.Select(item));

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

    /// <summary>Lands on a column: coming from above on its first card, from below on its last.</summary>
    private void Land(int lane, int gate, int step)
    {
        var column = _lanes[lane].Columns[gate];
        column.FocusCards(step switch { > 0 => 0, < 0 => column.Nodes - 1, _ => null });
        ScrollIntoView(lane, gate);
    }

    /// <summary>The row the keyboard is on has to be in the part of the lanes the window shows.</summary>
    private void ScrollIntoView(int lane, int gate)
    {
        var column = _lanes[lane].Columns[gate];
        var row = _lanes[lane].Frame.Y + column.Frame.Y + column.Row;
        if (row < Viewport.Y)
            Viewport = Viewport with { Y = row };
        else if (row >= Viewport.Y + Viewport.Height)
            Viewport = Viewport with { Y = row - Viewport.Height + 1 };
    }

    /// <summary>Which lane and which of its columns focus is in.</summary>
    private (int Lane, int Gate)? At()
    {
        if (FocusedColumn() is not { } focused)
            return null;
        for (var lane = 0; lane < _lanes.Count; lane++)
            for (var gate = 0; gate < _lanes[lane].Columns.Count; gate++)
                if (_lanes[lane].Columns[gate] == focused)
                    return (lane, gate);
        return null;
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

    /// <summary>How many rows the lane gives each column: enough for the fullest one's cards and their PRs, and
    /// never none.</summary>
    internal int Rows => Math.Max(1, _columns.Max(column => column.Nodes));

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

/// <summary>One gate's cards for one team: a frame titled with the count, holding a tree of them, each item's
/// PR hanging under it.</summary>
public sealed class WorkColumn : FrameView
{
    private readonly Cards _cards = new() { X = 1, Y = 0, Width = Dim.Fill(1), Height = Dim.Fill(), CanFocus = true };
    private readonly FocusBorder _border;
    private IReadOnlyList<WaitingItem> _items = [];
    private List<Card> _nodes = [];
    private int _laidOutOver = -1;
    private IconStyle _icons = IconStyle.Unicode;

    internal WorkColumn(string team, string gate, string status, Action focusChanged)
    {
        Team = team;
        Gate = gate;
        Status = status;
        CanFocus = true;
        Title = Heading(gate, 0);
        _border = new FocusBorder(this);
        _cards.TreeBuilder = new DelegateTreeBuilder<Card>(card => card.Children, card => card.Children.Count > 0);
        _cards.AspectGetter = Aspect;
        _cards.DrawLine += (_, line) => Paint(line);
        _cards.HasFocusChanged += (_, _) => focusChanged();
        // Moving within a column changes the tree's selection, not its focus, and the message bar follows both.
        _cards.SelectionChanged += (_, _) => focusChanged();
        Add(_cards);
        SubViewLayout += (_, _) => Fit();
    }

    internal string Team { get; }

    internal string Gate { get; }

    internal string Status { get; }

    /// <summary>How many items the column holds, which is what its title counts.</summary>
    internal int Count => _items.Count;

    /// <summary>How many rows it draws them in: the items and the PRs under them.</summary>
    internal int Nodes => _nodes.Count;

    /// <summary>The text of the rows as the tree draws them, their icons apart.</summary>
    internal IReadOnlyList<string> CardText { get; private set; } = [];

    /// <summary>The icon each of those rows wears.</summary>
    internal IReadOnlyList<TurnIcon> CardIcons { get; private set; } = [];

    /// <summary>The Priority colour each of those rows wears on its number.</summary>
    internal IReadOnlyList<PriorityMark> Marks { get; private set; } = [];

    /// <summary>The row the keyboard is on, or null when the column is empty.</summary>
    internal Card? Selected =>
        _cards.SelectedObject is { } card && _nodes.Contains(card) ? card : null;

    /// <summary>The item that row belongs to, whether it's the item's own row or its PR's.</summary>
    internal WaitingItem? SelectedItem => Selected?.Item;

    internal bool Shown => _border.Focused;

    internal void Show(IReadOnlyList<WaitingItem> items)
    {
        _items = items;
        var roots = Card.Roots(items);
        _nodes = [.. Card.Nodes(roots)];
        Title = Heading(Gate, items.Count);
        _cards.ClearObjects();
        _cards.AddObjects(roots);
        _cards.ExpandAll();
        _laidOutOver = -1;
        Fit();
        SetNeedsLayout();
    }

    internal void ShowIcons(IconStyle style)
    {
        _icons = style;
        _laidOutOver = -1;
        Fit();
        SetNeedsDraw();
    }

    internal void ShowFocus(bool focused) => _border.Show(focused);

    internal bool Holds(View view) => view == _cards || view == this;

    /// <summary>The row the selection is on, inside the frame.</summary>
    internal int Row => Index + 1;

    /// <summary>Moves the selection a row on, or reports that the column has no row that way.</summary>
    internal bool MoveSelection(int step)
    {
        var index = Index + step;
        if (index < 0 || index >= _nodes.Count)
            return false;
        _cards.GoTo(_nodes[index]);
        return true;
    }

    /// <summary>Takes the keyboard, on the row asked for or on the one it was left on.</summary>
    internal void FocusCards(int? select = null)
    {
        if (_nodes.Count > 0)
            _cards.GoTo(_nodes[Math.Clamp(select ?? Index, 0, _nodes.Count - 1)]);
        _cards.SetFocus();
    }

    internal static string Heading(string gate, int count) => $"{gate} · {count}";

    /// <summary>Moves the selection onto <paramref name="item"/>'s own row, where this column is showing it.</summary>
    internal bool Select(WaitingItem item)
    {
        var index = _nodes.FindIndex(card => !card.IsPr && card.Item == item);
        if (index < 0)
            return false;
        _cards.GoTo(_nodes[index]);
        return true;
    }

    /// <summary>Terminal.Gui's own TreeView answers the arrows and the letters itself: left and right would collapse
    /// the PR this view keeps expanded, and down would stop at the last row rather than carry on into the next lane.
    /// The Work area moves the selection itself, so the tree is left handling no key at all. Its expand and collapse
    /// symbols are a blank cell rather than hidden, so every row starts in the same column, PR or no PR.</summary>
    private sealed class Cards : TreeView<Card>
    {
        internal Cards()
        {
            MultiSelect = false;
            Style.ShowBranchLines = false;
            Style.CollapseableSymbol = (Rune)' ';
            Style.ExpandableSymbol = (Rune)' ';
            KeyBindings.Clear();
        }

        protected override bool OnKeyDown(Key key) => false;
    }

    private int Index => Selected is { } card ? _nodes.IndexOf(card) : 0;

    /// <summary>The row as the tree lays it out: the field the icon is painted into, then the card's own text.</summary>
    private string Aspect(Card card) => new string(' ', Icons.Width) + card.Text(Room(card));

    /// <summary>What the card's text is left: the tree spends a cell on its symbol and another on a PR's indent,
    /// and the icon has its field.</summary>
    private int Room(Card card) => _cards.Viewport.Width - Icons.Width - (card.IsPr ? 2 : 1);

    private void Paint(DrawTreeViewLineEventArgs<Card> line)
    {
        if (line is not { Model: { } card, Cells: { } cells })
            return;
        var index = _nodes.IndexOf(card);
        if (index < 0 || index >= CardIcons.Count)
            return;
        CardCells.Paint(cells, line.IndexOfModelText, CardIcons[index], Marks[index]);
    }

    private void Fit()
    {
        var width = _cards.Viewport.Width;
        if (_laidOutOver == width)
            return;
        _laidOutOver = width;
        CardText = [.. _nodes.Select(card => card.Text(Room(card)))];
        CardIcons = [.. _nodes.Select(card => card.Lead(_icons))];
        Marks = [.. _nodes.Select(card => card.Mark(Room(card)))];
    }
}
