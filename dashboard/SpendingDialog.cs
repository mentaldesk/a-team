using System.Drawing;
using System.Globalization;
using System.Text;
using MentalDesk.Tui.Columns;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>What the teams' runs cost over a period, by team, then theme, then pitch, with each role's share.</summary>
public sealed class SpendingDialog : DashboardDialog
{
    private const string HintText = "←/→ collapse/expand · Enter open · Esc close";
    private const int Inset = 1;
    private const int Wide = 96;
    private const int WhatWide = 40;
    private const string Expanded = "▾ ";
    private const string Collapsed = "▸ ";
    private const string Leaf = "  ";

    private static readonly Dictionary<string, string> RoleNames = new()
    {
        ["lead"] = "Lead $", ["dev"] = "Dev $", ["customer"] = "Customer $", ["reviewer"] = "Reviewer $",
    };

    private readonly IReadOnlyList<string> _teams;
    private readonly IReadOnlyList<string> _rolesOn;
    private readonly Action<string, SpendingItem>? _open;
    private readonly DateTimeOffset _now;
    private readonly OptionSelector _period;
    private readonly Label _message;
    private readonly Label _footer;
    private readonly HashSet<string> _expanded = [];
    private ColumnTable<SpendingLine> _table;
    private IReadOnlyList<(string Team, TeamSpending Spending)>? _spending;
    private bool _shownBefore;

