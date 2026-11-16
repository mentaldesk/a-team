using System.Collections.ObjectModel;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>Work's Priority Inbox down the right: a row per card of yours that holds up other work, ranked by what it
/// frees. With the Assistant open too, it and the Assistant are tabs of this one pane.</summary>
public sealed class InboxPane : FrameView
{
    private const string Name = "Priority Inbox";
    private const string Short = "Inbox";

    private readonly ListView _list = new() { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true };
    private readonly ObservableCollection<string> _lines = [];
    private readonly Tabs _tabs = new() { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true, Visible = false };
    private readonly View _listTab = new() { Title = Short, CanFocus = true, Width = Dim.Fill(), Height = Dim.Fill() };
    private readonly LineStyle? _border;
    private IReadOnlyList<InboxRow> _rows = [];

    public InboxPane()
    {
        CanFocus = true;
        SchemeName = SchemeManager.SchemesToSchemeName(Schemes.Base)!;
        _border = BorderStyle;
        _list.SetSource(_lines);
        _list.ValueChanged += (_, _) => SelectionChanged?.Invoke();
        _tabs.Add(_listTab, AssistantTab);
        Add(_list, _tabs);
        Title = Name;
    }

    /// <summary>Raised when the keyboard moves to another row.</summary>
    internal event Action? SelectionChanged;

    internal IReadOnlyList<InboxRow> Rows => _rows;

    internal IReadOnlyList<string> Lines => _lines;

    internal ListView List => _list;

    /// <summary>The tab the Assistant goes in while it shares the pane.</summary>
    internal View AssistantTab { get; } = new() { Title = "Assistant", CanFocus = true, Width = Dim.Fill(), Height = Dim.Fill() };

    internal InboxRow? Selected => _list.Value is { } at && at < _rows.Count ? _rows[at] : null;

    internal bool Tabbed => _tabs.Visible;

    internal bool InboxInFront => !Tabbed || _tabs.Value == _listTab;

    internal string TabTitle => _listTab.Title;

    /// <summary>Shows <paramref name="rows"/>, staying on the card it was on while you're in it, and otherwise on the top one.</summary>
    public void Show(IReadOnlyList<InboxRow> rows)
    {
        var was = _list.HasFocus ? Selected?.Item : null;
        _rows = rows;
        var lines = Inbox.Lines(rows);
        if (!_lines.SequenceEqual(lines))
        {
            _lines.Clear();
            foreach (var line in lines)
                _lines.Add(line);
        }
        var at = was is null ? 0 : Math.Max(0, rows.ToList().FindIndex(row => row.Item.Team == was.Team && row.Item.Number == was.Number));
        _list.Value = rows.Count == 0 ? null : at;
        Title = $"{Name} {rows.Count}";
        _listTab.Title = $"{Short} {rows.Count}";
    }

    /// <summary>Shares the pane with the Assistant as two tabs, or gives it the pane alone.</summary>
    public void ShareWithAssistant(bool share)
    {
        if (Tabbed == share)
            return;
        var focused = _list.HasFocus;
        _list.SuperView?.Remove(_list);
        (share ? _listTab : this).Add(_list);
        BorderStyle = share ? LineStyle.None : _border;
        _tabs.Visible = share;
        if (share)
            _tabs.Value = _listTab;
        if (focused)
            _list.SetFocus();
    }

    public void ShowList()
    {
        if (Tabbed)
            _tabs.Value = _listTab;
        _list.SetFocus();
    }

    public void ShowAssistant()
    {
        if (Tabbed)
            _tabs.Value = AssistantTab;
    }

    internal bool AssistantInFront => Tabbed && _tabs.Value == AssistantTab;
}
