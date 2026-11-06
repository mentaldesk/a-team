using MentalDesk.Tui.Settings;
using MentalDesk.Tui.Theming;

namespace ATeam.Dashboard;

/// <summary>Settings' Overseer page: how long a card may stay in each column before Overseer highlights it.</summary>
public sealed class OverseerPage : SettingsPage
{
    internal const string Name = "Overseer";
    private const string Heading = "Highlight a card that stays in a column longer than";
    private const string Hint = "e.g. 30m, 2h, 3d or 2w; blank for no limit";
    private const int FirstRow = 2;
    private const int Indent = 2;
    private const int FieldWidth = 10;

    private readonly DashboardSettings _settings;
    private readonly List<Row> _limits;
    private readonly Action<string> _say;
    private IReadOnlyDictionary<string, string> _kept;

    /// <param name="say">Puts a message at the foot of the screen; an empty one takes it away.</param>
    public OverseerPage(DashboardSettings settings, Action<string>? say = null) : base(Name)
    {
        _settings = settings;
        _say = say ?? (_ => { });
        _kept = settings.ReadLimits();
        var label = OverseerBoard.Columns.Max(column => column.Length) + 1;
        _limits = [.. OverseerBoard.Columns.Select((column, row) => new Row(column,
            new Label { X = Indent, Y = FirstRow + row, Text = column },
            new TextField { X = Indent + label, Y = FirstRow + row, Width = FieldWidth, Text = KeptLimit(column) },
            Dimmed(new Label { X = Indent + label + FieldWidth + 2, Y = FirstRow + row })))];
        Add(new Label { Text = Heading });
        foreach (var limit in _limits)
        {
            limit.Field.TextChanged += (_, _) =>
            {
                Show();
                OnChanged();
                if (limit.Marked)
                {
                    Mark(limit);
                    SayMarks();
                }
            };
            limit.Field.HasFocusChanged += (_, e) =>
            {
                if (e.NewValue && _limits.Any(row => row.Marked))
                    SayMarks();
            };
            Add(limit.Name, limit.Field, limit.Default);
        }
        Add(Dimmed(new Label { Y = FirstRow + _limits.Count + 1, Text = Hint }));
        Show();
    }

    internal IReadOnlyList<(string Column, TextField Field)> Fields => [.. _limits.Select(limit => (limit.Column, limit.Field))];

    /// <summary>The columns whose limit is marked as refused.</summary>
    internal IReadOnlyList<string> Marked => [.. _limits.Where(limit => limit.Marked).Select(limit => limit.Column)];

    internal Label LabelOf(string column) => _limits.First(limit => limit.Column == column).Name;

    /// <summary>Each column's limit as entered, blank for none.</summary>
    internal IReadOnlyDictionary<string, string> Limits =>
        _limits.ToDictionary(limit => limit.Column, limit => limit.Field.Text.Trim());

    internal IReadOnlyList<string> Defaults => [.. _limits.Select(limit => limit.Default.Text)];

    public override bool IsChanged => _limits.Any(limit => limit.Field.Text.Trim() != KeptLimit(limit.Column));

    /// <summary>The first column whose limit isn't one Overseer can read, or null when they all are.</summary>
    internal string? Unreadable() =>
        _limits.Find(limit => !ColumnLimits.TryParse(limit.Field.Text, out _))?.Column;

    internal static string Refusal(string column) =>
        $"Overseer: can't read the limit for {column}. Use a number and m, h, d or w, like 3d, or leave it blank";

    /// <summary>The library's Check, which a page can't reach: marks every limit Overseer can't read, focuses the first
    /// and speaks for the focused one; false when they all read.</summary>
    internal bool Refuse()
    {
        _limits.ForEach(Mark);
        if (_limits.Find(limit => limit.Marked) is not { } first)
            return false;
        first.Field.SetFocus();
        SayMarks();
        return true;
    }

    public override void Keep()
    {
        if (Unreadable() is { } column)
            throw new IOException(Refusal(column));
        _settings.WriteLimits(Limits);
        _kept = _settings.ReadLimits();
    }

    public override void Revert()
    {
        foreach (var limit in _limits)
            limit.Field.Text = KeptLimit(limit.Column);
    }

    private string KeptLimit(string column) => _kept.TryGetValue(column, out var limit) ? limit : "";

    private void Show()
    {
        foreach (var limit in _limits)
            limit.Default.Text = limit.Field.Text.Trim().Length == 0 ? "" : DefaultNote("none");
    }

    private static void Mark(Row limit)
    {
        limit.Marked = !ColumnLimits.TryParse(limit.Field.Text, out _);
        limit.Name.SchemeName = limit.Marked ? SchemeNames.Error : null;
    }

    private void SayMarks()
    {
        var marked = _limits.FindAll(limit => limit.Marked);
        var said = marked.Find(limit => limit.Field.HasFocus) ?? marked.FirstOrDefault();
        _say(said is null ? "" : marked.Count == 1 ? Refusal(said.Column) : $"{Refusal(said.Column)}  •  {marked.Count - 1} more to fix");
    }

    private sealed record Row(string Column, Label Name, TextField Field, Label Default)
    {
        public bool Marked { get; set; }
    }
}
