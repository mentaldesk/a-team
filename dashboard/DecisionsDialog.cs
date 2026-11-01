using System.Drawing;
using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Links;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>What the team remembers you deciding, to read, search and correct: the selected team's entries and all
/// teams', newest confirmed first, with the selected one in full below.</summary>
public sealed class DecisionsDialog : DashboardDialog
{
    internal const string OpenId = "decisions.open";
    internal const string EditId = "decisions.edit";
    internal const string KindId = "decisions.kind";
    internal const string WidenId = "decisions.widen";
    internal const string RetireId = "decisions.retire";
    internal const string SaveId = "decisions.save";
    private const int DetailRows = 6;

    private readonly string _team;
    private readonly Func<Task<Reading>> _read;
    private readonly Func<DecisionChange, Task<string?>> _change;
    private readonly Func<string, LinkOutcome> _openUrl;
    private readonly TextField _search = new() { X = 8, Y = 0, Width = 28 };
    private readonly CheckBox _allTeams = new() { Text = "Include all-teams", Value = CheckState.Checked, Y = 0 };
    private readonly CheckBox _replaced = new() { Text = "Show replaced", Value = CheckState.UnChecked, Y = 0 };
    private readonly TableView _table;
    private readonly Label _empty = new() { X = 1, Y = 2, Width = Dim.Fill(1), Visible = false };
    private readonly Line _rule = new() { X = 0, Width = Dim.Fill() };
    private readonly LogView _detail = new() { X = 1, Width = Dim.Fill(1), Height = DetailRows, CanFocus = false };
    private readonly TextField _editor = new() { X = 1, Width = Dim.Fill(1), Visible = false };
    private readonly MessageBar _message = new();
    private IReadOnlyList<Decision> _shown = [];
    private int? _keep;

    public DecisionsDialog(string team, Func<Task<Reading>> read, Func<DecisionChange, Task<string?>> change, Func<string, LinkOutcome> openUrl)
        : base($"Decisions · {team}")
    {
        _team = team;
        _read = read;
        _change = change;
        _openUrl = openUrl;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        int FootRow() => Math.Max(0, Viewport.Height - Foot.Rows - _message.Lines);

        _allTeams.X = Pos.Right(_search) + 3;
        _replaced.X = Pos.Right(_allTeams) + 3;
        _table = new TableView
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Func(_ => Math.Max(1, FootRow() - 2 - DetailRows - 2), this),
            FullRowSelect = true,
            Style = { ShowVerticalCellLines = false, ShowVerticalHeaderLines = false, ShowHorizontalHeaderOverline = false,
                ShowHorizontalBottomLine = false, ExpandLastColumn = false },
        };
        _rule.Y = Pos.Bottom(_table);
        _detail.Y = Pos.Bottom(_rule);
        _editor.Y = Pos.Top(_detail);
        Foot.Y = Pos.Func(_ => FootRow(), this);
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);

        _search.ValueChanged += (_, _) => Show();
        _search.KeyDown += (_, key) => key.Handled = Searching(key);
        _search.HasFocusChanged += (_, _) => ShowHints();
        _allTeams.ValueChanged += (_, _) => Show();
        _replaced.ValueChanged += (_, _) => Show();
        _table.ValueChanged += (_, _) => ShowSelected();
        _editor.KeyDown += (_, key) => key.Handled = Editing(key);
        Add(new Label { X = 0, Y = 0, Text = "Search:" }, _search, _allTeams, _replaced, _table, _empty, _rule, _detail, _editor, Foot, _message);
        Bind(OpenId, Key.Enter, "Open on GitHub", () => Open(), () => !_editor.Visible && Selected is not null);
        Bind(EditId, Key.E, "Edit", () => StartEditing(), Browsing);
        Bind(KindId, Key.K, "Rule/lean", () => Correct(entry => DecisionChange.Kind(entry.Id)), Browsing);
        Bind(WidenId, Key.W, "All teams", () => Correct(entry => DecisionChange.Widen(entry.Id)), Browsing);
        Bind(RetireId, Key.R, "Retire", () => Correct(entry => DecisionChange.Retire(entry.Id)), Browsing);
        Register(SaveId, "Save", () => Save(), () => _editor.Visible);
        Also("Enter", "Save", () => _editor.Visible);
        Also("Up/Down", "Select a decision", () => !_editor.Visible);
        Also("Tab", "Move between the search, the checkboxes and the list", () => !_editor.Visible);
        Register(CancelId, "Close", () => { if (_editor.Visible) StopEditing(); else Close(); });
        foreach (var id in new[] { OpenId, EditId, KindId, WidenId, RetireId, SaveId })
            AddButton(id == SaveId || id == OpenId ? ButtonKind.Primary : ButtonKind.Secondary, id);
        AddButton(ButtonKind.Secondary, CancelId);
        Decisions = new DecisionRegister([]);
        _empty.Text = "Reading the register…";
        _empty.Visible = true;
        _table.Visible = false;
        ShowHints();
        Reload();
    }

    internal DecisionRegister Decisions { get; private set; }

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

    internal MessageBar Message => _message;

    /// <summary>The first read puts focus on the list, where e, k, w and r work; later ones leave it where it is.</summary>
    internal void Loaded(DecisionRegister register)
    {
        var first = Decisions.Entries.Count == 0;
        Decisions = register;
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
        _shown = Decisions.Shown(_search.Text, _allTeams.Value == CheckState.Checked, _replaced.Value == CheckState.Checked);
        if (Decisions.Entries.Count == 0)
        {
            if (Decisions.Failure is null)
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
            ? [.. Decisions.Detail(entry).Select(line => new LogLine(line, LogLineKind.Prose))]
            : [];
        ShowHints();
    }

    private void ShowHints()
    {
        var entry = Selected;
        Rename(WidenId, entry is { Everyone: true } ? "This team only" : "All teams");
        Rename(RetireId, entry is { Retired: not null } ? "Bring back" : "Retire");
        Rename(CancelId, _editor.Visible ? "Cancel" : "Close");
        ShowButtons();
    }

    /// <summary>The list's own keys, which typing in the search or the editor leaves alone.</summary>
    private bool Browsing() => !_editor.Visible && !_search.HasFocus && Selected is not null;

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
        if (Selected is not { Link.Length: > 0 } entry)
            return true;
        var (said, scheme) = Copying.Said(_openUrl(entry.Link));
        _message.Show(said, scheme);
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

    public static void Show(IApplication app, string team, Func<Task<Reading>> read, Func<DecisionChange, Task<string?>> change, Func<string, LinkOutcome> openUrl)
    {
        using var dialog = new DecisionsDialog(team, read, change, openUrl);
        DialogHost.Run(app, dialog);
    }
}