    public SpendingDialog(IReadOnlyList<string> teams, Task<Reading[]> reading, IEnumerable<string> rolesOn, DateTimeOffset now,
        Action<string, SpendingItem>? open = null)
        : base("Spending")
    {
        _teams = teams;
        _rolesOn = [.. rolesOn];
        _open = open;
        _now = now;

        Width = Dim.Func(_ => Fits(Wide + GetAdornmentsThickness().Horizontal, Room()?.Width), this);
        Height = Dim.Func(_ => Fits(30, Room()?.Height), this);

        _period = new OptionSelector
        {
            X = Inset,
            Y = 0,
            Orientation = Orientation.Horizontal,
            Labels = Spending.Periods,
            Value = 0,
            Visible = false,
        };
        _period.ValueChanged += (_, _) => Show();
        _period.KeyDown += (_, key) =>
            key.Handled = key == Key.CursorRight ? Step(+1) : key == Key.CursorLeft && Step(-1);
        _table = NewTable([]);
        _message = new Label { X = Inset, Y = 2, Width = Dim.Fill(Inset), Text = "Reading the record…" };
        _footer = new Label { X = Inset, Y = Pos.AnchorEnd(2), Width = Dim.Fill(Inset), Text = "" };
        var hint = new Button
        {
            Text = HintText,
            X = Inset,
            Y = Pos.AnchorEnd(1),
            NoDecorations = true,
            NoPadding = true,
            ShadowStyle = ShadowStyles.None,
            HotKeySpecifier = (Rune)0xffff,
            CanFocus = false,
        };
        hint.Accepting += (_, args) => args.Handled = Close();
        Register(CancelId, "Close", () => Close());
        Also("←/→", "Period");
        Also("↑/↓", "Select a row");
        Also("Enter", "Read the pitch", () => SelectedRow is { Pitch: not null });
        Add(_period, _table, _message, _footer, hint);

        reading.ContinueWith(read =>
        {
            var readings = read.Status == TaskStatus.RanToCompletion ? read.Result : null;
            OnUi(() => Loaded(readings));
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    internal OptionSelector Period => _period;

    internal ColumnTable<SpendingLine> Table => _table;

    internal Label Message => _message;

    internal Label Footer => _footer;

    internal IReadOnlyList<SpendingRow> Rows { get; private set; } = [];

    internal IReadOnlyList<string> Columns => [.. _table.RowLayout.Columns.Select(column => column.Header)];

    internal bool HasClosed { get; private set; }

    internal SpendingRow? SelectedRow => _table.Visible ? _table.SelectedRow?.Row : null;

    /// <summary>The rows as they're shown, expanded or not.</summary>
    internal IReadOnlyList<SpendingRow> Shown => [.. _table.Rows.Select(line => line.Row)];

    private void Loaded(Reading[]? readings)
    {
        var failure = readings is null ? "couldn't read the record"
            : readings.Select(reading => reading.Failure).FirstOrDefault(failed => failed is { Length: > 0 });
        var spending = readings?.Select(TeamSpending.Of).ToList();
        if (failure is null && (spending is null || spending.Contains(null)))
            failure = "couldn't read the record";
        if (failure is not null)
        {
            _message.Text = $"Couldn't read the record: {failure}";
            return;
        }
        _spending = [.. _teams.Zip(spending!.OfType<TeamSpending>())];
        var since = _spending.Select(team => team.Spending.Since).OfType<DateTimeOffset>().ToList();
        if (since.Count > 0)
            _footer.Text = $"Costs at API prices, from {since.Min().ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture)}.";
        _period.Visible = true;
        Show();
        if (_table.Visible)
            _table.SetFocus();
    }

    private void Show()
    {
        if (_spending is null)
            return;
        Rows = Spending.Roll(_spending, Spending.From(_period.Value ?? 0, _now));
        if (Rows.Count == 0)
        {
            _message.Text = $"No runs in the last {Spending.Periods[_period.Value ?? 0]}.";
            if (_period.Value == Spending.Periods.Length - 1)
                _message.Text = "No runs recorded yet.";
            _message.Visible = true;
            _table.Visible = false;
            _period.SetFocus();
            return;
        }
        if (!_shownBefore)
            _expanded.Add(PlaceOf(Rows[0], ""));
        _shownBefore = true;
        var roles = Spending.Roles(_rolesOn, Rows);
        var selected = _table.SelectedRow?.Key;
        if (!_table.RowLayout.Columns.Skip(3).Select(column => column.Header).SequenceEqual(roles.Select(role => RoleNames[role])))
            Replace(NewTable(roles));
        Lay(selected);
        _message.Visible = false;
        _table.Visible = true;
    }

    private ColumnTable<SpendingLine> NewTable(IReadOnlyList<string> roles)
    {
        var columns = new List<Column<SpendingLine>>
        {
            Column<SpendingLine>.Name("What", line => line.Text, minWidth: 20) with { Width = WhatWide },
            Column<SpendingLine>.Number("Total $", line => Money(line.Row.Total)),
            Column<SpendingLine>.Number("Runs", line => line.Row.Runs.ToString(CultureInfo.InvariantCulture)),
        };
        columns.AddRange(roles.Select(role => Column<SpendingLine>.Number(RoleNames[role], line => Money(line.Row.Spent(role)))));
        var table = new ColumnTable<SpendingLine>(new RowLayout<SpendingLine>(columns), [])
        {
            X = Inset,
            Y = 2,
            Width = Dim.Fill(Inset),
            Height = Dim.Fill(3),
            Visible = false,
        };
        table.KeyDown += (_, key) => key.Handled = Pressed(key);
        return table;
    }

    private void Replace(ColumnTable<SpendingLine> table)
    {
        var focused = _table.HasFocus;
        Remove(_table);
        _table.Dispose();
        _table = table;
        _table.Visible = true;
        Add(_table);
        if (focused)
            _table.SetFocus();
    }

    private bool Pressed(Key key)
    {
        if (_table.SelectedRow is not { } line)
            return false;
        if (key == Key.Enter)
        {
            if (line.Row.Pitch is { } pitch)
                _open?.Invoke(line.Row.Team, pitch);
            return line.Row.Pitch is not null;
        }
        if (key == Key.CursorRight && line.Row.Children.Count > 0 && _expanded.Add(line.Key))
            Lay(line.Key);
        else if (key == Key.CursorLeft && _expanded.Remove(line.Key))
            Lay(line.Key);
        return key == Key.CursorRight || key == Key.CursorLeft;
    }

    /// <summary>Lists the expanded rows, keeping the selection on the row with the same place in the tree.</summary>
    private void Lay(string? selected)
    {
        var lines = new List<SpendingLine>();
        foreach (var row in Rows)
            Flatten(row, "", 0, lines);
        _table.Rows = lines;
        var at = lines.FindIndex(line => line.Key == selected);
        _table.Value = new TableSelection(new Point(0, Math.Max(0, at)));
        _table.SetNeedsDraw();
    }

    private void Flatten(SpendingRow row, string parent, int depth, List<SpendingLine> lines)
    {
        var key = PlaceOf(row, parent);
        var open = _expanded.Contains(key);
        var symbol = row.Children.Count == 0 ? Leaf : open ? Expanded : Collapsed;
        lines.Add(new SpendingLine(row, key, new string(' ', 2 * depth) + symbol + row.Label));
        if (open)
            foreach (var child in row.Children)
                Flatten(child, key, depth + 1, lines);
    }

    private static string PlaceOf(SpendingRow row, string parent) => $"{parent}/{row.Label}";

    private static string Money(decimal cost) => cost == 0 ? "–" : cost.ToString("0.00", CultureInfo.InvariantCulture);

    private bool Step(int by)
    {
        var next = ((_period.Value ?? 0) + by + Spending.Periods.Length) % Spending.Periods.Length;
        _period.Value = next;
        _period.FocusedItem = next;
        return true;
    }

    private static int Fits(int wanted, int? available) => available is { } room ? Math.Min(wanted, room) : wanted;

    private Size? Room() => SuperView?.Viewport.Size ?? App?.Screen.Size;

    private void OnUi(Action action)
    {
        if (App is { } app)
            app.Invoke(action);
        else
            action();
    }

    private bool Close()
    {
        HasClosed = true;
        RequestStop();
        return true;
    }

    public static void Show(IApplication app, IReadOnlyList<string> teams, Func<string, Task<Reading>> read, IEnumerable<string> rolesOn,
        Action<string, SpendingItem> open)
    {
        using var dialog = new SpendingDialog(teams, Task.WhenAll(teams.Select(read)), rolesOn, DateTimeOffset.Now, open);
        DialogHost.Run(app, dialog);
    }
}

/// <summary>A row of Spending as it's listed: indented to its depth, with ▸ or ▾ when it has rows under it.</summary>
public sealed record SpendingLine(SpendingRow Row, string Key, string Text);
