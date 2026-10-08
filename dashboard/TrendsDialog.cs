using System.Drawing;
using System.Globalization;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Color = Terminal.Gui.Drawing.Color;

namespace ATeam.Dashboard;

/// <summary>The teams side by side over the last fortnight: a chart of one measure, and this week's numbers.</summary>
public sealed class TrendsDialog : Dialog
{
    private const string HintText = "Esc close";
    private const int Inset = 1;
    private const int GraphRows = 14;
    private const int AxisRows = 2;
    private const int LabelWidth = 7;

    internal static readonly string[] Measures = ["Waiting on you", "Accepted per day"];

    private static readonly char[] Markers = ['*', '+', 'o', 'x', '#', '@', '%', '&'];

    private static readonly ColorName16[] OnDark =
        [ColorName16.BrightCyan, ColorName16.BrightMagenta, ColorName16.BrightGreen, ColorName16.BrightYellow, ColorName16.BrightBlue, ColorName16.BrightRed];

    private static readonly ColorName16[] OnLight =
        [ColorName16.Blue, ColorName16.Magenta, ColorName16.Green, ColorName16.Red, ColorName16.Cyan, ColorName16.Black];

    private readonly IReadOnlyList<(string Team, int? Waiting)> _teams;
    private readonly DateTimeOffset _now;
    private readonly TimeZoneInfo _zone;
    private readonly IReadOnlyList<DateOnly> _dates;
    private readonly OptionSelector _measure;
    private readonly GraphView _graph;
    private readonly TableView _table;
    private readonly Label _message;
    private IReadOnlyList<TeamRecord>? _records;
    private Legend? _legend;

