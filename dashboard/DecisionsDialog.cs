using System.Drawing;
using Terminal.Gui.Drawing;
using MentalDesk.Tui.Keys;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>What the team remembers you deciding, to read, search and correct: the selected team's entries and all
/// teams', newest confirmed first, with the selected one in full below.</summary>
public sealed class DecisionsDialog : Dialog
{
    internal const string OpenHint = "open";
    internal const string EditHint = "edit";
    internal const string KindHint = "kind";
    internal const string WidenHint = "widen";
    internal const string RetireHint = "retire";
    internal const string SaveHint = "save";
    internal const string CancelHint = "cancel";
    internal const string CloseHint = "close";
    private const int DetailRows = 6;

    private readonly string _team;
    private readonly Func<Task<Reading>> _read;
    private readonly Func<DecisionChange, Task<string?>> _change;
    private readonly Action<string> _openUrl;
    private readonly TextField _search = new() { X = 8, Y = 0, Width = 28 };
    private readonly CheckBox _allTeams = new() { Text = "Include all-teams", Value = CheckState.Checked, Y = 0 };
    private readonly CheckBox _replaced = new() { Text = "Show replaced", Value = CheckState.UnChecked, Y = 0 };
    private readonly TableView _table;
    private readonly Label _empty = new() { X = 1, Y = 2, Width = Dim.Fill(1), Visible = false };
    private readonly Line _rule = new() { X = 0, Width = Dim.Fill() };
    private readonly LogView _detail = new() { X = 1, Width = Dim.Fill(1), Height = DetailRows, CanFocus = false };
    private readonly TextField _editor = new() { X = 1, Width = Dim.Fill(1), Visible = false };
    private readonly StatusBar _hints = new();
    private readonly MessageBar _message = new();
    private IReadOnlyList<Decision> _shown = [];
    private int? _keep;

