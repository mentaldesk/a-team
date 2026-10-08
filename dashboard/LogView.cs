using System.Drawing;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard;

/// <summary>One drawn row of a log line: the text alone, whether it's the row the icon goes beside, and which line drawn it's from.</summary>
public readonly record struct LogRow(string Text, LogLineKind Kind, bool Wrapped = false, int Line = 0);

/// <summary>Where a log view was: its lines, scroll, selection and whether tool calls were shown.</summary>
public sealed record LogPlace(IReadOnlyList<LogLine> Lines, int Top, bool Following, int Anchor, int Cursor, bool Expanded);

/// <summary>Word-wrapped lines that follow the end until the user scrolls up, or an elided tail that never wraps.</summary>
public sealed class LogView : View
{
    private const int ErrorIndent = 2;

    private IReadOnlyList<LogLine> _lines = [];
    private IconStyle _icons = IconStyle.Auto;
    private int _top;
    private int _maxTop;
    private bool _following = true;
    private readonly bool _follows = true;
    private bool _expanded;
    private bool _scrolls;
    private bool _selects;
    private int _anchor;
    private int _cursor;
    private (int Anchor, int Cursor)? _marked;

    public LogView()
    {
        CanFocus = false;
        SubViewLayout += (_, _) => Fit();
        ViewportChanged += (_, e) => Dragged(e);
    }

    public IReadOnlyList<LogLine> Lines
    {
        get => _lines;
        set
        {
            if (_selects && !_following)
                Rebase(value);
            _lines = value;
            _marked = null;
            // Before anything scrolls: a viewport past the old content gets clamped back, and stops following.
            if (Viewport.Height > 0)
                Fit();
            SetNeedsDraw();
        }
    }

    /// <summary>Whether new lines pull the view to the end, as a session log's do. A body read once starts at the
    /// top instead.</summary>
    public bool Following
    {
        get => _following;
        init => _following = _follows = value;
    }

    /// <summary>The top row; set before the view is laid out, it's where the view opens.</summary>
    internal int Top
    {
        get => _top;
        set
        {
            _following = false;
            if (Viewport.Height > 0)
                ScrollTo(value);
            else
                _top = value;
        }
    }

    /// <summary>Whether text too long for the view gets a scroll bar beside it, as a body read at a sitting
    /// does.</summary>
    public bool Scrolls
    {
        get => _scrolls;
        init
        {
            _scrolls = value;
            if (value)
                VerticalScrollBar.VisibilityMode = ScrollBarVisibilityMode.Auto;
        }
    }

    public bool Expanded
    {
        get => _expanded;
        init => _expanded = value;
    }

    /// <summary>Colours the markdown inside prose rows too: list markers, inline code and bold.</summary>
    public bool ReadsMarkdown { get; init; }

    /// <summary>One row per line, elided in the middle rather than wrapped, for a tail nobody can scroll.</summary>
    public bool Elides { get; init; }

    /// <summary>Whether whole lines are highlighted for copying, starting at the tail.</summary>
    public bool Selects
    {
        get => _selects;
        set
        {
            _selects = value;
            if (value)
                _following = true;
            SetNeedsDraw();
        }
    }

    /// <summary>Draws the rows' icons from the vocabulary the reviewer picked.</summary>
    public void ShowIcons(IconStyle style)
    {
        if (_icons == style)
            return;
        _icons = style;
        SetNeedsDraw();
    }

