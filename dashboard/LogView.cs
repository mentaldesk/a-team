using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard;

/// <summary>Word-wrapped lines that follow the end until the user scrolls up, or an elided tail that never wraps.</summary>
public sealed class LogView : View
{
    private IReadOnlyList<LogLine> _lines = [];
    private int _top;
    private int _maxTop;
    private bool _following = true;
    private bool _expanded;

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

    public bool Expanded
    {
        get => _expanded;
        init => _expanded = value;
    }

    /// <summary>One row per line, elided in the middle rather than wrapped, for a tail nobody can scroll.</summary>
    public bool Elides { get; init; }

    /// <summary>Shows every tool call again, or folds the runs back up, keeping the line you were reading.</summary>
    public void ToggleToolCalls()
    {
        var width = Math.Max(1, Viewport.Width);
        var before = Rows(width);
        _expanded = !_expanded;
        var after = Rows(width);
        _maxTop = Math.Max(0, after.Count - Viewport.Height);
        if (_following)
            SetNeedsDraw();
        else
            ScrollTo(Anchor(before, after, _top));
    }

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
        var rows = Rows(width);
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

    internal List<LogLine> Rows(int width) => Elides
        ? [.. _lines.Select(line => line with { Text = Elide(line.Text, width) })]
        : Wrap(_expanded ? _lines : Collapse(_lines, width), width);

    /// <summary>Drops the middle of a line too long to fit, so its head and its tail both survive in exactly <paramref name="width"/> cells.</summary>
    internal static string Elide(string text, int width)
    {
        if (width < 1)
            return "";
        if (text.Length <= width)
            return text;
        var head = width / 2;
        var tail = width - 1 - head;
        return text[..head] + "…" + text[^tail..];
    }

    /// <summary>The row in <paramref name="after"/> holding what row <paramref name="top"/> of <paramref name="before"/> held.</summary>
    internal static int Anchor(IReadOnlyList<LogLine> before, IReadOnlyList<LogLine> after, int top)
    {
        top = Math.Clamp(top, 0, before.Count);
        var prose = 0;
        for (var row = 0; row < top; row++)
            if (before[row].Kind != LogLineKind.ToolCall)
                prose++;
        var inRun = top < before.Count && before[top].Kind == LogLineKind.ToolCall;
        var seen = 0;
        for (var row = 0; row < after.Count; row++)
        {
            if (seen == prose && (inRun || after[row].Kind != LogLineKind.ToolCall))
                return row;
            if (after[row].Kind != LogLineKind.ToolCall)
                seen++;
        }
        return after.Count;
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
