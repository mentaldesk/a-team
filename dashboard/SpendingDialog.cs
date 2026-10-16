using System.Drawing;
using System.Globalization;
using System.Text;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>What the teams' runs cost over a period, by team, then theme, then pitch, with each role's share.</summary>
public sealed class SpendingDialog : DashboardDialog
{
    private const string HintText = "←/→ collapse/expand · Enter open · Esc close";
    private const int Inset = 1;
    private const int Wide = 96;
    private const int WhatWide = 40;

    private static readonly Dictionary<string, string> RoleNames = new()
    {
        ["lead"] = "Lead $", ["dev"] = "Dev $", ["customer"] = "Customer $", ["reviewer"] = "Reviewer $",
    };

    private readonly IReadOnlyList<string> _teams;
    private readonly IReadOnlyList<string> _rolesOn;
    private readonly Action<string, SpendingItem>? _open;
    private readonly DateTimeOffset _now;
    private readonly OptionSelector _period;
    private readonly TableView _table;
    private readonly Label _message;
    private readonly Label _footer;
    private IReadOnlyList<(string Team, TeamSpending Spending)>? _spending;
    private TreeView<SpendingRow>? _tree;

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
        _table = new TableView
        {
            X = Inset,
            Y = 2,
            Width = Dim.Fill(Inset),
            Height = Dim.Fill(3),
            FullRowSelect = true,
            Visible = false,
            Style =
            {
                ShowHorizontalHeaderOverline = false,
                ShowHorizontalBottomLine = false,
                ShowVerticalCellLines = false,
                ShowVerticalHeaderLines = false,
                ExpandLastColumn = false,
            },
        };
        _table.KeyDown += (_, key) =>
        {
            if (key == Key.Enter && SelectedRow is { Pitch: { } pitch } row)
            {
                _open?.Invoke(row.Team, pitch);
                key.Handled = true;
            }
        };
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

    internal TableView Table => _table;

    internal Label Message => _message;

    internal Label Footer => _footer;

    internal IReadOnlyList<SpendingRow> Rows { get; private set; } = [];

    internal IReadOnlyList<string> Columns => _table.Table?.ColumnNames ?? [];

    internal bool HasClosed { get; private set; }

    internal SpendingRow? SelectedRow =>
        _tree is not null && _table.Table is { Rows: > 0 } && _table.Value?.SelectedCell.Y is { } y && y >= 0 && y < _table.Table.Rows
            ? ((TreeTableSource<SpendingRow>)_table.Table).GetObjectOnRow(y)
            : null;

    /// <summary>The rows as they're shown, expanded or not.</summary>
    internal IReadOnlyList<SpendingRow> Shown => _table.Table is TreeTableSource<SpendingRow> source ? [.. source.GetAllObjects()] : [];

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
        var roles = Spending.Roles(_rolesOn, Rows);
        var columns = new Dictionary<string, Func<SpendingRow, object>> { ["Runs"] = row => row.Runs.ToString(CultureInfo.InvariantCulture) };
        foreach (var role in roles)
            columns[RoleNames[role]] = row => Money(row.Spent(role));
        columns["Total $"] = row => Money(row.Total);

        var old = _table.Table as IDisposable;
        _tree = new TreeView<SpendingRow>(new DelegateTreeBuilder<SpendingRow>(row => row.Children, row => row.Children.Count > 0))
        {
            AspectGetter = row => Fit(row.Label, WhatWide - 2 * Depth(row)),
        };
        _tree.Style.ShowBranchLines = false;
        _tree.Style.ExpandableSymbol = (Rune)'▸';
        _tree.Style.CollapseableSymbol = (Rune)'▾';
        _tree.AddObjects(Rows);
        _tree.Expand(Rows[0]);
        _table.Style.ColumnStyles.Clear();
        _table.Style.GetOrCreateColumnStyle(0).MinWidth = WhatWide;
        for (var column = 1; column <= columns.Count; column++)
            _table.Style.GetOrCreateColumnStyle(column).Alignment = Alignment.End;
        _table.Table = new TreeTableSource<SpendingRow>(_table, "What", _tree, columns);
        old?.Dispose();
        _message.Visible = false;
        _table.Visible = true;
        _table.SetNeedsDraw();
    }

    private static int Depth(SpendingRow row) => row.Kind switch
    {
        SpendingKind.Team => 0,
        SpendingKind.Pitch => 2,
        _ => 1,
    };

    private static string Money(decimal cost) => cost == 0 ? "–" : cost.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Fit(string text, int wide) => text.Length <= wide ? text : text[..(wide - 1)] + "…";

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
