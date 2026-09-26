using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard;

/// <summary>One drawn row of a log line: the text alone, and whether it's the row the icon goes beside.</summary>
public readonly record struct LogRow(string Text, LogLineKind Kind, bool Wrapped = false);

/// <summary>Word-wrapped lines that follow the end until the user scrolls up, or an elided tail that never wraps.</summary>
public sealed class LogView : View
{
    private const int ErrorIndent = 2;

    private IReadOnlyList<LogLine> _lines = [];
    private IconStyle _icons = IconStyle.Auto;
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

    /// <summary>Whether new lines pull the view to the end, as a session log's do. A body read once starts at the
    /// top instead.</summary>
    public bool Following
    {
        get => _following;
        init => _following = value;
    }

    internal int Top => _top;

    public bool Expanded
    {
        get => _expanded;
        init => _expanded = value;
    }

    /// <summary>One row per line, elided in the middle rather than wrapped, for a tail nobody can scroll.</summary>
    public bool Elides { get; init; }

    /// <summary>Draws the rows' icons from the vocabulary the reviewer picked.</summary>
    public void ShowIcons(IconStyle style)
    {
        if (_icons == style)
            return;
        _icons = style;
        SetNeedsDraw();
    }

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
        if (Viewport.Height > 0)
            _maxTop = Math.Max(0, Rows(Math.Max(1, Viewport.Width)).Count - Viewport.Height);
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
            var line = _top + row < rows.Count ? rows[_top + row] : new LogRow("", LogLineKind.Prose);
            SetAttribute(AttributeFor(line.Kind));
            AddStr(0, row, Drawn(line, width));
        }
        return true;
    }

    /// <summary>The row as it reaches the screen: its icon in the field the text is budgeted around, then the text.</summary>
    internal string Drawn(LogRow row, int width)
    {
        var lead = Lead(row.Kind);
        var icon = row.Wrapped ? null : Icons.For(row.Kind);
        var prefix = new string(' ', lead - (icon is null ? 0 : Icons.Width))
            + (icon is { } drawn ? Icons.Field(drawn, _icons) : "");
        return prefix + row.Text.PadRight(Math.Max(0, width - lead));
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

    /// <summary>The cells a row spends before its text: its icon's field, and an error's indent so it hangs under the call it came from.</summary>
    internal static int Lead(LogLineKind kind) => Icons.For(kind) is null
        ? 0
        : Icons.Width + (kind == LogLineKind.ToolError ? ErrorIndent : 0);

    internal List<LogRow> Rows(int width) => Elides
        ? [.. _lines.Select(line => new LogRow(Elide(line.Text, Room(width, line.Kind)), line.Kind))]
        : Wrap(_expanded ? _lines : Collapse(_lines, width), width);

    /// <summary>The cells a line of this kind has left for its text, never fewer than one.</summary>
    private static int Room(int width, LogLineKind kind) => Math.Max(1, width - Lead(kind));

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
    internal static int Anchor(IReadOnlyList<LogRow> before, IReadOnlyList<LogRow> after, int top)
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
            rows.Add(lines[i] with { Text = Fold(lines[i].Text, i - first, Room(width, LogLineKind.ToolCall)) });
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

    internal static List<LogRow> Wrap(IReadOnlyList<LogLine> lines, int width)
    {
        var rows = new List<LogRow>(lines.Count);
        foreach (var line in lines)
        {
            var room = Room(width, line.Kind);
            if (line.Text.Length <= room)
            {
                rows.Add(new LogRow(line.Text, line.Kind));
                continue;
            }
            var rest = line.Text;
            var wrapped = false;
            while (rest.Length > room)
            {
                var cut = rest.LastIndexOf(' ', room - 1);
                if (cut <= 0)
                    cut = room;
                rows.Add(new LogRow(rest[..cut], line.Kind, wrapped));
                rest = rest[cut..].TrimStart();
                wrapped = true;
            }
            rows.Add(new LogRow(rest, line.Kind, wrapped));
        }
        return rows;
    }
}
