using System.Diagnostics;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard;

/// <summary>What Overseer needs from the rest of the app besides the cards: the clock, the limits, which cards an
/// agent is on, which roles are busy, and why a card is waiting where Work knows.</summary>
public sealed record OverseerState(
    DateTimeOffset Now,
    IReadOnlyDictionary<string, string> Limits,
    IReadOnlyDictionary<(string Team, int Number), string> Worked,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Busy,
    Func<string, int, WaitingItem?> Waiting)
{
    public static readonly OverseerState Empty = new(
        DateTimeOffset.MinValue, new Dictionary<string, string>(), new Dictionary<(string, int), string>(),
        new Dictionary<string, IReadOnlyList<string>>(), (_, _) => null);
}

/// <summary>Where a busy role's header frame goes in its lane's title, in columns from the title's start.</summary>
public readonly record struct HeaderSlot(int Column, string Role);

/// <summary>A lane's title, and the cells in it the busy roles' animations play in.</summary>
public sealed record LaneHeading(string Title, IReadOnlyList<HeaderSlot> Slots);

/// <summary>Every team's board on one screen: a framed lane per team under one row of column headings, and the
/// selected card's details at the foot when they're open.</summary>
public sealed class OverseerView : View
{
    private const int HeaderRows = 1;
    private const int FrameRows = 2;
    private const int MaxDetailRows = 8;
    private const string Separator = " · ";
    private const int NumberWidth = 5;
    private const int AgeWidth = 3;

    private readonly OverseerBoard _board;
    private readonly Label _header = new() { X = 1, Y = 0, Width = Dim.Fill(), CanFocus = false };
    private readonly View _lanes;
    private readonly List<Lane> _laneViews = [];
    private readonly FrameView _details;
    private readonly Label _detailText = new() { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = false };
    private OverseerState _state = OverseerState.Empty;
    private readonly long _started = Stopwatch.GetTimestamp();
    private string _steps = "";
    private IconStyle _icons = IconStyle.Unicode;

    public OverseerView(IReadOnlyList<string> teams)
    {
        CanFocus = true;
        _board = new OverseerBoard(teams);
        _lanes = new View
        {
            X = 0,
            Y = HeaderRows,
            Width = Dim.Fill(),
            Height = Dim.Func(_ => Math.Max(0, Viewport.Height - HeaderRows - DetailHeight()), this),
            CanFocus = false,
        };
        _lanes.VerticalScrollBar.VisibilityMode = ScrollBarVisibilityMode.Auto;
        _lanes.SubViewLayout += (_, _) =>
        {
            _board.FitTo(_lanes.Viewport.Height, FrameRows);
            FitLanes();
        };
        _details = new FrameView
        {
            X = 0,
            Y = Pos.Func(_ => Math.Max(0, Viewport.Height - DetailHeight()), this),
            Width = Dim.Fill(),
            Height = Dim.Func(_ => DetailHeight(), this),
            CanFocus = false,
            Visible = false,
        };
        _details.Add(_detailText);
        Add(_header, _lanes, _details);
        foreach (var team in teams)
            AddLane(team);
        SubViewLayout += (_, _) => ShowHeader();
    }

    internal OverseerBoard Board => _board;

    public void ShowIcons(IconStyle style)
    {
        _icons = style;
        SetNeedsDraw();
    }

    internal bool DetailsShown => _details.Visible;

    internal string Details => _detailText.Text;

    internal string DetailsTitle => _details.Title;

    internal int ScrolledTo => _lanes.Viewport.Y;

    internal IReadOnlyList<string> LaneTitles => [.. _laneViews.Select(lane => lane.Title)];

    /// <summary>Whether no read has landed yet, so there are no cards to look at.</summary>
    internal bool Unread { get; private set; } = true;

    /// <summary>The team and column the selection is in, for the status bar.</summary>
    internal string? Region => _board.Selected is { } place ? $"{OverseerBoard.Columns[place.Column]} · {place.Team}" : null;

    public void Show(IReadOnlyList<BoardCard> cards)
    {
        Unread = false;
        _board.Show(cards);
        if (_board.Selected is null)
            _board.SelectFirst();
        Changed();
    }

    public void Forget(IReadOnlyList<string> teams)
    {
        foreach (var lane in _laneViews.Where(lane => teams.Contains(LaneTeam(lane))).ToList())
        {
            _laneViews.Remove(lane);
            _lanes.Remove(lane);
            lane.Dispose();
        }
        _board.Forget(teams);
        Changed();
    }