    public TrendsDialog(IReadOnlyList<(string Team, int? Waiting)> teams, Task<Reading[]> reading, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        _teams = teams;
        _now = now;
        _zone = zone ?? TimeZoneInfo.Local;
        _dates = TeamRecord.Dates(now, _zone);

        Title = "Trends";
        var tableRows = teams.Count + 2;
        Width = Dim.Func(_ => Fits(76 + GetAdornmentsThickness().Horizontal, Room()?.Width), this);
        Height = Dim.Func(_ => Fits(2 + GraphRows + AxisRows + 1 + tableRows + 2 + GetAdornmentsThickness().Vertical, Room()?.Height), this);

        _measure = new OptionSelector
        {
            X = Inset,
            Y = 0,
            Orientation = Orientation.Horizontal,
            TabBehavior = TabBehavior.NoStop,
            Labels = Measures,
            Value = 0,
            Visible = false,
        };
        _measure.ValueChanged += (_, _) => Plot();
        _graph = new GraphView
        {
            X = Inset,
            Y = 2,
            Width = Dim.Fill(Inset),
            Height = Dim.Fill(tableRows + 3),
            MarginBottom = AxisRows,
            MarginLeft = (uint)LabelWidth,
            CanFocus = false,
            Visible = false,
        };
        _table = new TableView
        {
            X = Inset,
            Y = Pos.AnchorEnd(tableRows + 2),
            Width = Dim.Fill(Inset),
            Height = tableRows,
            CanFocus = false,
            FullRowSelect = false,
            Visible = false,
            Style =
            {
                ShowHorizontalHeaderOverline = false,
                ShowHorizontalHeaderUnderline = false,
                ShowHorizontalBottomLine = false,
                ShowVerticalCellLines = false,
                ShowVerticalHeaderLines = false,
                ExpandLastColumn = false,
            },
        };
        _message = new Label { X = Inset, Y = 0, Width = Dim.Fill(Inset), Text = "Reading the record…" };
        var hint = new Button
        {
            Text = HintText,
            X = Pos.Center(),
            Y = Pos.AnchorEnd(1),
            NoDecorations = true,
            NoPadding = true,
            ShadowStyle = ShadowStyles.None,
            HotKeySpecifier = (Rune)0xffff,
            CanFocus = false,
        };
        hint.Accepting += (_, args) => args.Handled = Close();
        _graph.ViewportChanged += (_, _) => Plot();
        Add(_measure, _graph, _table, _message, hint);

        reading.ContinueWith(read =>
        {
            var readings = read.Status == TaskStatus.RanToCompletion ? read.Result : null;
            OnUi(() => Loaded(readings));
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    internal OptionSelector Measure => _measure;

    internal GraphView Graph => _graph;

    internal Label Message => _message;

    internal IReadOnlyList<TeamTrendRow> Rows { get; private set; } = [];

    internal TeamTrendRow? Total { get; private set; }

    internal bool Closed { get; private set; }

    internal IReadOnlyList<IReadOnlyList<int?>> Series =>
        _records is null ? [] : [.. _records.Select(record => record.Series(Chosen, _dates, _zone))];

    private TrendMeasure Chosen => _measure.Value == 1 ? TrendMeasure.Accepted : TrendMeasure.Waiting;

    /// <summary>Enter reaches a Dialog as Accept and would close it; Trends has nothing to confirm.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => true;

    protected override bool OnKeyDown(Key key) => key == Key.Esc ? Close() : base.OnKeyDown(key);

    private void Loaded(Reading[]? readings)
    {
        var failure = readings is null ? "couldn't read the record"
            : readings.Select(reading => reading.Failure).FirstOrDefault(failed => failed is { Length: > 0 });
        var records = readings?.Select(TeamRecord.Of).ToList();
        if (failure is null && (records is null || records.Contains(null)))
            failure = "couldn't read the record";
        if (failure is not null)
        {
            _message.Text = $"Couldn't read the record: {failure}";
            return;
        }
        _records = [.. records!.OfType<TeamRecord>()];
        if (_records.All(record => record.Since is null))
        {
            _message.Text = "Nothing recorded yet. a-team keeps its record from the day it's installed.";
            return;
        }
        Rows = [.. _teams.Select((team, i) => TeamTrendRow.Of(team.Team, team.Waiting, _records[i], _now))];
        Total = TeamTrendRow.Total(Rows);
        _table.Table = new EnumerableTableSource<TeamTrendRow>([.. Rows, Total], new Dictionary<string, Func<TeamTrendRow, object>>
        {
            ["Team"] = row => row.Team,
            ["Waiting now"] = row => Number(row.WaitingNow),
            ["A week ago"] = row => Number(row.WeekAgo),
            ["Accepted (7d)"] = row => Number(row.Accepted),
            ["Runs $"] = row => row.Cost.ToString("0.00", CultureInfo.InvariantCulture),
        });
        for (var column = 1; column < 5; column++)
            _table.Style.GetOrCreateColumnStyle(column).Alignment = Alignment.End;
        _legend = new Legend([.. _teams.Select((team, i) => (Cell(i), team.Team))]);
        _message.Visible = false;
        _measure.Visible = _graph.Visible = _table.Visible = true;
        _measure.SetFocus();
        Plot();
    }

    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "–";

    private void Plot()
    {
        if (_records is null || _legend is null || !_graph.Visible || _graph.Viewport.Width == 0)
            return;
        var series = Series;
        var plotWide = Math.Max(TeamRecord.Days, _graph.Viewport.Width - LabelWidth - LegendWide - 1);
        var perDay = Math.Max(1, (plotWide - 1) / (TeamRecord.Days - 1));
        var plotTall = Math.Max(2, _graph.Viewport.Height - AxisRows);
        var highest = Math.Max(1, series.SelectMany(values => values).Max(value => value ?? 0));
        var perRow = Math.Max(1, (int)Math.Ceiling(highest / (double)(plotTall - 1)));

        _graph.Series.Clear();
        _graph.Annotations.Clear();
        _graph.CellSize = new PointF(1, perRow);
        _graph.AxisX.Increment = perDay;
        _graph.AxisX.ShowLabelsEvery = (uint)Math.Ceiling(8.0 / perDay);
        _graph.AxisX.Minimum = 0;
        _graph.AxisX.LabelGetter = increment => DayLabel((int)Math.Round(increment.Value / perDay));
        _graph.AxisY.Increment = perRow * 2;
        _graph.AxisY.ShowLabelsEvery = 1;
        _graph.AxisY.Minimum = 0;
        _graph.AxisY.LabelGetter = increment => increment.Value.ToString("0", CultureInfo.InvariantCulture);

        for (var i = 0; i < series.Count; i++)
        {
            var cell = Cell(i);
            var points = series[i].Select((value, day) => (value, day)).Where(point => point.value is not null)
                .Select(point => new PointF(point.day * perDay, point.value!.Value)).ToList();
            foreach (var run in Runs(series[i]))
                _graph.Annotations.Add(new PathAnnotation
                {
                    LineColor = cell.Color,
                    BeforeSeries = true,
                    Points = [.. run.Select(point => new PointF(point.Day * perDay, point.Value))],
                });
            _graph.Series.Add(new ScatterSeries { Points = points, Fill = cell });
        }
        _graph.Annotations.Add(_legend);
        _graph.SetNeedsDraw();
    }

    private int LegendWide => _teams.Max(team => team.Team.Length) + 3;

    private GraphCellToRender Cell(int team)
    {
        var background = _graph.GetAttributeForRole(VisualRole.Normal).Background;
        var colours = background.IsDarkColor() ? OnDark : OnLight;
        return new((Rune)Markers[team % Markers.Length], new Attribute(new Color(colours[team % colours.Length]), background));
    }

    private static IEnumerable<List<(int Day, int Value)>> Runs(IReadOnlyList<int?> values)
    {
        var run = new List<(int Day, int Value)>();
        for (var day = 0; day < values.Count; day++)
        {
            if (values[day] is { } value)
            {
                run.Add((day, value));
                continue;
            }
            if (run.Count > 1)
                yield return run;
            run = [];
        }
        if (run.Count > 1)
            yield return run;
    }

    private string DayLabel(int index)
    {
        if (index < 0 || index >= _dates.Count)
            return "";
        var date = _dates[index];
        return date.ToString(index == 0 || date.Day == 1 ? "d MMM" : "%d", CultureInfo.InvariantCulture);
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
        Closed = true;
        RequestStop();
        return true;
    }

    public static void Show(IApplication app, IReadOnlyList<(string Team, int? Waiting)> teams, Func<string, Task<Reading>> read)
    {
        using var dialog = new TrendsDialog(teams, Task.WhenAll(teams.Select(team => read(team.Team))), DateTimeOffset.Now);
        app.Run(dialog);
    }

    /// <summary>LegendAnnotation draws as a blank box in Terminal.Gui 2.5.</summary>
    private sealed class Legend(IReadOnlyList<(GraphCellToRender Cell, string Team)> entries) : IAnnotation
    {
        public bool BeforeSeries => false;

        public void Render(GraphView graph, DrawContext? context)
        {
            var x = graph.Viewport.Width - entries.Max(entry => entry.Team.Length) - 2;
            for (var line = 0; line < entries.Count && line < graph.Viewport.Height; line++)
            {
                var (cell, team) = entries[line];
                graph.SetAttribute(cell.Color ?? graph.GetAttributeForRole(VisualRole.Normal));
                graph.AddRune(x, line, cell.Rune);
                graph.SetAttribute(graph.GetAttributeForRole(VisualRole.Normal));
                graph.Move(x + 2, line);
                graph.AddStr(team);
            }
        }
    }
}
