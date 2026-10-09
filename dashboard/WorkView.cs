using System.Drawing;
using System.Text;
using MentalDesk.Tui.Focus;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard;

/// <summary>Everything waiting on the reviewer: a tab per team, holding what's still to rank and then a column per
/// gate, in the order the work moves through them.</summary>
public sealed class WorkView : View
{
    /// <summary>The columns, and what each holds. Priority decides what gets pitched and approved next, so an
    /// Idea or a pitch that carries none is still to rank; by In review it's decided, and a PR waits for
    /// acceptance whatever its rank. The Customer lead's docs proposal is a PR, so it can't carry one. A question,
    /// the Dev's on a Ready task or the Lead's on a pitch, waits in Questions whatever its rank. Review holds only what Accept would take, and sums up the rest under its
    /// cards. Triage and Pitches wear the kind they hold in their heading, and only a card of another kind wears
    /// its own. Each tab tallies its columns under the Tally icon.</summary>
    internal static readonly (string Name, Icon? Kind, Icon Tally, Func<WaitingItem, bool> Holds, Func<WaitingItem, bool>? Aside)[] Gates =
    [
        ("Triage", Icon.Idea, Icon.Idea, item => item.Priority.Length == 0 && item.Question.Length == 0 && !item.Docs && item.Status is "Idea" or "Pitched", null),
        ("Pitches", Icon.Pitch, Icon.Pitch, item => item.Status == "Pitched" && (item.Priority.Length > 0 || item.Docs) && item.Question.Length == 0, null),
        ("Questions", null, Icon.Question, item => item.Status == "Ready" || item.Status == "Pitched" && item.Question.Length > 0, null),
        ("Review", null, Icon.PullRequest, item => item.Status == "In review" && item.Holdup.Length == 0, item => item.Status == "In review" && item.Holdup.Length > 0),
    ];

    private readonly TeamTabs _tabs = new();
    private readonly List<WorkLane> _lanes = [];
    private IReadOnlyList<WaitingItem> _items = [];
    private WorkLane? _picked;
    private WorkLane? _current;

    public WorkView(IReadOnlyList<string> teams)
    {
        foreach (var team in teams)
        {
            var lane = new WorkLane(team, FocusMoved);
            _lanes.Add(lane);
            _tabs.Add(lane);
        }
        _tabs.ValueChanged += (_, _) => ShowFocus();
        Add(_tabs);
        CanFocus = true;
    }

    internal IReadOnlyList<WorkLane> Lanes => _lanes;

    /// <summary>The team whose tab the keyboard was last in. Not the tab strip's own, which Terminal.Gui moves on by
    /// itself as focus comes and goes.</summary>
    internal WorkLane? Current => _current ?? _lanes.FirstOrDefault();

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

    /// <summary>Whether the selection is a card in Triage, waiting for a rank.</summary>
    internal bool InTriage => SelectedColumn()?.Gate == Gates[0].Name;

    /// <summary>The team whose lane the keyboard is in, or null before any column has had focus.</summary>
    internal string? Team => SelectedColumn()?.Team;

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

    /// <summary>Lays the cards out again, hiding the columns a team has nothing in. A re-read brings each tab's
    /// selected card back with its reason moved on, so it's found again by team and number, in whichever column it's now in.</summary>
    public void Show(IReadOnlyList<WaitingItem> items) => Keep(items, follow: true);

    /// <summary>Drops the tabs and the cards of teams that have been removed.</summary>
    public void Forget(IReadOnlyList<string> teams)
    {
        var current = Current;
        foreach (var lane in _lanes.Where(lane => teams.Contains(lane.Team)).ToList())
        {
            if (_picked == lane)
                _picked = null;
            if (_current == lane)
                _current = null;
            _lanes.Remove(lane);
            _tabs.Remove(lane);
            lane.Dispose();
        }
        _items = [.. _items.Where(item => !teams.Contains(item.Team))];
        Lay(follow: false);
        if (current is not null && _lanes.Contains(current))
            Pick(current);
    }