    /// <summary>Takes what the rest of the app knows now: the time, the limits, and what the agents are on.</summary>
    public void Show(OverseerState state)
    {
        _state = state;
        foreach (var lane in _laneViews)
        {
            var team = LaneTeam(lane);
            var heading = Heading(team, state.Busy.TryGetValue(team, out var busy) ? busy : [], Over(team));
            lane.Slots = heading.Slots;
            if (lane.Title != heading.Title)
                lane.Title = heading.Title;
        }
        Redraw();
        ShowDetails();
    }

    /// <summary>Redraws the animations of the busy roles, once one of them has moved on a frame.</summary>
    public void Spin()
    {
        var elapsed = Elapsed;
        var steps = string.Join(' ', _state.Busy.Values.SelectMany(roles => roles).Concat(_state.Worked.Values).Distinct()
            .Select(role => RoleAnimation.For(role)?.Step(elapsed)));
        if (steps == _steps)
            return;
        _steps = steps;
        Redraw();
    }

    /// <summary>Every frame of every role's animation lasts a multiple of this.</summary>
    internal static TimeSpan SpinEvery => TimeSpan.FromMilliseconds(20);

    private TimeSpan Elapsed => Stopwatch.GetElapsedTime(_started);

    public void MoveColumn(int step) => Move(() => _board.MoveColumn(step));

    public void MoveRow(int step) => Move(() => _board.MoveRow(step));

    public void MoveLane(int step) => Move(() => _board.MoveLane(step));

    /// <summary>PgUp and PgDn: scrolls the lanes a screen, selecting in the first lane with cards from the top, or at
    /// the bottom already, the last.</summary>
    public void Page(int step)
    {
        var height = _lanes.Viewport.Height;
        var y = Math.Clamp(_lanes.Viewport.Y + step * height, 0, Math.Max(0, Tall() - height));
        var column = _board.Selected?.Column ?? 0;
        var lanes = step > 0 && y == _lanes.Viewport.Y ? Enumerable.Reverse(_laneViews) : _laneViews.Where(lane => Top(lane) >= y);
        if (lanes.FirstOrDefault(lane => _board.SelectLane(LaneTeam(lane), column)) is null)
            return;
        _lanes.Viewport = _lanes.Viewport with { Y = y };
        Changed();
        ScrollToSelection();
    }