    public DecisionsDialog(string team, Func<Task<Reading>> read, Func<DecisionChange, Task<string?>> change, Action<string> openUrl)
    {
        _team = team;
        _read = read;
        _change = change;
        _openUrl = openUrl;
        Title = $"Decisions · {team}";
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        int HintRow() => Math.Max(0, Viewport.Height - 1 - _message.Lines);

        _allTeams.X = Pos.Right(_search) + 3;
        _replaced.X = Pos.Right(_allTeams) + 3;
        _table = new TableView
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Func(_ => Math.Max(1, HintRow() - 2 - DetailRows - 2), this),
            FullRowSelect = true,
            Style = { ShowVerticalCellLines = false, ShowVerticalHeaderLines = false, ShowHorizontalHeaderOverline = false,
                ShowHorizontalBottomLine = false, ExpandLastColumn = false },
        };
        _rule.Y = Pos.Bottom(_table);
        _detail.Y = Pos.Bottom(_rule);
        _editor.Y = Pos.Top(_detail);
        _hints.Y = Pos.Func(_ => HintRow(), this);
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);

        _search.ValueChanged += (_, _) => Show();
        _search.KeyDown += (_, key) => key.Handled = Searching(key);
        _allTeams.ValueChanged += (_, _) => Show();
        _replaced.ValueChanged += (_, _) => Show();
        _table.ValueChanged += (_, _) => ShowSelected();
        _table.KeyDown += (_, key) => key.Handled = Browsing(key);
        _editor.KeyDown += (_, key) => key.Handled = Editing(key);
        Add(new Label { X = 0, Y = 0, Text = "Search:" }, _search, _allTeams, _replaced, _table, _empty, _rule, _detail, _editor, _hints, _message);
        Register = new DecisionRegister([]);
        _empty.Text = "Reading the register…";
        _empty.Visible = true;
        _table.Visible = false;
        ShowHints();
        Reload();
    }

    internal DecisionRegister Register { get; private set; }

    internal IReadOnlyList<Decision> Shown => _shown;

    internal Decision? Selected => _table.Visible && _table.Value?.SelectedCell.Y is { } row && row >= 0 && row < _shown.Count
        ? _shown[row] : null;

    internal TableView Table => _table;

    internal TextField Search => _search;

    internal CheckBox AllTeams => _allTeams;

    internal CheckBox Replaced => _replaced;

    internal LogView Detail => _detail;

    internal TextField Editor => _editor;

    internal Label Empty => _empty;

    internal StatusBar Hints => _hints;

    internal MessageBar Message => _message;

    /// <summary>Every key the dialog binds, as F1 lists them and the hints name them.</summary>
    internal static IReadOnlyList<(string Keys, string Label)> Keys =>
    [
        ("Enter", "Open where it was said on GitHub"),
        ("e", "Edit the decision's wording"),
        ("k", "Make it a rule or a lean"),
        ("w", "Widen it to all teams, or narrow it again"),
        ("r", "Retire it, or bring it back"),
        ("Up/Down", "Select a decision"),
        ("Tab", "Move between the search, the checkboxes and the list"),
        ("Esc", "Close"),
    ];

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.F1)
        {
            ShowKeys();
            return true;
        }
        if (key == Key.Esc)
            return _editor.Visible ? StopEditing() : Close();
        return base.OnKeyDown(key);
    }

    /// <summary>Enter reaches a Dialog as Accept, from the table and the field alike.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => _editor.Visible ? Save() : Open();

    /// <summary>The first read puts focus on the list, where e, k, w and r work; later ones leave it where it is.</summary>
    internal void Loaded(DecisionRegister register)
    {
        var first = Register.Entries.Count == 0;
        Register = register;
        if (register.Failure is { Length: > 0 } failure)
        {
            _message.Show(failure, Schemes.Error);
            _empty.Text = "";
        }
        Show();
        if (first && _table.Visible)
            _table.SetFocus();
    }

    /// <summary>Narrows the list to what's asked for now, keeping the selection where it can.</summary>
    internal void Show()
    {
        var keep = _keep ?? Selected?.Id;
        _keep = null;
        _shown = Register.Shown(_search.Text, _allTeams.Value == CheckState.Checked, _replaced.Value == CheckState.Checked);
        if (Register.Entries.Count == 0)
        {
            if (Register.Failure is null)
                _empty.Text = DecisionRegister.Empty;
            _empty.Y = 2;
            _empty.Visible = true;
            _table.Visible = false;
        }
        else
        {
            _empty.Text = "(no matches)";
            _empty.Y = 4;
            _empty.Visible = _shown.Count == 0;
            _table.Visible = true;
            _table.Table = new EnumerableTableSource<Decision>(_shown, new Dictionary<string, Func<Decision, object>>(
                DecisionRegister.Columns.Select((column, i) =>
                    KeyValuePair.Create(column, (Func<Decision, object>)(entry => DecisionRegister.Row(entry)[i])))));
            var row = Math.Max(0, _shown.ToList().FindIndex(entry => entry.Id == keep));
            if (_shown.Count > 0)
                _table.Value = new TableSelection(new Point(0, row));
        }
        ShowSelected();
    }

    internal bool Run(string hint) => hint switch
    {
        OpenHint => Open(),
        EditHint => StartEditing(),
        KindHint => Correct(entry => DecisionChange.Kind(entry.Id)),
        WidenHint => Correct(entry => DecisionChange.Widen(entry.Id)),
        RetireHint => Correct(entry => DecisionChange.Retire(entry.Id)),
        SaveHint => Save(),
        CancelHint => StopEditing(),
        CloseHint => Close(),
        _ => false,
    };

    internal bool StartEditing()
    {
        if (Selected is not { } entry)
            return true;
        _editor.Text = entry.Text;
        _editor.Visible = true;
        _editor.SetFocus();
        _editor.MoveEnd();
        ShowHints();
        return true;
    }

    private void ShowSelected()
    {
        _detail.Lines = Selected is { } entry
            ? [.. Register.Detail(entry).Select(line => new LogLine(line, LogLineKind.Prose))]
            : [];
        ShowHints();
    }

    private void ShowHints()
    {
        if (_editor.Visible)
        {
            _hints.Show([new HintedCommand(SaveHint, "Enter save"), new HintedCommand(CancelHint, "Esc cancel")], Run);
            return;
        }
        var entry = Selected;
        var some = entry is not null;
        _hints.Show([
            new HintedCommand(OpenHint, "Enter open on GitHub", some),
            new HintedCommand(EditHint, "e edit", some),
            new HintedCommand(KindHint, "k rule/lean", some),
            new HintedCommand(WidenHint, entry is { Everyone: true } ? "w this team only" : "w all teams", some),
            new HintedCommand(RetireHint, entry is { Retired: not null } ? "r bring back" : "r retire", some),
            new HintedCommand(CloseHint, "Esc close"),
        ], Run);
    }

    private bool Browsing(Key key)
    {
        if (key == Key.Enter)
            return Open();
        if (key == Key.E)
            return StartEditing();
        if (key == Key.K)
            return Run(KindHint);
        if (key == Key.W)
            return Run(WidenHint);
        if (key == Key.R)
            return Run(RetireHint);
        return false;
    }

    /// <summary>Typing never leaves the search: the arrows move the selection, and Enter opens it.</summary>
    private bool Searching(Key key)
    {
        if (key == Key.Enter)
            return Open();
        if (key == Key.CursorUp || key == Key.CursorDown || key == Key.PageUp || key == Key.PageDown)
            return _table.NewKeyDownEvent(key);
        return false;
    }

    private bool Editing(Key key)
    {
        if (key == Key.Enter || key == Key.Enter.WithCtrl)
            return Save();
        if (key == Key.Esc)
            return StopEditing();
        return false;
    }

    private bool Open()
    {
        if (Selected is { Link.Length: > 0 } entry)
            _openUrl(entry.Link);
        return true;
    }

    private bool Save()
    {
        if (Selected is not { } entry)
            return StopEditing();
        var wording = _editor.Text.Trim();
        if (wording.Length == 0)
        {
            _message.Show("A decision can't be empty", Schemes.Error);
            return true;
        }
        StopEditing();
        return wording == entry.Text || Correct(_ => DecisionChange.Edit(entry.Id, wording));
    }

    private bool StopEditing()
    {
        _editor.Visible = false;
        _table.SetFocus();
        ShowHints();
        return true;
    }

    /// <summary>Makes the change and reads the register again, keeping the entry selected, whether it's shown now or not.</summary>
    private bool Correct(Func<Decision, DecisionChange> change)
    {
        if (Selected is not { } entry)
            return true;
        _message.Clear();
        _change(change(entry)).ContinueWith(done =>
        {
            var failure = done.Status == TaskStatus.RanToCompletion ? done.Result : "couldn't correct the register";
            OnUi(() =>
            {
                if (failure is { Length: > 0 })
                    _message.Show(failure, Schemes.Error);
                _keep = entry.Id;
                Reload();
            });
        }, TaskContinuationOptions.ExecuteSynchronously);
        return true;
    }

    private void Reload() =>
        _read().ContinueWith(read => OnUi(() => Loaded(read.Status == TaskStatus.RanToCompletion
            ? DecisionRegister.Of(read.Result) : new DecisionRegister([], "couldn't read the register"))),
            TaskContinuationOptions.ExecuteSynchronously);

    private void ShowKeys()
    {
        var sheet = new KeySheet(null, new KeyColumn(Title, [new KeyGroup(null, [.. Keys.Select(key => new KeyRow(key.Keys, key.Label))])]));
        if (App is { } app)
            KeysSheet.Show(app, sheet);
    }

    private void OnUi(Action action)
    {
        if (App is { } app)
            app.Invoke(action);
        else
            action();
    }

    private bool Close()
    {
        RequestStop();
        return true;
    }

    public static void Show(IApplication app, string team, Func<Task<Reading>> read, Func<DecisionChange, Task<string?>> change, Action<string> openUrl)
    {
        using var dialog = new DecisionsDialog(team, read, change, openUrl);
        app.Run(dialog);
    }
}