    /// <summary>Hides everything that isn't the reviewer's move, or brings it all back, staying on the
    /// selected card when the filter still shows it.</summary>
    public void ShowOnlyMine(bool onlyMine)
    {
        if (OnlyMine == onlyMine)
            return;
        OnlyMine = onlyMine;
        var was = Selected;
        Lay(follow: false);
        if (was is null || !Reselect(was))
            FocusFirstCard();
    }

    /// <summary>Draws the cards' icons from the vocabulary in effect.</summary>
    public void ShowIcons(IconStyle style)
    {
        if (Icons == style)
            return;
        Icons = style;
        foreach (var lane in _lanes)
            lane.ShowIcons(style);
    }

    /// <summary>Focus starts on the first card of the tab in front, unless it has none and wasn't picked, when it
    /// goes to the first team with something waiting.</summary>
    public void FocusFirstCard()
    {
        var lane = Current is { Count: > 0 } current || Current == _picked ? Current
            : _lanes.FirstOrDefault(lane => lane.Count > 0) ?? Current;
        if (lane?.Columns.FirstOrDefault(column => column.Count > 0) is { } first)
            first.FocusCards(0);
        else if (lane is not null)
        {
            _current = lane;
            lane.SetFocus();
            _tabs.Value = lane;
        }
        ShowFocus();
    }

    /// <summary>Brings the next team's tab, or the previous one's, to the front, round from the last to the first.</summary>
    internal void MoveTeam(int step)
    {
        if (_lanes.Count == 0)
            return;
        var at = Current is { } current ? _lanes.IndexOf(current) : 0;
        Pick(_lanes[((at + step) % _lanes.Count + _lanes.Count) % _lanes.Count]);
    }

    /// <summary>Brings <paramref name="team"/>'s tab to the front, on the card it was left on.</summary>
    internal bool Pick(string team) =>
        _lanes.FirstOrDefault(lane => lane.Team == team) is { } lane && Pick(lane);

    /// <summary>Brings the tab at <paramref name="index"/> to the front, counting from the first.</summary>
    internal bool PickTab(int index) => index >= 0 && index < _lanes.Count && Pick(_lanes[index]);

    private bool Pick(WorkLane lane)
    {
        if (lane.LastColumn is { Count: > 0 } column)
            column.FocusCards();
        else if (lane.Columns.FirstOrDefault(each => each.Count > 0) is { } first)
            first.FocusCards(0);
        else
            lane.SetFocus();
        // Only once focus has moved: the column it leaves reports itself as current on the way out.
        _picked = _current = lane;
        _tabs.Value = lane;
        ShowFocus();
        FocusChanged?.Invoke();
        return true;
    }

    /// <summary>Left and right step between the columns a tab has cards in, stopping at its edges.</summary>
    internal void MoveColumn(int step)
    {
        if (At() is not { } at)
        {
            FocusFirstCard();
            return;
        }
        var columns = _lanes[at.Lane].Columns;
        for (var gate = at.Gate + step; gate >= 0 && gate < columns.Count; gate += step)
            if (columns[gate].Count > 0)
            {
                Land(at.Lane, gate);
                return;
            }
    }

