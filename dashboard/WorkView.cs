using System.Drawing;
using System.Text;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>Everything waiting on the reviewer: a swimlane per team, holding what's still to rank and then a
/// column per gate, in the order the work moves through them.</summary>
public sealed class WorkView : View
{
    /// <summary>The columns, and what each holds. Priority decides what gets pitched and approved next, so an
    /// Idea or a pitch that carries none is still to rank; by In review it's decided, and a PR waits for
    /// acceptance whatever its rank. A question, the Dev's on a Ready task or the Lead's on a pitch, waits in
    /// Questions whatever its rank. Triage and Pitches wear the kind they hold in their heading, and only a card
    /// of another kind wears its own.</summary>
    internal static readonly (string Name, Icon? Kind, Func<WaitingItem, bool> Holds)[] Gates =
    [
        ("Triage", Icon.Idea, item => item.Priority.Length == 0 && item.Question.Length == 0 && item.Status is "Idea" or "Pitched"),
        ("Pitches", Icon.Pitch, item => item.Status == "Pitched" && item.Priority.Length > 0 && item.Question.Length == 0),
        ("Questions", null, item => item.Status == "Ready" || item.Status == "Pitched" && item.Question.Length > 0),
        ("Review", null, item => item.Status == "In review"),
    ];

    private readonly List<WorkLane> _lanes = [];
    private WorkColumn? _lastFocused;
    private IReadOnlyList<WaitingItem> _items = [];

    public WorkView(IReadOnlyList<string> teams)
    {
        CanFocus = true;
        VerticalScrollBar.VisibilityMode = ScrollBarVisibilityMode.Auto;
        foreach (var team in teams)
        {
            var lane = new WorkLane(team, FocusMoved) { X = 0, Width = Dim.Fill() };
            lane.Y = Pos.Func(_ => Top(_lanes.IndexOf(lane)), this);
            _lanes.Add(lane);
            Add(lane);
        }
        SubViewLayout += (_, _) => Fit();
    }

    internal IReadOnlyList<WorkLane> Lanes => _lanes;

    /// <summary>Raised when the keyboard moves between columns, so the window can name the region it's in.</summary>
    internal event Action? FocusChanged;

    /// <summary>The item the keyboard is on, whether it's on the item's own row or its PR's, or null before any
    /// column has had focus.</summary>
    internal WaitingItem? Selected => SelectedColumn()?.SelectedItem;

    /// <summary>The page Enter opens: the issue's on a card, the PR's on the row under it.</summary>
    internal string? SelectedUrl => SelectedColumn()?.Selected?.Url;

    /// <summary>The item a rank would be set on: a card's own, and nothing on the PR row under it.</summary>
    internal WaitingItem? SelectedCard =>
        SelectedColumn()?.Selected is { IsPr: false } card ? card.Item : null;

    /// <summary>The region focus is in, for the message bar: the gate and the team.</summary>
    internal string? Region => FocusedColumn() is { } column ? $"{column.Gate} · {column.Team}" : null;

    /// <summary>Every item the last read brought, shown or filtered out.</summary>
    internal IReadOnlyList<WaitingItem> Items => _items;

    /// <summary>Whether no read has landed yet, so there are no cards to look at.</summary>
    internal bool Unread => _items.Count == 0;

    /// <summary>Whether the cards that aren't the reviewer's move are hidden.</summary>
    internal bool OnlyMine { get; private set; }

    /// <summary>The vocabulary the cards wear their icons from.</summary>
    internal IconStyle Icons { get; private set; } = IconStyle.Unicode;

    /// <summary>Lays the cards out again, hiding the columns a team has nothing in.</summary>
    public void Show(IReadOnlyList<WaitingItem> items)
    {
        _items = items;
        Lay();
    }

    /// <summary>Drops the lanes and the cards of teams that have been removed.</summary>
    public void Forget(IReadOnlyList<string> teams)
    {
        foreach (var lane in _lanes.Where(lane => teams.Contains(lane.Team)).ToList())
        {
            if (_lastFocused is not null && lane.Columns.Contains(_lastFocused))
                _lastFocused = null;
            _lanes.Remove(lane);
            Remove(lane);
            lane.Dispose();
        }
        _items = [.. _items.Where(item => !teams.Contains(item.Team))];
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
        _lanes.SelectMany(lane => lane.Columns).FirstOrDefault(column => column.Count > 0)?.FocusCards();
        ShowFocus();
    }

