using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard;

/// <summary>Word-wrapped lines that follow the end until the user scrolls up.</summary>
public sealed class LogView : View
{
    private IReadOnlyList<LogLine> _lines = [];
    private int _top;
    private int _maxTop;
    private bool _following = true;

    public LogView() => CanFocus = false;

    public IReadOnlyList<LogLine> Lines
    {
        get => _lines;
        set
        {
            _lines = value;
            SetNeedsDraw();
        }
    }

    public bool Following => _following;

    public void Page(int direction) => ScrollTo(_top + direction * Math.Max(1, Viewport.Height - 1));

    public void Home() => ScrollTo(0);

    public void End() => ScrollTo(int.MaxValue);

    private void ScrollTo(int top)
    {
        _top = Math.Clamp(top, 0, _maxTop);
        _following = _top >= _maxTop;
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Math.Max(1, Viewport.Width);
        var height = Viewport.Height;
        var rows = Wrap(Collapse(_lines, width), width);
        _maxTop = Math.Max(0, rows.Count - height);
        _top = _following ? _maxTop : Math.Min(_top, _maxTop);
        for (var row = 0; row < height; row++)
        {
            var line = _top + row < rows.Count ? rows[_top + row] : new LogLine("", LogLineKind.Prose);
            SetAttribute(AttributeFor(line.Kind));
            AddStr(0, row, line.Text.PadRight(width));
        }
        return true;
    }

    private Attribute AttributeFor(LogLineKind kind)
    {
        var style = LogStyle.For(kind);
        if (style.Scheme is null)
            return GetAttributeForRole(style.Role);
        return SchemeManager.TryGetScheme(style.Scheme, out var scheme)
            ? scheme.GetAttributeForRole(style.Role)
            : GetAttributeForRole(VisualRole.Normal);
    }

    /// <summary>Folds each run of consecutive tool calls into one clipped row, showing the latest call.</summary>
    internal static List<LogLine> Collapse(IReadOnlyList<LogLine> lines, int width)
    {
        var rows = new List<LogLine>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind != LogLineKind.ToolCall)
            {
                rows.Add(lines[i]);
                continue;
            }
            var first = i;
            while (i + 1 < lines.Count && lines[i + 1].Kind == LogLineKind.ToolCall)
                i++;
            rows.Add(lines[i] with { Text = Fold(lines[i].Text, i - first, width) });
        }
        return rows;
    }

    private static string Fold(string text, int folded, int width)
    {
        var suffix = folded > 0 ? $" (+{folded})" : "";
        var room = width - suffix.Length;
        return room < 1 ? Clip(suffix.TrimStart(), width) : Clip(text, room) + suffix;
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    internal static List<LogLine> Wrap(IReadOnlyList<LogLine> lines, int width)
    {
        var rows = new List<LogLine>(lines.Count);
        foreach (var line in lines)
        {
            if (line.Text.Length <= width)
            {
                rows.Add(line);
                continue;
            }
            var rest = line.Text;
            while (rest.Length > width)
            {
                var cut = rest.LastIndexOf(' ', width - 1);
                if (cut <= 0)
                    cut = width;
                rows.Add(line with { Text = rest[..cut] });
                rest = rest[cut..].TrimStart();
            }
            rows.Add(line with { Text = rest });
        }
        return rows;
    }
}