    /// <summary>Up and down walk a column's rows, stopping at its ends.</summary>
    internal void MoveCard(int step)
    {
        if (At() is not { } at)
        {
            FocusFirstCard();
            return;
        }
        if (_lanes[at.Lane].Columns[at.Gate].MoveSelection(step))
            _lanes[at.Lane].ScrollIntoView(at.Gate);
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

    private void Replace(WaitingItem item, WaitingItem? now) =>
        Keep(now is null ? [.. _items.Where(each => each != item)] : [.. _items.Select(each => each == item ? now : each)],
            follow: false);

    /// <summary>Shows <paramref name="items"/>, each tab keeping its selection, and gives the keyboard back to the
    /// tab in front where it had it.</summary>
    private void Keep(IReadOnlyList<WaitingItem> items, bool follow)
    {
        var focused = At() is { } at ? _lanes[at.Lane] : null;
        var first = Unread;
        _items = items;
        Lay(follow);
        if (first && focused is not null)
            FocusFirstCard();
        if (first || focused is null)
            return;
        if (focused.LastColumn is { Count: > 0 } column)
            column.FocusCards();
        else
            FocusFirstCard();
    }

    private void Lay(bool follow)
    {
        var shown = OnlyMine ? _items.Where(item => item.Mine).ToList() : _items;
        foreach (var lane in _lanes)
            lane.Show(shown, follow);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Gives the keyboard to <paramref name="item"/>'s own row, or its PR's, wherever it's shown.</summary>
    internal bool Focus(WaitingItem item, bool onPr)
    {
        for (var lane = 0; lane < _lanes.Count; lane++)
            for (var gate = 0; gate < _lanes[lane].Columns.Count; gate++)
                if (_lanes[lane].Columns[gate].Select(item, onPr))
                {
                    Land(lane, gate);
                    ShowFocus();
                    return true;
                }
        return false;
    }

    /// <summary>The card or PR row the keyboard is on, by team and number, so it can be found again after a read.</summary>
    internal Place? Place => SelectedColumn()?.Selected is { } row ? new(row.Item.Team, row.Item.Number, row.IsPr) : null;

    /// <summary>Gives the keyboard back to <paramref name="place"/>, or to its card where its PR row has gone.</summary>
    internal bool Focus(Place place) =>
        _items.FirstOrDefault(item => item.Team == place.Team && item.Number == place.Number) is { } item
        && (Focus(item, place.OnPr) || Focus(item, false));

    /// <summary>Puts the selection back on a card, where the column it was in still has it.</summary>
    private bool Reselect(WaitingItem item) =>
        _lanes.SelectMany(lane => lane.Columns).Any(column => column.Select(item));

    private void FocusMoved()
    {
        if (FocusedColumn() is { } column)
        {
            _current = _lanes.First(lane => lane.Columns.Contains(column));
            _current.Remember(column);
        }
        ShowFocus();
        FocusChanged?.Invoke();
    }

    internal void ShowFocus()
    {
        var focused = FocusedColumn();
        var selected = SelectedColumn();
        foreach (var column in _lanes.SelectMany(lane => lane.Columns))
        {
            column.ShowFocus(column == focused);
            column.ShowSelection(column == selected);
        }
    }

    private void Land(int lane, int gate)
    {
        _lanes[lane].Columns[gate].FocusCards();
        _lanes[lane].ScrollIntoView(gate);
    }

    /// <summary>Which tab and which of its columns focus is in.</summary>
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

    /// <summary>The column focus is in, or was last in on the tab in front while something outside the cards,
    /// like the Cards menu, has it.</summary>
    private WorkColumn? SelectedColumn() => FocusedColumn() ?? Current?.LastColumn;

    private WorkColumn? FocusedColumn() =>
        MostFocused is { } view ? _lanes.SelectMany(lane => lane.Columns).FirstOrDefault(column => column.Holds(view)) : null;
}

/// <summary>Where the keyboard was in Work: an item's own row, or its PR's.</summary>
public readonly record struct Place(string Team, int Number, bool OnPr);

/// <summary>The strip of team tabs. The window's commands move between cards and teams, so the arrows Terminal.Gui
/// binds to switching tabs are taken off.</summary>
internal sealed class TeamTabs : Tabs
{
    private static readonly Rune NoHotKey = (Rune)0xffff;

    internal TeamTabs()
    {
        foreach (var key in new[] { Key.CursorUp, Key.CursorDown, Key.CursorLeft, Key.CursorRight })
            KeyBindings.Remove(key);
    }

    // Terminal.Gui 2.5 turns a tab's hotkey off but not its header's, which would hide the '_' in a team's name.
    protected override void OnSubViewAdded(View view)
    {
        base.OnSubViewAdded(view);
        if (view.Border.View is not { } border)
            return;
        foreach (var header in border.SubViews.OfType<ITitleView>().OfType<View>())
            header.HotKeySpecifier = NoHotKey;
        border.SubViewAdded += (_, e) =>
        {
            if (e.SubView is ITitleView)
                e.SubView.HotKeySpecifier = NoHotKey;
        };
    }

    /// <summary>Retitles <paramref name="tab"/>; Terminal.Gui would otherwise draw the new title at the old one's width.</summary>
    internal static void Retitle(View tab, string title)
    {
        if (tab.Title == title)
            return;
        tab.Title = title;
        if (tab.Border.View is not BorderView { TitleView: { } header })
            return;
        header.Text = title;
        header.TextFormatter.ConstrainToSize = null;
        if (header is ITitleView titled)
            titled.MeasuredTabLength = 0;
        tab.SetNeedsLayout();
    }
}

/// <summary>One team's tab: a column per gate that has anything in it, titled with the team and how many cards
/// each column shows.</summary>
public sealed class WorkLane : View
{
    /// <summary>The rows a column spends on its frame.</summary>
    private const int FrameRows = 2;

    private readonly List<WorkColumn> _columns = [];
    private WorkColumn? _wide;
    private IconStyle _icons = IconStyle.Unicode;

    internal WorkLane(string team, Action focusChanged)
    {
        Team = team;
        Title = team;
        CanFocus = true;
        VerticalScrollBar.VisibilityMode = ScrollBarVisibilityMode.Auto;
        for (var i = 0; i < WorkView.Gates.Length; i++)
        {
            var index = i;
            var (name, kind, tally, holds, aside) = WorkView.Gates[i];
            var column = new WorkColumn(team, name, kind, tally, holds, aside, focusChanged)
            {
                X = Pos.Func(_ => Left(index), this),
                Y = 0,
                Width = Dim.Func(_ => ColumnWidth(index), this),
                Height = Dim.Func(_ => Tall(), this),
                Visible = false,
            };
            _columns.Add(column);
            Add(column);
        }
        SubViewLayout += (_, _) => Fit();
    }

    internal string Team { get; }

    internal IReadOnlyList<WorkColumn> Columns => _columns;

    /// <summary>How many cards the tab shows, which is what its title counts.</summary>
    internal int Count => _columns.Sum(column => column.Count);

    /// <summary>How many rows the fullest column needs for its cards, their PRs and its summary, and never none.</summary>
    internal int Rows => Math.Max(1, _columns.Max(column => column.Lines));

    /// <summary>The column the keyboard was last in on this tab, kept while the tab is behind another.</summary>
    internal WorkColumn? LastColumn { get; private set; }

    /// <summary>A team's name, then how many cards wait in each column that has any, after the column's icon.</summary>
    internal static string Heading(string team, IEnumerable<(Icon Tally, int Count)> columns, IconStyle style) =>
        string.Join(" ", [team, .. columns.Where(column => column.Count > 0).Select(column => $"{Icons.Field(column.Tally, style)}{column.Count}")]);

    internal void ShowIcons(IconStyle style)
    {
        _icons = style;
        foreach (var column in _columns)
            column.ShowIcons(style);
        Retitle();
    }

    private void Retitle() =>
        TeamTabs.Retitle(this, Heading(Team, _columns.Select(column => (column.Tally, column.Count)), _icons));

    /// <summary>Lays out the team's cards, keeping the selection on the card it was on, found again by number in
    /// whichever column it's now in when <paramref name="follow"/>, and otherwise on the row it was on.</summary>
    internal void Show(IReadOnlyList<WaitingItem> items, bool follow)
    {
        var column = LastColumn;
        var row = column?.Selected;
        var index = column?.Index ?? 0;
        var team = items.Where(item => item.Team == Team).ToList();
        foreach (var each in _columns)
        {
            each.Show([.. team.Where(each.Holds).OrderByDescending(Priorities.Recommended)], [.. team.Where(each.SetsAside)]);
            each.Visible = each.Lines > 0;
        }
        Retitle();
        SetNeedsLayout();
        if (column is null)
            return;
        var now = row is null ? null : team.FirstOrDefault(item => item.Number == row.Item.Number);
        if (now is not null && (column.Select(now, row!.IsPr) || column.Select(now)))
            return;
        if (follow && now is not null && _columns.FirstOrDefault(each => each.Select(now, row!.IsPr) || each.Select(now)) is { } moved)
            LastColumn = moved;
        else if (column.Count > 0)
            column.SelectRow(index);
        else if (Nearest(_columns.IndexOf(column)) is { } gate)
        {
            LastColumn = _columns[gate];
            LastColumn.SelectRow(0);
        }
        else
            LastColumn = null;
    }

    internal void Remember(WorkColumn column)
    {
        LastColumn = column;
        if (_wide == column)
            return;
        _wide = column;
        SetNeedsLayout();
    }

    /// <summary>The column with cards nearest <paramref name="gate"/>, or null when none has any.</summary>
    internal int? Nearest(int gate) =>
        Enumerable.Range(0, _columns.Count)
            .Where(index => _columns[index].Count > 0)
            .OrderBy(index => Math.Abs(index - gate))
            .Cast<int?>()
            .FirstOrDefault();

    /// <summary>The row the keyboard is on has to be in the part of the tab that's shown.</summary>
    internal void ScrollIntoView(int gate)
    {
        var column = _columns[gate];
        var row = column.Frame.Y + column.Row;
        if (row < Viewport.Y)
            Viewport = Viewport with { Y = row };
        else if (row >= Viewport.Y + Viewport.Height)
            Viewport = Viewport with { Y = row - Viewport.Height + 1 };
    }

    /// <summary>Every column runs the height of the tab, or of the fullest one's cards where they're taller.</summary>
    private int Tall() => Math.Max(Viewport.Height, Rows + FrameRows);

    private void Fit()
    {
        // A second pass: the scroll bar takes a column off the area the first one measured.
        for (var pass = 0; pass < 2 && GetContentSize() != Content(); pass++)
            SetContentSize(Content());
    }

    private Size Content() => Viewport.Size with { Height = Tall() };

    private int Left(int index) => Enumerable.Range(0, index).Sum(ColumnWidth);

    /// <summary>The selected column takes half the tab and the others it shows share the rest.</summary>
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
/// PR hanging under it, and a dimmed line under them summing up what it sets aside.</summary>
public sealed class WorkColumn : FrameView
{
    private readonly Func<WaitingItem, bool> _holds;
    private readonly Func<WaitingItem, bool>? _aside;
    private readonly Cards _cards;
    private readonly Label _summary;
    private readonly FocusBorder _border;
    private IReadOnlyList<WaitingItem> _items = [];
    private IReadOnlyList<WaitingItem> _setAside = [];
    private List<Card> _nodes = [];
    private int _laidOutOver = -1;
    private IconStyle _icons = IconStyle.Unicode;

    internal WorkColumn(
        string team, string gate, Icon? kind, Icon tally, Func<WaitingItem, bool> holds, Func<WaitingItem, bool>? aside,
        Action focusChanged)
    {
        Team = team;
        Gate = gate;
        Kind = kind;
        Tally = tally;
        _holds = holds;
        _aside = aside;
        CanFocus = true;
        _cards = new() { X = 1, Y = 0, Width = Dim.Fill(1), Height = Dim.Func(_ => Nodes), CanFocus = true };
        _summary = new Label
        {
            X = 1, Y = Pos.Func(_ => Nodes), Width = Dim.Fill(1), CanFocus = false, SchemeName = LogSchemes.Dimmed,
        };
        Title = Heading(kind, gate, 0, _icons);
        _border = new FocusBorder(this);
        _cards.TreeBuilder = new DelegateTreeBuilder<Card>(card => card.Children, card => card.Children.Count > 0);
        _cards.AspectGetter = Aspect;
        _cards.DrawLine += (_, line) => Paint(line);
        _cards.HasFocusChanged += (_, _) => focusChanged();
        // Moving within a column changes the tree's selection, not its focus, and the message bar follows both.
        _cards.SelectionChanged += (_, _) => focusChanged();
        Add(_cards, _summary);
        SubViewsLaidOut += (_, _) => Fit();
    }

    internal string Team { get; }

    internal string Gate { get; }

    /// <summary>The kind of card the column's heading wears, or null where its cards each wear their own.</summary>
    internal Icon? Kind { get; }

    /// <summary>The icon its count wears on the team's tab.</summary>
    internal Icon Tally { get; }

    /// <summary>How many items the column holds, which is what its title counts.</summary>
    internal int Count => _items.Count;

    /// <summary>How many rows it draws them in: the items and the PRs under them.</summary>
    internal int Nodes => _nodes.Count;

    /// <summary>How many rows it needs: those, and one more for the summary when it has one.</summary>
    internal int Lines => Nodes + (_setAside.Count > 0 ? 1 : 0);

    /// <summary>The summary line as it's drawn, or nothing when the column sets nothing aside.</summary>
    internal string Summary => _summary.Text;

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

    /// <summary>Whether the column draws its selected row's bar: only the one the keys act on does.</summary>
    internal bool ShowsSelection => _cards.ShowsSelection;

    internal void Show(IReadOnlyList<WaitingItem> items, IReadOnlyList<WaitingItem>? setAside = null)
    {
        _items = items;
        _setAside = setAside ?? [];
        _summary.Visible = _setAside.Count > 0;
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

    internal void ShowSelection(bool shown)
    {
        if (_cards.ShowsSelection == shown)
            return;
        _cards.ShowsSelection = shown;
        _cards.SetNeedsDraw();
    }

    internal bool Holds(View view) => view == _cards || view == this;

    /// <summary>Whether this is the column <paramref name="item"/> belongs in.</summary>
    internal bool Holds(WaitingItem item) => _holds(item);

    /// <summary>Whether <paramref name="item"/> is one this column sums up rather than shows.</summary>
    internal bool SetsAside(WaitingItem item) => _aside?.Invoke(item) ?? false;

    /// <summary>The Review column's summary of the tasks the Dev is still fixing, cut to fit.</summary>
    internal static string Summarise(IReadOnlyList<WaitingItem> items, int width) =>
        items.Count == 0 ? ""
        : Card.Elide($"{items.Count} with the Dev: {string.Join(" · ", items.Select(item => $"#{item.Number} {item.Holdup}"))}", width);

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
        SelectRow(select ?? Index);
        _cards.SetFocus();
    }

    /// <summary>Moves the selection onto a row without taking the keyboard.</summary>
    internal void SelectRow(int select)
    {
        if (_nodes.Count > 0)
            _cards.GoTo(_nodes[Math.Clamp(select, 0, _nodes.Count - 1)]);
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
    /// the PR this view keeps expanded. The Work area moves the selection itself, so the tree is left handling no key
    /// at all. Its expand and collapse symbols are a blank cell rather than hidden, so every row starts in the same
    /// column, PR or no PR. A column that isn't the one the keys act on draws its selected row like any other, keeping
    /// the selection unseen.</summary>
    private sealed class Cards : TreeView<Card>
    {
        internal bool ShowsSelection { get; set; }

        internal Cards()
        {
            MultiSelect = false;
            Style.ShowBranchLines = false;
            Style.CollapseableSymbol = (Rune)' ';
            Style.ExpandableSymbol = (Rune)' ';
            KeyBindings.Clear();
        }

        protected override bool OnKeyDown(Key key) => false;

        protected override bool OnGettingAttributeForRole(in VisualRole role, ref Attribute currentAttribute)
        {
            if (ShowsSelection || role is not (VisualRole.Focus or VisualRole.Active))
                return false;
            currentAttribute = GetAttributeForRole(VisualRole.Normal);
            return true;
        }
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
        // The tree is as tall as its rows, but a GoTo before it was laid out can leave it scrolled past the first.
        if (_cards.ScrollOffsetVertical != 0)
            _cards.ScrollOffsetVertical = 0;
        var width = _cards.Viewport.Width;
        if (_laidOutOver == width)
            return;
        _laidOutOver = width;
        _summary.Text = Summarise(_setAside, width);
        CardText = [.. _nodes.Select(card => card.Text(Room(card), _icons))];
        CardLeads = [.. _nodes.Select(card => card.Leads(_icons))];
        Marks = [.. _nodes.Select(card => card.Mark(Room(card)))];
    }
}