    /// <summary>Left and right step between the columns a lane shows, stopping at its edges.</summary>
    internal void MoveColumn(int step)
    {
        if (At() is not { } at)
        {
            FocusFirstCard();
            return;
        }
        var columns = _lanes[at.Lane].Columns;
        for (var gate = at.Gate + step; gate >= 0 && gate < columns.Count; gate += step)
            if (columns[gate].Visible)
            {
                Land(at.Lane, gate, 0);
                return;
            }
    }

    /// <summary>Up and down walk a column's rows, then carry on into the lane above or below, in the same column
    /// or the nearest one it shows.</summary>
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
        for (var lane = at.Lane + step; lane >= 0 && lane < _lanes.Count; lane += step)
            if (_lanes[lane].Nearest(at.Gate) is { } gate)
            {
                Land(lane, gate, step);
                return;
            }
    }

    /// <summary>What a rank the reviewer has just given leaves on screen, with no re-read: the card is laid out
    /// again where its new Priority puts it, so ranking and clearing move it between Triage and its Status column,
    /// and a ranked Idea — which `waiting` no longer returns — leaves the screen. A card that stays in the column
    /// keeps the selection, wearing the colour its new rank gives its number; one that leaves hands it to the next
    /// card down.</summary>
    internal void Ranked(WaitingItem item, Rank rank) =>
        Replace(item, item with { Priority = rank == Rank.None ? "" : rank.ToString() });

    /// <summary>What an approval leaves on screen, with no re-read: Approved isn't a gate, so the card leaves and
    /// hands the selection to the next card down.</summary>
    internal void Approved(WaitingItem item) => Replace(item, null);

    /// <summary>What a merge leaves on screen, with no re-read: the task is done, so its card leaves.</summary>
    internal void Leave(WaitingItem item) => Replace(item, null);

    private void Replace(WaitingItem item, WaitingItem? now)
    {
        var at = At();
        var row = FocusedColumn()?.Index ?? 0;
        _items = now is null
            ? [.. _items.Where(each => each != item)]
            : [.. _items.Select(each => each == item ? now : each)];
        Lay();
        if (at is not { } was)
            return;
        var column = _lanes[was.Lane].Columns[was.Gate];
        if (now is not null && column.Select(now))
            return;
        if (column.Visible)
            column.FocusCards(row);
        else if (_lanes[was.Lane].Nearest(was.Gate) is { } gate)
            _lanes[was.Lane].Columns[gate].FocusCards(0);
        else
            FocusFirstCard();
    }

    private void Lay()
    {
        var shown = OnlyMine ? _items.Where(item => item.Mine).ToList() : _items;
        foreach (var lane in _lanes)
            lane.Show(shown);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Gives the keyboard to <paramref name="item"/>'s own row, or its PR's, wherever it's shown.</summary>
    internal bool Focus(WaitingItem item, bool onPr)
    {
        if (_lanes.SelectMany(lane => lane.Columns).FirstOrDefault(column => column.Select(item, onPr)) is not { } found)
            return false;
        found.FocusCards();
        ShowFocus();
        return true;
    }

    /// <summary>Puts the selection back on a card, where the column it was in still has it.</summary>
    private bool Reselect(WaitingItem item) =>
        _lanes.SelectMany(lane => lane.Columns).Any(column => column.Select(item));

    private void FocusMoved()
    {
        if (FocusedColumn() is { } column)
        {
            _lastFocused = column;
            _lanes.First(lane => lane.Columns.Contains(column)).Widen(column);
        }
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

    /// <summary>The column focus is in, or was last in while something outside the cards, like the Cards menu, has it.</summary>
    private WorkColumn? SelectedColumn() => FocusedColumn() ?? _lastFocused;

    private WorkColumn? FocusedColumn() =>
        MostFocused is { } view ? _lanes.SelectMany(lane => lane.Columns).FirstOrDefault(column => column.Holds(view)) : null;

    private int Top(int index) => _lanes.Take(index).Sum(lane => lane.Lines);

    private int Total() => _lanes.Sum(lane => lane.Lines);

    private void Fit()
    {
        // A second pass: the scroll bar takes a column off the area the first one measured.
        for (var pass = 0; pass < 2 && GetContentSize() != Content(); pass++)
            SetContentSize(Content());
        ShowFocus();
    }

    private Size Content() => Viewport.Size with { Height = Math.Max(Viewport.Height, Total()) };
}

/// <summary>One team's swimlane: the team's name, and a column per gate under it that has anything in it.</summary>
public sealed class WorkLane : View
{
    /// <summary>The rows a lane spends on anything but cards: a blank one, the team's name, and the column's frame.</summary>
    internal const int Chrome = 4;

    private readonly List<WorkColumn> _columns = [];
    private readonly Label _header;
    private WorkColumn? _wide;
    private int _laidOutOver = -1;

    internal WorkLane(string team, Action focusChanged)
    {
        Team = team;
        CanFocus = true;
        Height = Dim.Func(_ => Lines, this);
        _header = new Label { X = 0, Y = 1, Width = Dim.Fill(), CanFocus = false };
        Add(_header);
        for (var i = 0; i < WorkView.Gates.Length; i++)
        {
            var index = i;
            var (name, kind, holds) = WorkView.Gates[i];
            var column = new WorkColumn(team, name, kind, holds, focusChanged)
            {
                X = Pos.Func(_ => Left(index), this),
                Y = 2,
                Width = Dim.Func(_ => ColumnWidth(index), this),
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

    /// <summary>How tall the lane is: just its name when it shows no column.</summary>
    internal int Lines => _columns.Any(column => column.Visible) ? Rows + Chrome : Chrome - 2;

    internal string Header => _header.Text;

    internal void Show(IReadOnlyList<WaitingItem> items)
    {
        foreach (var column in _columns)
        {
            column.Show([.. items.Where(item => item.Team == Team && column.Holds(item))]);
            column.Visible = column.Count > 0;
        }
        SetNeedsLayout();
    }

    /// <summary>Gives <paramref name="column"/> the lane's wide share.</summary>
    internal void Widen(WorkColumn column)
    {
        if (_wide == column)
            return;
        _wide = column;
        SetNeedsLayout();
    }

    /// <summary>The column the lane shows nearest <paramref name="gate"/>, or null when it shows none.</summary>
    internal int? Nearest(int gate) =>
        Enumerable.Range(0, _columns.Count)
            .Where(index => _columns[index].Visible)
            .OrderBy(index => Math.Abs(index - gate))
            .Cast<int?>()
            .FirstOrDefault();

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

    private int Left(int index) => Enumerable.Range(0, index).Sum(ColumnWidth);

    /// <summary>The selected column takes half the lane and the others it shows share the rest.</summary>
    private int ColumnWidth(int index)
    {
        var column = _columns[index];
        if (!column.Visible)
            return 0;
        var shown = _columns.Where(each => each.Visible).ToList();
        var wide = shown.Count > 1 && _wide is { Visible: true } ? _wide : null;
        var half = Viewport.Width / 2;
        if (column == wide)
            return half;
        var rest = shown.Where(each => each != wide).ToList();
        var room = wide is null ? Viewport.Width : Viewport.Width - half;
        var at = rest.IndexOf(column);
        return room * (at + 1) / rest.Count - room * at / rest.Count;
    }
}

/// <summary>One column's cards for one team: a frame titled with the count, holding a tree of them, each item's
/// PR hanging under it.</summary>
public sealed class WorkColumn : FrameView
{
    private readonly Func<WaitingItem, bool> _holds;
    private readonly Cards _cards = new() { X = 1, Y = 0, Width = Dim.Fill(1), Height = Dim.Fill(), CanFocus = true };
    private readonly FocusBorder _border;
    private IReadOnlyList<WaitingItem> _items = [];
    private List<Card> _nodes = [];
    private int _laidOutOver = -1;
    private IconStyle _icons = IconStyle.Unicode;

    internal WorkColumn(string team, string gate, Icon? kind, Func<WaitingItem, bool> holds, Action focusChanged)
    {
        Team = team;
        Gate = gate;
        Kind = kind;
        _holds = holds;
        CanFocus = true;
        Title = Heading(kind, gate, 0, _icons);
        _border = new FocusBorder(this);
        _cards.TreeBuilder = new DelegateTreeBuilder<Card>(card => card.Children, card => card.Children.Count > 0);
        _cards.AspectGetter = Aspect;
        _cards.DrawLine += (_, line) => Paint(line);
        _cards.HasFocusChanged += (_, _) => focusChanged();
        // Moving within a column changes the tree's selection, not its focus, and the message bar follows both.
        _cards.SelectionChanged += (_, _) => focusChanged();
        Add(_cards);
        SubViewsLaidOut += (_, _) => Fit();
    }

    internal string Team { get; }

    internal string Gate { get; }

    /// <summary>The kind of card the column's heading wears, or null where its cards each wear their own.</summary>
    internal Icon? Kind { get; }

    /// <summary>How many items the column holds, which is what its title counts.</summary>
    internal int Count => _items.Count;

    /// <summary>How many rows it draws them in: the items and the PRs under them.</summary>
    internal int Nodes => _nodes.Count;

    /// <summary>The text of the rows as the tree draws them, their icons apart.</summary>
    internal IReadOnlyList<string> CardText { get; private set; } = [];

    /// <summary>Every icon each of those rows wears, a card's kind after its turn.</summary>
    internal IReadOnlyList<IReadOnlyList<TurnIcon>> CardLeads { get; private set; } = [];

    /// <summary>The first of them: whose move it is, or the line a PR hangs from.</summary>
    internal IReadOnlyList<TurnIcon> CardIcons => [.. CardLeads.Select(leads => leads[0])];

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
        var roots = Card.Roots(items, Kind);
        _nodes = [.. Card.Nodes(roots)];
        Title = Heading(Kind, Gate, items.Count, _icons);
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
        Title = Heading(Kind, Gate, Count, style);
        _laidOutOver = -1;
        Fit();
        SetNeedsDraw();
    }

    internal void ShowFocus(bool focused) => _border.Show(focused);

    internal bool Holds(View view) => view == _cards || view == this;

    /// <summary>Whether this is the column <paramref name="item"/> belongs in.</summary>
    internal bool Holds(WaitingItem item) => _holds(item);

    /// <summary>The row the selection is on, inside the frame.</summary>
    internal int Row => Index + 1;

    /// <summary>Which of the column's rows that is, for a caller that wants to land on it again.</summary>
    internal int Index => Selected is { } card ? _nodes.IndexOf(card) : 0;

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

    internal static string Heading(Icon? kind, string gate, int count, IconStyle style) =>
        $"{(kind is { } icon ? Icons.Field(icon, style) : "")}{gate} · {count}";

    /// <summary>Moves the selection onto <paramref name="item"/>'s own row, or its PR's, where this column is
    /// showing it.</summary>
    internal bool Select(WaitingItem item, bool onPr = false)
    {
        var index = _nodes.FindIndex(card => card.IsPr == onPr && card.Item == item);
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

    /// <summary>The row as the tree lays it out: the fields the icons are painted into, then the card's own text.</summary>
    private string Aspect(Card card) => new string(' ', card.LeadWidth) + CardCells.LaidOut(card.Text(Room(card), _icons));

    /// <summary>What the card's text is left: the tree spends a cell on its symbol and another on a PR's indent,
    /// and the icons have their fields.</summary>
    private int Room(Card card) => _cards.Viewport.Width - card.LeadWidth - (card.IsPr ? 2 : 1);

    private void Paint(DrawTreeViewLineEventArgs<Card> line)
    {
        if (line is not { Model: { } card, Cells: { } cells })
            return;
        var index = _nodes.IndexOf(card);
        if (index < 0 || index >= CardLeads.Count)
            return;
        CardCells.Paint(cells, line.IndexOfModelText, CardText[index], CardLeads[index], Marks[index]);
    }

    private void Fit()
    {
        var width = _cards.Viewport.Width;
        if (_laidOutOver == width)
            return;
        _laidOutOver = width;
        CardText = [.. _nodes.Select(card => card.Text(Room(card), _icons))];
        CardLeads = [.. _nodes.Select(card => card.Leads(_icons))];
        Marks = [.. _nodes.Select(card => card.Mark(Room(card)))];
    }
}
