using MentalDesk.Tui.Chrome;
using MentalDesk.Tui.Dialogs;
using System.Collections.ObjectModel;
using MentalDesk.Tui.Filtering;
using MentalDesk.Tui.Loading;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>Several names picked from a list narrowed by typing, where typing a name the list hasn't got adds it.</summary>
public sealed class Picker : DashboardDialog
{
    private const string DoneId = "picker.done";
    private const string RetryId = "picker.retry";

    private readonly string _unlisted;
    private readonly List<string> _chosen;
    private readonly List<string> _kept;
    private readonly List<string> _listed = [];
    private readonly List<string> _shown = [];
    private readonly TextField _filter;
    private readonly ListView _list;
    private readonly Label _noMatches;
    private readonly MessageBar _message = new();
    private readonly LoadStateView? _state;
    private readonly string? _loading;
    private Action? _retry;
    private bool _closed;

    /// <summary><paramref name="unlisted"/> follows a chosen name the list doesn't offer, like <c>(not installed)</c>.
    /// With <paramref name="loading"/>, it opens before its rows are in, saying so in the list until
    /// <see cref="ShowChoices"/> or <see cref="ShowFailed"/>.</summary>
    public Picker(string title, IReadOnlyList<string> chosen, string unlisted, string? loading = null)
        : base(title)
    {
        _unlisted = unlisted;
        _loading = loading;
        _chosen = [.. chosen];
        _kept = [.. chosen];

        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _filter = new TextField { X = Pos.Absolute(1), Y = 0, Width = Dim.Fill(1) };
        _filter.ValueChanged += (_, _) =>
        {
            Harvest();
            Narrow();
        };
        _filter.KeyBindings.Remove(Key.Home);
        _filter.KeyBindings.Remove(Key.End);
        _list = new ListView { ShowMarks = true, MarkMultiple = true };
        View rows = _list;
        if (loading is not null)
        {
            _list.Width = Dim.Fill();
            _list.Height = Dim.Fill();
            rows = _state = new LoadStateView(_list, loading);
        }
        rows.X = Pos.Absolute(1);
        rows.Y = Pos.Bottom(_filter);
        rows.Width = Dim.Fill(1);
        rows.Height = Dim.Func(_ => Math.Max(0, Viewport.Height - 2 - Foot.Rows - _message.Lines), this);
        _noMatches = new Label { X = Pos.Absolute(1), Y = Pos.Bottom(_filter), Text = "(no matches)", Enabled = false, Visible = false };

        Bind(DoneId, Key.Enter, "Done", () => Done());
        Bind("picker.mark", Key.Space, "Mark or unmark", () => Toggle());
        Bind("picker.up", Key.CursorUp, "Previous", () => MoveTo((_list.Value ?? 0) - 1));
        Bind("picker.down", Key.CursorDown, "Next", () => MoveTo((_list.Value ?? -1) + 1));
        Bind("picker.switch", Key.Tab, "Filter or list", () => (_filter.HasFocus ? (View)_list : _filter).SetFocus());
        Bind(RetryId, Key.R, "Retry", () => _retry?.Invoke(), () => State == LoadState.Failed);
        Register(CancelId, "Cancel", () => Close(null));
        AddButton(ButtonKind.Primary, DoneId);
        AddButton(ButtonKind.Secondary, CancelId);
        Foot.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - Foot.Rows - _message.Lines), this);
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);
        Add(_filter, rows, _noMatches, Foot, _message);

        Narrow();
        _filter.SetFocus();
    }

    /// <summary>The names picked, in the order they were chosen, or null where it was cancelled.</summary>
    internal IReadOnlyList<string>? Picked { get; private set; }

    internal TextField Filter => _filter;

    internal ListView List => _list;

    internal bool NoMatches => _noMatches.Visible;

    internal MessageBar Message => _message;

    internal LoadState State => _state?.State ?? LoadState.Loaded;

    /// <summary>What the list says in place of its rows, while it has none to show.</summary>
    internal string ListSays => State == LoadState.Loading ? _loading! : _state?.Says ?? "";

    /// <summary>The rows as the list shows them, each with whether it's marked.</summary>
    internal IReadOnlyList<(string Row, bool Marked)> Rows =>
        [.. _shown.Select((name, index) => (RowOf(name), _list.Source?.IsMarked(index) == true))];

    /// <summary>Fills the list with what was found, keeping every mark, and says where it looked.</summary>
    internal void ShowChoices(IReadOnlyList<string> listed, string message)
    {
        if (_closed)
            return;
        Harvest();
        _listed.Clear();
        _listed.AddRange(listed.Distinct());
        _state?.ShowContent();
        Narrow();
        Say(message, Schemes.Base);
    }

    /// <summary>Loads the list again, keeping every mark.</summary>
    internal void ShowLoading()
    {
        if (_closed)
            return;
        Harvest();
        _retry = null;
        RequireState().ShowLoading();
        Narrow();
        Say("", Schemes.Base);
    }

    /// <summary>Says in the list why it couldn't be read, with <paramref name="retry"/> on R under it.</summary>
    internal void ShowFailed(string message, Action retry)
    {
        if (_closed)
            return;
        Harvest();
        _retry = retry;
        var step = new Hint(RetryId, "retry").Resolve(Commands, Keys);
        RequireState().ShowFailed(message, (step?.Text ?? "Retry", retry));
        Narrow();
        Say("", Schemes.Base);
    }

    /// <summary>Enter reaches a Dialog as Accept, from the filter or the list alike, and never as a key.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => Done();

    /// <summary>Marks the selected row, or unmarks it.</summary>
    internal bool Toggle()
    {
        if (_list.Value is { } index && index >= 0 && index < _shown.Count && _list.Source is { } source)
        {
            source.SetMark(index, !source.IsMarked(index));
            _list.SetNeedsDraw();
        }
        return true;
    }

    /// <summary>Keeps what's marked, and a name typed that the list hasn't got, and closes.</summary>
    internal bool Done()
    {
        if (State != LoadState.Loaded)
            return true;
        Harvest();
        var typed = _filter.Text.Trim();
        if (typed.Length > 0 && _shown.Count == 0 && !_chosen.Contains(typed))
            _chosen.Add(typed);
        return Close([.. _chosen]);
    }

    /// <summary>Runs the picker, and returns what was picked, or null where it was cancelled.</summary>
    public static IReadOnlyList<string>? Show(IApplication app, Picker picker)
    {
        DialogHost.Run(app, picker);
        return picker.Picked;
    }

    private LoadStateView RequireState() =>
        _state ?? throw new InvalidOperationException("Only a picker opened with a loading message loads its rows.");

    private bool Close(IReadOnlyList<string>? picked)
    {
        _closed = true;
        Picked = picked;
        RequestStop();
        return true;
    }

    /// <summary>Takes the marks off the rows on show into what's chosen, before the rows change.</summary>
    private void Harvest()
    {
        if (_list.Source is not { } source)
            return;
        for (var index = 0; index < _shown.Count && index < source.Count; index++)
        {
            var name = _shown[index];
            var marked = source.IsMarked(index);
            if (marked && !_chosen.Contains(name))
                _chosen.Add(name);
            else if (!marked)
                _chosen.Remove(name);
        }
    }

    /// <summary>The chosen names the list hasn't got first, then the list, narrowed to what the filter holds, best match
    /// first.</summary>
    private void Narrow()
    {
        _shown.Clear();
        if (State == LoadState.Loaded)
            _shown.AddRange(ListFilter.Apply(_filter.Text.Trim(), [.. _kept.Where(name => !_listed.Contains(name)).Concat(_listed)], name => name));
        _list.SetSource(new ObservableCollection<string>(_shown.Select(RowOf)));
        for (var index = 0; index < _shown.Count; index++)
            _list.Source?.SetMark(index, _chosen.Contains(_shown[index]));
        _list.Value = _shown.Count == 0 ? null : 0;
        _noMatches.Visible = State == LoadState.Loaded && _shown.Count == 0 && _filter.Text.Trim().Length > 0;
        Grey(DoneId, State == LoadState.Loaded);
    }

    private string RowOf(string name) =>
        _unlisted.Length > 0 && !_listed.Contains(name) ? $"{name} {_unlisted}" : name;

    private bool MoveTo(int index)
    {
        if (_shown.Count > 0)
        {
            _list.Value = Math.Clamp(index, 0, _shown.Count - 1);
            _list.EnsureSelectedItemVisible();
        }
        return true;
    }

    private void Say(string message, Schemes scheme)
    {
        _message.Show(message, scheme);
        SetNeedsLayout();
        SetNeedsDraw();
    }
}
