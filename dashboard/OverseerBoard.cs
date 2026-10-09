namespace ATeam.Dashboard;

/// <summary>A chip in a column: a card, or the <c>+N</c> that stands in for the cards a folded lane hides.</summary>
public sealed record Chip(BoardCard? Card, int Hidden = 0)
{
    public bool IsMore => Card is null;
}

/// <summary>Where the selection is: a card by team and number, or a lane's <c>+N</c> in a column.</summary>
public readonly record struct ChipPlace(string Team, int Column, int? Number);

/// <summary>Every team's board as Overseer lays it out: a lane per team, a column per status, chips oldest first,
/// and the selection moving between them.</summary>
public sealed class OverseerBoard
{
    public static readonly string[] Columns = ["Idea", "Exploring", "Pitched", "Approved", "Building", "Ready", "In progress", "In review"];

    /// <summary>Rows a column shows before a folded lane puts the rest behind a <c>+N</c>.</summary>
    public const int FoldRows = 3;

    private readonly List<string> _teams;
    private readonly HashSet<string> _unfolded = [];
    private IReadOnlyList<BoardCard> _cards = [];

    public OverseerBoard(IEnumerable<string> teams) => _teams = [.. teams];

    public IReadOnlyList<string> Teams => _teams;

    public IReadOnlyList<BoardCard> Cards => _cards;

    public ChipPlace? Selected { get; private set; }

    public void Show(IReadOnlyList<BoardCard> cards)
    {
        _cards = cards;
        Reselect();
    }

    public void Forget(IReadOnlyList<string> teams)
    {
        _teams.RemoveAll(teams.Contains);
        _cards = [.. _cards.Where(card => !teams.Contains(card.Team))];
        Reselect();
    }

    public bool Unfolded(string team) => _unfolded.Contains(team);

    /// <summary>A column's cards, the one that has waited longest first.</summary>
    public IReadOnlyList<BoardCard> Column(string team, int column) =>
        [.. _cards.Where(card => card.Team == team && card.Status == Columns[column])
            .OrderBy(card => card.Since ?? DateTimeOffset.MaxValue)
            .ThenBy(card => card.Number)];

    /// <summary>The chips a column draws: all of them in an unfolded lane, and otherwise as many as fit in
    /// <see cref="FoldRows"/> with a <c>+N</c> in the last row for the rest.</summary>
    public IReadOnlyList<Chip> Chips(string team, int column)
    {
        var cards = Column(team, column);
        if (cards.Count <= FoldRows || Unfolded(team))
            return [.. cards.Select(card => new Chip(card))];
        return [.. cards.Take(FoldRows - 1).Select(card => new Chip(card)), new Chip(null, cards.Count - (FoldRows - 1))];
    }

    /// <summary>How many rows the lane needs for its fullest column, and never none.</summary>
    public int Rows(string team) => Math.Max(1, Enumerable.Range(0, Columns.Length).Max(column => Chips(team, column).Count));

    public BoardCard? SelectedCard =>
        Selected is { Number: { } number } place ? _cards.FirstOrDefault(card => card.Team == place.Team && card.Number == number) : null;

    public bool OnMore => Selected is { Number: null };

    /// <summary>Which row of its column the selection is on.</summary>
    public int? SelectedRow => Selected is { } place && IndexOf(place) is >= 0 and var row ? row : null;

    /// <summary>Starts on the first chip of the first lane with any.</summary>
    public void SelectFirst()
    {
        Selected = null;
        foreach (var team in _teams)
            if (FirstColumn(team, 0, +1) is { } column)
            {
                Selected = At(team, column, 0);
                return;
            }
    }

    public bool Select(string team, int number)
    {
        if (_cards.FirstOrDefault(card => card.Team == team && card.Number == number) is not { } card)
            return false;
        var column = Array.IndexOf(Columns, card.Status);
        if (column < 0)
            return false;
        var chips = Chips(team, column);
        Selected = chips.Any(chip => chip.Card == card) ? new ChipPlace(team, column, number) : At(team, column, chips.Count - 1);
        return true;
    }