    /// <summary>Shows every tool call again, or folds them back into the newest one, keeping the line you were reading.</summary>
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
        if (_selects && !_following && _lines.Count > 0)
            Reveal(Highlighted(Shown()));
    }

    public void Page(int direction)
    {
        var page = Math.Max(1, Viewport.Height - 1);
        if (!_selects || _lines.Count == 0)
        {
            ScrollTo(_top + direction * page);
            return;
        }
        var shown = Shown();
        var rows = Rows(Math.Max(1, Viewport.Width));
        var row = rows.FindIndex(row => row.Line == Highlighted(shown));
        var target = rows[Math.Clamp(row + direction * page, 0, rows.Count - 1)].Line;
        ScrollTo(Tail(rows) + direction * page);
        if (_following)
            return;
        _anchor = _cursor = shown[target];
        Reveal(target);
    }

    public void Step(int direction) => ScrollTo(_top + direction);

    public void Home()
    {
        ScrollTo(0);
        if (_selects && _lines.Count > 0 && !_following)
            _anchor = _cursor = Shown()[0];
    }

    public void MoveSelection(int step, bool extend)
    {
        if (!_selects || _lines.Count == 0)
            return;
        var shown = Shown();
        if (_following)
        {
            _anchor = _cursor = shown[^1];
            _top = Tail(Rows(Math.Max(1, Viewport.Width)));
        }
        var to = Math.Clamp(Highlighted(shown) + step, 0, shown.Count - 1);
        _cursor = shown[to];
        if (!extend)
            _anchor = _cursor;
        _following = false;
        Reveal(to);
    }

    public LogCopy CopySelection()
    {
        if (_lines.Count == 0)
            return LogCopy.Of([]);
        var shown = Shown();
        var (from, to) = Selection(shown);
        return LogCopy.Of([.. shown[from..(to + 1)].Select(source => _lines[source])]);
    }

    public LogCopy CopyAll() => LogCopy.Of(_lines);

    /// <summary>How many lines are marked for quoting, if any.</summary>
    public int Marked => _marked is (var anchor, var cursor) ? Math.Abs(cursor - anchor) + 1 : 0;

    /// <summary>Marks the top line in view, or moves the marked end <paramref name="step"/> lines.</summary>
    public void Mark(int step)
    {
        var rows = Rows(Math.Max(1, Viewport.Width));
        if (rows.Count == 0)
            return;
        if (_marked is (var anchor, var cursor))
            _marked = (anchor, Math.Clamp(cursor + step, 0, Shown().Count - 1));
        else
        {
            var top = rows[Math.Min(_top, rows.Count - 1)].Line;
            _marked = (top, top);
        }
        var end = _marked.Value.Cursor;
        var first = rows.FindIndex(row => row.Line == end);
        var last = rows.FindLastIndex(row => row.Line == end);
        if (first < _top)
            ScrollTo(first);
        else if (last >= _top + Viewport.Height)
            ScrollTo(Math.Min(first, last - Viewport.Height + 1));
        SetNeedsDraw();
    }

    public void Unmark()
    {
        _marked = null;
        SetNeedsDraw();
    }

    /// <summary>The marked lines' text as written, top to bottom.</summary>
    public IReadOnlyList<string> MarkedText()
    {
        if (_marked is not (var anchor, var cursor))
            return [];
        var shown = Shown();
        return [.. shown[Math.Min(anchor, cursor)..(Math.Max(anchor, cursor) + 1)].Select(line => _lines[line].Text)];
    }

    internal LogPlace Place => new(_lines, _top, _following, _anchor, _cursor, _expanded);

    /// <summary>Back where <paramref name="place"/> was, then on to <paramref name="lines"/> as if they'd arrived
    /// while you watched.</summary>
    internal void Restore(LogPlace place, IReadOnlyList<LogLine> lines)
    {
        _lines = place.Lines;
        (_top, _following, _anchor, _cursor, _expanded) = (place.Top, place.Following, place.Anchor, place.Cursor, place.Expanded);
        Lines = lines;
    }

    internal (int From, int To) Selection(IReadOnlyList<int> shown)
    {
        if (_following)
            return (shown.Count - 1, shown.Count - 1);
        var anchor = Position(shown, _anchor);
        var cursor = Position(shown, _cursor);
        return (Math.Min(anchor, cursor), Math.Max(anchor, cursor));
    }

    /// <summary>While following, the top row may not have been drawn yet.</summary>
    private int Tail(IReadOnlyList<LogRow> rows) => _following ? Math.Max(0, rows.Count - Viewport.Height) : _top;

    private int Highlighted(IReadOnlyList<int> shown) => _following ? shown.Count - 1 : Position(shown, _cursor);

    /// <summary>Where a line falls among those drawn: a folded tool call counts as the line drawn before it.</summary>
    private static int Position(IReadOnlyList<int> shown, int source)
    {
        var position = 0;
        for (var i = 0; i < shown.Count && shown[i] <= source; i++)
            position = i;
        return position;
    }

    /// <summary>Which of <see cref="Lines"/> are drawn, in order.</summary>
    internal List<int> Shown()
    {
        if (_expanded || Elides)
            return [.. Enumerable.Range(0, _lines.Count)];
        var newest = -1;
        for (var i = 0; i < _lines.Count; i++)
            if (_lines[i].Kind == LogLineKind.ToolCall)
                newest = i;
        return [.. Enumerable.Range(0, _lines.Count).Where(i => _lines[i].Kind != LogLineKind.ToolCall || i == newest)];
    }

    private void Reveal(int line)
    {
        var rows = Rows(Math.Max(1, Viewport.Width));
        _maxTop = Math.Max(0, rows.Count - Viewport.Height);
        var first = rows.FindIndex(row => row.Line == line);
        var last = rows.FindLastIndex(row => row.Line == line);
        if (first < _top)
            _top = first;
        else if (last >= _top + Viewport.Height)
            _top = Math.Min(first, last - Viewport.Height + 1);
        _top = Math.Clamp(_top, 0, _maxTop);
        SetNeedsDraw();
    }

    /// <summary>Keeps the selection on its lines when the oldest are trimmed; a different log goes back to the tail.</summary>
    private void Rebase(IReadOnlyList<LogLine> lines)
    {
        if (Dropped(_lines, lines) is not { } dropped)
        {
            _following = true;
            return;
        }
        _anchor = Math.Max(0, _anchor - dropped);
        _cursor = Math.Max(0, _cursor - dropped);
    }

    internal static int? Dropped(IReadOnlyList<LogLine> before, IReadOnlyList<LogLine> after)
    {
        for (var dropped = 0; dropped < before.Count; dropped++)
        {
            var kept = before.Count - dropped;
            if (kept > after.Count)
                continue;
            var same = true;
            for (var i = 0; i < kept && same; i++)
                same = before[dropped + i] == after[i];
            if (same)
                return dropped;
        }
        return null;
    }

    /// <summary>Stays at the end, as the view is laid out again, until it's scrolled.</summary>
    public void End()
    {
        ScrollTo(int.MaxValue);
        _following = true;
    }

    private void ScrollTo(int top)
    {
        if (Viewport.Height > 0)
            _maxTop = Math.Max(0, Rows(Math.Max(1, Viewport.Width)).Count - Viewport.Height);
        _top = Math.Clamp(top, 0, _maxTop);
        _following = _follows && _top >= _maxTop;
        if (_scrolls)
            Viewport = Viewport with { Y = _top };
        SetNeedsDraw();
    }

    /// <summary>The bar scrolls the viewport, not the rows, so where it lands becomes the top row.</summary>
    private void Dragged(DrawEventArgs e)
    {
        if (_scrolls && e.NewViewport.Y != e.OldViewport.Y && _top != Viewport.Y)
            ScrollTo(Viewport.Y);
    }

    private void Fit()
    {
        if (!_scrolls)
            return;
        // A second pass: the scroll bar takes a column off the width the first one wrapped to.
        for (var pass = 0; pass < 2 && GetContentSize() != Content(); pass++)
            SetContentSize(Content());
        ScrollTo(_following ? int.MaxValue : _top);
    }

    private Size Content() => Viewport.Size with
    {
        Height = Math.Max(Viewport.Height, Rows(Math.Max(1, Viewport.Width)).Count),
    };

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Math.Max(1, Viewport.Width);
        var height = Viewport.Height;
        var rows = Rows(width);
        _maxTop = Math.Max(0, rows.Count - height);
        _top = _following ? _maxTop : Math.Min(_top, _maxTop);
        var (from, to) = _selects && _lines.Count > 0 ? Selection(Shown())
            : _marked is (var anchor, var cursor) ? (Math.Min(anchor, cursor), Math.Max(anchor, cursor))
            : (0, -1);
        for (var row = 0; row < height; row++)
        {
            var line = _top + row < rows.Count ? rows[_top + row] : new LogRow("", LogLineKind.Prose, Line: -1);
            SetAttribute(AttributeFor(line.Kind, line.Line >= from && line.Line <= to));
            AddStr(0, row, Drawn(line, width));
            if (ReadsMarkdown)
                DrawSpans(line, row);
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

    private void DrawSpans(LogRow line, int row)
    {
        foreach (var span in Markdown.Spans(line))
        {
            SetAttribute(AttributeFor(span.Kind));
            AddStr(Lead(line.Kind) + line.Text[..span.Start].GetColumns(), row, line.Text.Substring(span.Start, span.Length));
        }
    }

    /// <summary>A highlighted row keeps its kind's colour, on the pane's focus background.</summary>
    private Attribute AttributeFor(LogLineKind kind, bool selected)
    {
        var attribute = AttributeFor(kind);
        return selected ? new Attribute(attribute.Foreground, GetAttributeForRole(VisualRole.Focus).Background) : attribute;
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
        ? [.. _lines.Select((line, i) => new LogRow(Elide(line.Text, Room(width, line.Kind)), line.Kind, Line: i))]
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

    /// <summary>Keeps the newest tool call in the log, on one clipped row counting every older call, and drops
    /// the rest.</summary>
    internal static List<LogLine> Collapse(IReadOnlyList<LogLine> lines, int width)
    {
        var newest = -1;
        var calls = 0;
        for (var i = 0; i < lines.Count; i++)
            if (lines[i].Kind == LogLineKind.ToolCall)
            {
                newest = i;
                calls++;
            }
        var rows = new List<LogLine>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
            if (lines[i].Kind != LogLineKind.ToolCall)
                rows.Add(lines[i]);
            else if (i == newest)
                rows.Add(lines[i] with { Text = Fold(lines[i].Text, calls - 1, Room(width, LogLineKind.ToolCall)) });
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
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var room = Room(width, line.Kind);
            if (line.Text.Length <= room)
            {
                rows.Add(new LogRow(line.Text, line.Kind, Line: index));
                continue;
            }
            var rest = line.Text;
            var wrapped = false;
            while (rest.Length > room)
            {
                var cut = rest.LastIndexOf(' ', room - 1);
                if (cut <= 0)
                    cut = room;
                rows.Add(new LogRow(rest[..cut], line.Kind, wrapped, index));
                rest = rest[cut..].TrimStart();
                wrapped = true;
            }
            rows.Add(new LogRow(rest, line.Kind, wrapped, index));
        }
        return rows;
    }
}
