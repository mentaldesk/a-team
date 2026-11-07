using System.Text;
using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Fields;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>Which of the Reviewer's <b>Worth considering</b> points you want fixed, and a note, as one comment on the PR.</summary>
public sealed class AskFixDialog : DashboardDialog
{
    private const string AskId = "askFix.ask";
    private const int Wide = 72;
    private const int Glyph = 2;

    private readonly IReadOnlyList<string> _points;
    private readonly List<CheckBox> _boxes = [];
    private readonly View _list;
    private readonly PlaceholderField _note;
    private int _wrappedTo;

    public AskFixDialog(int number, IReadOnlyList<string> points)
        : base($"Ask the Dev to fix #{number}")
    {
        _points = points;
        Width = Dim.Func(_ => Math.Min(Wide, SuperView?.Viewport.Width ?? App?.Screen.Width ?? Wide), this);
        Height = Dim.Func(_ => Math.Min(ListRows() + 4 + Foot.Rows + Alert.Lines + GetAdornmentsThickness().Vertical,
            SuperView?.Viewport.Height ?? App?.Screen.Height ?? int.MaxValue), this);
        var heading = new Label { Text = "Worth considering", X = 1, Y = 0 };
        _list = new View
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = Dim.Func(_ => Math.Max(1, Viewport.Height - 4 - Foot.Rows - Alert.Lines), this),
            CanFocus = true,
            TabStop = TabBehavior.TabStop,
        };
        _list.VerticalScrollBar.VisibilityMode = ScrollBarVisibilityMode.Auto;
        foreach (var point in points)
            _boxes.Add(Box(point));
        var label = new Label { Text = "Note:", X = 1, Y = Pos.Bottom(_list) + 1 };
        _note = new PlaceholderField { X = 7, Y = Pos.Top(label), Width = Dim.Fill(1) };
        Bind(AskId, Key.Enter.WithCtrl, "Ask", Ask, () => Ticked.Count > 0);
        Register(CancelId, "Cancel", RequestStop);
        AddButton(ButtonKind.Primary, AskId);
        AddButton(ButtonKind.Secondary, CancelId);
        Grey(AskId, false);
        Foot.Y = Pos.Bottom(label) + 1;
        Alert.Y = Pos.Bottom(Foot);
        Add(heading, _list, label, _note, Foot);
        Also("Space", "Tick or untick the point");
        Also("Tab/Shift+Tab", "Next or previous");
        SubViewsLaidOut += (_, _) => Wrap();
        _boxes[0].SetFocus();
    }

    /// <summary>The comment to post, or null where the dialog was cancelled.</summary>
    internal string? Chosen { get; private set; }

    internal IReadOnlyList<CheckBox> Boxes => _boxes;

    internal PlaceholderField Note => _note;

    internal IReadOnlyList<string> Ticked =>
        [.. _points.Where((_, i) => _boxes[i].Value == CheckState.Checked)];

    /// <summary>Opens the dialog, and returns the comment to post, or null where it was cancelled.</summary>
    public static string? Show(IApplication app, WaitingItem item, IReadOnlyList<string> points)
    {
        using var dialog = new AskFixDialog(item.Number, points);
        DialogHost.Run(app, dialog);
        return dialog.Chosen;
    }

    private CheckBox Box(string point)
    {
        var box = new CheckBox
        {
            Text = OneLine(point),
            X = 0,
            Width = Dim.Fill(),
            HotKeySpecifier = (Rune)0xffff,
            Value = CheckState.UnChecked,
        };
        box.ValueChanged += (_, _) => Grey(AskId, Ticked.Count > 0);
        box.HasFocusChanged += (_, e) =>
        {
            if (e.NewValue)
                ScrollTo(box);
        };
        _list.Add(box);
        return box;
    }

    private static string OneLine(string point) =>
        string.Join(' ', point.Split('\n', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Rows each point takes across the list as wide as it is now, wrapped at its spaces.</summary>
    private int ListRows() => _points.Sum(point => Lines(OneLine(point), TextWidth()).Count);

    private int TextWidth() => Math.Max(1, (Frame.Width > 0 ? Frame.Width : Wide) - GetAdornmentsThickness().Horizontal - 2 - Glyph - 2);

    private void Wrap()
    {
        var width = TextWidth();
        if (width == _wrappedTo)
            return;
        _wrappedTo = width;
        var y = 0;
        for (var i = 0; i < _boxes.Count; i++)
        {
            var lines = Lines(OneLine(_points[i]), width);
            _boxes[i].Text = string.Join("\n  ", lines);
            _boxes[i].Y = y;
            _boxes[i].Height = lines.Count;
            y += lines.Count;
        }
        _list.SetContentSize(new(width + Glyph, y));
    }

    private void ScrollTo(CheckBox box)
    {
        var top = box.Frame.Y;
        var bottom = box.Frame.Bottom;
        var view = _list.Viewport;
        if (top < view.Y)
            _list.Viewport = view with { Y = top };
        else if (bottom > view.Bottom)
            _list.Viewport = view with { Y = Math.Min(top, bottom - view.Height) };
    }

    internal static IReadOnlyList<string> Lines(string text, int width)
    {
        List<string> lines = [];
        var line = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var rest = word;
            if (line.Length > 0 && line.Length + 1 + rest.Length > width)
            {
                lines.Add(line.ToString());
                line.Clear();
            }
            while (rest.Length > width)
            {
                lines.Add(rest[..width]);
                rest = rest[width..];
            }
            if (line.Length > 0)
                line.Append(' ');
            line.Append(rest);
        }
        if (line.Length > 0 || lines.Count == 0)
            lines.Add(line.ToString());
        return lines;
    }

    private void Ask()
    {
        if (Ticked is { Count: > 0 } ticked)
        {
            Chosen = Considered.Request(ticked, _note.Text);
            RequestStop();
        }
    }
}