    /// <summary>Left and right go to the nearest column that way with chips in it, on the same row or its last.</summary>
    public void MoveColumn(int step)
    {
        if (Selected is not { } place || SelectedRow is not { } row)
        {
            SelectFirst();
            return;
        }
        for (var column = place.Column + step; column >= 0 && column < Columns.Length; column += step)
        {
            var chips = Chips(place.Team, column);
            if (chips.Count == 0)
                continue;
            Selected = At(place.Team, column, Math.Min(row, chips.Count - 1));
            return;
        }
    }

    /// <summary>Up and down go along a column, and past its ends into the lane before or after, to the column
    /// nearest this one with chips in it.</summary>
    public void MoveRow(int step)
    {
        if (Selected is not { } place || SelectedRow is not { } row)
        {
            SelectFirst();
            return;
        }
        var chips = Chips(place.Team, place.Column);
        if (row + step >= 0 && row + step < chips.Count)
        {
            Selected = At(place.Team, place.Column, row + step);
            return;
        }
        for (var lane = _teams.IndexOf(place.Team) + step; lane >= 0 && lane < _teams.Count; lane += step)
        {
            var team = _teams[lane];
            if (Nearest(team, place.Column) is not { } column)
                continue;
            Selected = At(team, column, step > 0 ? 0 : Chips(team, column).Count - 1);
            return;
        }
    }

    /// <summary>Tab and Shift+Tab: the next or previous lane with cards, round past the end.</summary>
    public void MoveLane(int step)
    {
        if (Selected is not { } place)
        {
            SelectFirst();
            return;
        }
        var from = _teams.IndexOf(place.Team);
        for (var offset = 1; offset < _teams.Count; offset++)
            if (SelectLane(_teams[((from + step * offset) % _teams.Count + _teams.Count) % _teams.Count], place.Column))
                return;
    }

    /// <summary>The top chip of the lane's column nearest <paramref name="column"/>, if the lane has any.</summary>
    public bool SelectLane(string team, int column)
    {
        if (Nearest(team, column) is not { } nearest)
            return false;
        Selected = At(team, nearest, 0);
        return true;
    }

    /// <summary>Shows every chip in the selected lane, staying on the card the <c>+N</c> stood in front of.</summary>
    public void Unfold()
    {
        if (Selected is not { } place)
            return;
        var row = SelectedRow ?? 0;
        _unfolded.Add(place.Team);
        Selected = At(place.Team, place.Column, row);
    }

    /// <summary>Folds the selected lane again, the selection going to the <c>+N</c> where its card is folded away.</summary>
    public bool Fold()
    {
        if (Selected is not { } place || !_unfolded.Remove(place.Team))
            return false;
        var chips = Chips(place.Team, place.Column);
        if (place.Number is { } number && chips.All(chip => chip.Card?.Number != number))
            Selected = At(place.Team, place.Column, chips.Count - 1);
        return true;
    }

    /// <summary>After a read, the same card wherever it has moved to, or the chip nearest where it was.</summary>
    private void Reselect()
    {
        if (Selected is not { } place)
            return;
        if (place.Number is { } number && Select(place.Team, number))
            return;
        if (!_teams.Contains(place.Team))
        {
            SelectFirst();
            return;
        }
        if (Nearest(place.Team, place.Column) is { } column)
            Selected = At(place.Team, column, SelectedRow ?? 0);
        else
            SelectFirst();
    }

    private int? Nearest(string team, int column) =>
        Enumerable.Range(0, Columns.Length)
            .Where(each => Chips(team, each).Count > 0)
            .OrderBy(each => Math.Abs(each - column))
            .Cast<int?>()
            .FirstOrDefault();

    private int? FirstColumn(string team, int from, int step)
    {
        for (var column = from; column >= 0 && column < Columns.Length; column += step)
            if (Chips(team, column).Count > 0)
                return column;
        return null;
    }

    private ChipPlace At(string team, int column, int row)
    {
        var chips = Chips(team, column);
        var chip = chips[Math.Clamp(row, 0, chips.Count - 1)];
        return new ChipPlace(team, column, chip.Card?.Number);
    }

    private int IndexOf(ChipPlace place)
    {
        var chips = Chips(place.Team, place.Column);
        return place.Number is { } number
            ? chips.ToList().FindIndex(chip => chip.Card?.Number == number)
            : chips.ToList().FindIndex(chip => chip.IsMore);
    }
}