    /// <summary>Enter: unfolds a lane on its <c>+N</c>, and otherwise opens or closes the details.</summary>
    public void Enter()
    {
        if (_board.OnMore)
        {
            Move(_board.Unfold);
            return;
        }
        if (_board.SelectedCard is null)
            return;
        _details.Visible = !_details.Visible;
        ShowDetails();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Esc: folds the selected lane, if it's unfolded.</summary>
    public bool Fold()
    {
        if (!_board.Fold())
            return false;
        Changed();
        return true;
    }

    internal bool CanFold => _board.Selected is { } place && _board.Unfolded(place.Team);

    /// <summary>A lane's title: the team, its busy roles or <c>idle</c>, and how many of its cards are past their limit.
    /// A busy role with an animation keeps a cell after its name, which the lane paints its header frame into.</summary>
    internal static LaneHeading Heading(string team, IReadOnlyList<string> busy, int over)
    {
        var title = team;
        List<HeaderSlot> slots = [];
        foreach (var role in busy)
        {
            title += Separator + RoleName(role);
            if (RoleAnimation.For(role) is null)
                continue;
            title += " ";
            slots.Add(new(title.GetColumns(), role));
            title += " ";
        }
        if (busy.Count == 0)
            title += Separator + "idle";
        if (over > 0)
            title += $"{Separator}{over} over";
        return new(title, slots);
    }

    internal static string RoleName(string role) => role switch
    {
        "lead" => "Lead",
        "dev" => "Dev",
        "customer" => "Customer lead",
        "reviewer" => "Reviewer",
        _ => role,
    };

    /// <summary>A chip as drawn: its mark and number, then its age, or its animation frame while an agent is on it,
    /// right-aligned in three cells.</summary>
    internal static string ChipText(Chip chip, DateTimeOffset now, string? playing, IconStyle style)
    {
        if (chip.Card is not { } card)
            return $"+{chip.Hidden}";
        var age = playing ?? (card.Age(now) is { } waited ? Ages.Short(waited) : "");
        return $"{card.Mark(style)}{card.Number,-(NumberWidth - 1)} {age,AgeWidth}";
    }

    /// <summary>The details pane's lines for <paramref name="card"/>.</summary>
    internal static IReadOnlyList<string> Describe(BoardCard card, IReadOnlyList<BoardCard> all, OverseerState state)
    {
        var age = card.Age(state.Now) is { } waited ? $" for {Ages.Full(waited)}" : "";
        var limit = ColumnLimits.Limit(state.Limits, card.Status) is not null ? $" (limit {state.Limits[card.Status]})" : "";
        List<string> lines =
        [
            string.Join(Separator, card.KindName, card.Priority.Length > 0 ? card.Priority : "Unranked", card.Team, $"{card.Status}{age}{limit}"),
        ];
        if (state.Waiting(card.Team, card.Number) is { Reason.Length: > 0 } waiting)
            lines.Add($"Waiting: {(waiting.Mine ? "" : $"{waiting.Turn} · ")}{waiting.Reason}");
        if (card.Kind == CardKind.Pitch)
        {
            var tasks = all.Where(task => task.Team == card.Team && task.Parent == card.Number).OrderBy(task => task.Number).ToList();
            lines.Add(tasks.Count == 0 ? "Open tasks: none" : "Open tasks:");
            lines.AddRange(tasks.Select(task =>
                $"  #{task.Number} {task.Title} ({task.Status}{(task.Age(state.Now) is { } taskAge ? $", {Ages.Short(taskAge)}" : "")})"));
        }
        return lines;
    }

    private int Over(string team) =>
        _board.Cards.Count(card => card.Team == team && ColumnLimits.IsOver(card, _state.Limits, _state.Now));

    private void Move(Action move)
    {
        move();
        Changed();
        ScrollToSelection();
    }

    private void Redraw()
    {
        foreach (var lane in _laneViews)
        {
            if (lane.Slots.Count > 0)
                lane.SetNeedsDraw();
            lane.SubViews.First().SetNeedsDraw();
        }
    }

    private void Changed()
    {
        ShowDetails();
        Redraw();
        _lanes.SetNeedsLayout();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void ShowDetails()
    {
        if (!_details.Visible)
            return;
        if (_board.SelectedCard is not { } card)
        {
            _details.Visible = false;
            SetNeedsLayout();
            return;
        }
        var title = $"#{card.Number} {card.Title}";
        if (_details.Title != title)
            _details.Title = title;
        var text = string.Join("\n", Describe(card, _board.Cards, _state));
        if (_detailText.Text != text)
        {
            _detailText.Text = text;
            SetNeedsLayout();
        }
    }

    private int DetailHeight() =>
        _details.Visible ? Math.Min(MaxDetailRows, _detailText.Text.Split('\n').Length + FrameRows) : 0;

    private void AddLane(string team)
    {
        var lane = new Lane(this, _laneViews.Count) { X = 0, Width = Dim.Fill(), CanFocus = false, Title = Heading(team, [], 0).Title, Data = team };
        lane.Y = Pos.Func(_ => Top(lane), _lanes);
        lane.Height = Dim.Func(_ => _board.Rows(team) + FrameRows, _lanes);
        lane.Add(new ChipGrid(this, team) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() });
        _laneViews.Add(lane);
        _lanes.Add(lane);
    }

    private static string LaneTeam(View lane) => (string)lane.Data!;

    private int Top(Lane lane) =>
        _laneViews.TakeWhile(each => each != lane).Sum(each => _board.Rows(LaneTeam(each)) + FrameRows);

    private int Tall() => _laneViews.Sum(lane => _board.Rows(LaneTeam(lane)) + FrameRows);

    private void FitLanes()
    {
        var size = _lanes.Viewport.Size with { Height = Math.Max(_lanes.Viewport.Height, Tall()) };
        for (var pass = 0; pass < 2 && _lanes.GetContentSize() != size; pass++)
            _lanes.SetContentSize(size);
    }

    /// <summary>Keeps the selected chip's row on screen.</summary>
    private void ScrollToSelection()
    {
        if (_board.Selected is not { } place || _board.SelectedRow is not { } row
            || _laneViews.FirstOrDefault(lane => LaneTeam(lane) == place.Team) is not { } lane)
            return;
        var top = Top(lane);
        var height = _lanes.Viewport.Height;
        var first = top + 1 + row;
        var y = _lanes.Viewport.Y;
        if (top < y || first < y)
            y = top;
        else if (first + 1 >= y + height)
            y = Math.Max(0, first + 2 - height);
        if (_lanes.Viewport.Y != y)
            _lanes.Viewport = _lanes.Viewport with { Y = y };
    }

    /// <summary>Lines the column headings up over the chips inside each lane's frame.</summary>
    private void ShowHeader()
    {
        var width = ColumnWidth(Viewport.Width - FrameRows);
        var header = string.Concat(OverseerBoard.Columns.Select(column => Card.Elide(column, width - 1).PadRight(width)));
        if (_header.Text != header)
            _header.Text = header;
    }

    internal static int ColumnWidth(int inside) => Math.Max(1, inside / OverseerBoard.Columns.Length);

    private Attribute ChipAttribute(BoardCard? card, bool selected)
    {
        if (selected)
            return GetAttributeForRole(VisualRole.Focus);
        if (card is null)
            return GetAttributeForRole(VisualRole.Disabled);
        return CardCells.Colour(Priorities.Scheme(card.Priority), GetAttributeForRole(VisualRole.Normal));
    }

    /// <summary>An age is neutral, and amber only past its column's limit, so it never reads as a Priority.</summary>
    private Attribute AgeAttribute(BoardCard card, bool selected)
    {
        if (selected)
            return GetAttributeForRole(VisualRole.Focus);
        if (ColumnLimits.IsOver(card, _state.Limits, _state.Now) && SchemeManager.TryGetScheme(LogSchemes.Overdue, out var overdue))
            return overdue.GetAttributeForRole(VisualRole.Normal);
        return GetAttributeForRole(VisualRole.Normal);
    }

    /// <summary>The colour a cell of a role's animation is drawn in, on <paramref name="under"/>'s background.</summary>
    internal static Attribute ToneAttribute(RoleAnimation animation, Tone tone, Attribute under) =>
        under with { Foreground = animation.Colour(tone, under.Background.IsDarkColor()) };

    /// <summary>Paints <paramref name="frame"/> from where <paramref name="view"/> last moved to, a colour per cell.</summary>
    private static void PaintFrame(View view, RoleAnimation animation, AnimationFrame frame, Attribute under)
    {
        for (var cell = 0; cell < frame.Text.Length; cell++)
        {
            view.SetAttribute(ToneAttribute(animation, frame.ToneAt(cell), under));
            view.AddStr(frame.Text[cell].ToString());
        }
    }

    /// <summary>A team's framed lane, with each busy role's header frame painted into the cell its title keeps for it.</summary>
    private sealed class Lane(OverseerView overseer, int index) : FrameView
    {
        public IReadOnlyList<HeaderSlot> Slots { get; set; } = [];

        protected override void OnDrawComplete(DrawContext? context)
        {
            base.OnDrawComplete(context);
            var shown = Math.Max(0, Frame.Width - 4);
            var under = GetAttributeForRole(VisualRole.Normal);
            foreach (var slot in Slots)
            {
                if (slot.Column >= shown || RoleAnimation.For(slot.Role) is not { } animation)
                    continue;
                // The border draws the title two cells in from the frame's left edge, on the row above the viewport.
                Move(1 + slot.Column, -1);
                PaintFrame(this, animation, animation.Header(overseer.Elapsed, index), under);
            }
        }
    }

    /// <summary>One lane's chips, a column per status. Painted cell by cell: each chip takes its own colour, which no
    /// built-in list or table draws.</summary>
    private sealed class ChipGrid(OverseerView overseer, string team) : View
    {
        protected override bool OnDrawingContent(DrawContext? context)
        {
            var board = overseer._board;
            var state = overseer._state;
            var width = ColumnWidth(Viewport.Width);
            var elapsed = overseer.Elapsed;
            for (var column = 0; column < OverseerBoard.Columns.Length; column++)
            {
                var chips = board.Chips(team, column);
                for (var row = 0; row < chips.Count; row++)
                {
                    var chip = chips[row];
                    var animation = chip.Card is { } card && state.Worked.TryGetValue((card.Team, card.Number), out var role)
                        ? RoleAnimation.For(role)
                        : null;
                    var frame = animation?.Chip(elapsed, chip.Card!.Number);
                    var full = ChipText(chip, state.Now, frame?.Text, overseer._icons);
                    var text = Card.Elide(full, width - 1);
                    var selected = board.Selected is { } place && place.Team == team && place.Column == column
                        && place.Number == chip.Card?.Number;
                    var age = chip.Card is not null && text == full ? AgeWidth : 0;
                    SetAttribute(overseer.ChipAttribute(chip.Card, selected));
                    AddStr(column * width, row, text[..^age]);
                    if (chip.Card is { } shown && age > 0)
                    {
                        var under = overseer.AgeAttribute(shown, selected);
                        SetAttribute(under);
                        AddStr(column * width + text[..^age].GetColumns(), row, text[^age..]);
                        if (animation is not null && frame is { } playing)
                        {
                            Move(column * width + text[..^age].GetColumns(), row);
                            PaintFrame(this, animation, playing, under);
                        }
                    }
                }
            }
            return true;
        }
    }
}
