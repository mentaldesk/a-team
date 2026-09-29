using System.Collections.ObjectModel;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>Several names picked from a list narrowed by typing, where typing a name the list hasn't got adds it.</summary>
public sealed class Picker : Dialog
{
    private const string MarkHint = "mark";
    private const string DoneHint = "done";
    private const string CancelHint = "cancel";

    private readonly string _unlisted;
    private readonly List<string> _chosen;
    private readonly List<string> _kept;
    private readonly List<string> _listed = [];
    private readonly List<string> _shown = [];
    private readonly TextField _filter;
    private readonly ListView _list;
    private readonly StatusBar _hints = new();
    private readonly MessageBar _message = new();
    private bool _closed;

    /// <summary><paramref name="unlisted"/> follows a chosen name the list doesn't offer, like <c>(not installed)</c>.</summary>
    public Picker(string title, IReadOnlyList<string> chosen, string unlisted, string message)
    {
        _unlisted = unlisted;
        _chosen = [.. chosen];
        _kept = [.. chosen];

        Title = title;
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
        _filter.KeyDown += (_, key) => key.Handled = key == Key.Space && Toggle();
        _list = new ListView
        {
            X = Pos.Absolute(1),
            Y = Pos.Bottom(_filter),
            Width = Dim.Fill(1),
            Height = Dim.Func(_ => Math.Max(0, Viewport.Height - 2 - _message.Lines), this),
            ShowMarks = true,
            MarkMultiple = true,
        };
        _list.KeyDown += (_, key) => key.Handled = key == Key.Space && Toggle();

        _hints.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - 1 - _message.Lines), this);
        _hints.Show("", [new HintedCommand(MarkHint, "Space mark"), new HintedCommand(DoneHint, "Enter done"),
            new HintedCommand(CancelHint, "Esc cancel")], Run);
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);
        Add(_filter, _list, _hints, _message);

        Narrow();
        Say(message, Schemes.Base);
        _filter.SetFocus();
    }

    /// <summary>The names picked, in the order they were chosen, or null where it was cancelled.</summary>
    internal IReadOnlyList<string>? Picked { get; private set; }

    internal TextField Filter => _filter;

    internal ListView List => _list;

    internal StatusBar Hints => _hints;

    internal MessageBar Message => _message;

    /// <summary>The rows as the list shows them, each with whether it's marked.</summary>
    internal IReadOnlyList<(string Row, bool Marked)> Rows =>
        [.. _shown.Select((name, index) => (RowOf(name), _list.Source?.IsMarked(index) == true))];

    /// <summary>Fills the list with what was found, keeping every mark, and says where it looked.</summary>
    internal void ShowChoices(IReadOnlyList<string> listed, string message, Schemes scheme = Schemes.Base)
    {
        if (_closed)
            return;
        Harvest();
        _listed.Clear();
        _listed.AddRange(listed.Distinct());
        Narrow();
        Say(message, scheme);
    }

    /// <summary>Enter reaches a Dialog as Accept, from the filter or the list alike, and never as a key.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => Done();

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
            return Close(null);
        if (key == Key.CursorUp)
            return MoveTo((_list.Value ?? 0) - 1);
        if (key == Key.CursorDown)
            return MoveTo((_list.Value ?? -1) + 1);
        if (key == Key.Tab)
        {
            (_filter.HasFocus ? (View)_list : _filter).SetFocus();
            return true;
        }
        return base.OnKeyDown(key);
    }

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
        Harvest();
        var typed = _filter.Text.Trim();
        if (typed.Length > 0 && _shown.Count == 0 && !_chosen.Contains(typed))
            _chosen.Add(typed);
        return Close([.. _chosen]);
    }

    /// <summary>Runs the picker, and returns what was picked, or null where it was cancelled.</summary>
    public static IReadOnlyList<string>? Show(IApplication app, Picker picker)
    {
        app.Run(picker);
        return picker.Picked;
    }

    private bool Close(IReadOnlyList<string>? picked)
    {
        _closed = true;
        Picked = picked;
        RequestStop();
        return true;
    }

    private bool Run(string hint) => hint switch
    {
        MarkHint => Toggle(),
        DoneHint => Done(),
        _ => Close(null),
    };

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

    /// <summary>The chosen names the list hasn't got first, then the list, narrowed to what the filter holds.</summary>
    private void Narrow()
    {
        var filter = _filter.Text.Trim();
        _shown.Clear();
        _shown.AddRange(_kept.Where(name => !_listed.Contains(name)).Concat(_listed)
            .Where(name => name.Contains(filter, StringComparison.OrdinalIgnoreCase)));
        _list.SetSource(new ObservableCollection<string>(_shown.Select(RowOf)));
        for (var index = 0; index < _shown.Count; index++)
            _list.Source?.SetMark(index, _chosen.Contains(_shown[index]));
        _list.Value = _shown.Count == 0 ? null : 0;
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
