using MentalDesk.Tui.Fields;
using MentalDesk.Tui.Settings;

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
    private readonly List<(string Column, PlaceholderField Field, Label Default)> _limits;
    private IReadOnlyDictionary<string, string> _kept;

    public OverseerPage(DashboardSettings settings) : base(Name)
    {
        _settings = settings;
        _kept = settings.ReadLimits();
        var label = OverseerBoard.Columns.Max(column => column.Length) + 1;
        _limits = [.. OverseerBoard.Columns.Select((column, row) => (column,
            new PlaceholderField { X = Indent + label, Y = FirstRow + row, Width = FieldWidth, Text = KeptLimit(column) },
            Dimmed(new Label { X = Indent + label + FieldWidth + 2, Y = FirstRow + row })))];
        Add(new Label { Text = Heading });
        foreach (var (column, field, note) in _limits)
        {
            field.TextChanged += (_, _) =>
            {
                Show();
                OnChanged();
            };
            Add(new Label { X = Indent, Y = field.Y, Text = column }, field, note);
        }
        Add(Dimmed(new Label { Y = FirstRow + _limits.Count + 1, Text = Hint }));
        Show();
    }

    internal IReadOnlyList<(string Column, PlaceholderField Field)> Fields => [.. _limits.Select(limit => (limit.Column, limit.Field))];

    /// <summary>Each column's limit as entered, blank for none.</summary>
    internal IReadOnlyDictionary<string, string> Limits =>
        _limits.ToDictionary(limit => limit.Column, limit => limit.Field.Text.Trim());

    internal IReadOnlyList<string> Defaults => [.. _limits.Select(limit => limit.Default.Text)];

    public override bool IsChanged => _limits.Any(limit => limit.Field.Text.Trim() != KeptLimit(limit.Column));

    /// <summary>The first column whose limit isn't one Overseer can read, or null when they all are.</summary>
    internal string? Unreadable() =>
        _limits.FirstOrDefault(limit => !ColumnLimits.TryParse(limit.Field.Text, out _)).Column;

    internal static string Refusal(string column) =>
        $"Overseer: can't read the limit for {column}. Use a number and m, h, d or w, like 3d, or leave it blank.";

    /// <summary>Puts focus on the first limit Overseer can't read, and says so; false when they all read.</summary>
    internal bool Refuse(Action<string> say)
    {
        if (Unreadable() is not { } column)
            return false;
        _limits.First(limit => limit.Column == column).Field.SetFocus();
        say(Refusal(column));
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
        foreach (var (column, field, _) in _limits)
            field.Text = KeptLimit(column);
    }

    private string KeptLimit(string column) => _kept.TryGetValue(column, out var limit) ? limit : "";

    private void Show()
    {
        foreach (var (_, field, note) in _limits)
            note.Text = field.Text.Trim().Length == 0 ? "" : DefaultNote("none");
    }
}
