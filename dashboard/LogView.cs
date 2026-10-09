using System.Drawing;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard;

/// <summary>One drawn row of a log line: the text alone, whether it's the row the icon goes beside, which line drawn
/// it's from, and where in that line it starts.</summary>
public readonly record struct LogRow(string Text, LogLineKind Kind, bool Wrapped = false, int Line = 0, int Start = 0);

public enum CaretMove { Left, Right, Up, Down, PageUp, PageDown, RowStart, RowEnd, Start, End }

/// <summary>Where a log view was: its lines, scroll, selection and whether tool calls were shown.</summary>
public sealed record LogPlace(IReadOnlyList<LogLine> Lines, int Top, bool Following, int Anchor, int Cursor, bool Expanded);

/// <summary>Word-wrapped lines that follow the end until the user scrolls up, or an elided tail that never wraps.</summary>
public sealed class LogView : View
{
    private const int ErrorIndent = 2;
    private const string WordEdges = "()[]{}<>\"'`.,;:!?*";

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
    private (int Line, int Offset)? _caret;
    private (int Line, int Offset)? _from;
    private int? _goal;
    private bool _showsCaret;

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
            _caret = _from = null;
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

    /// <summary>Draws the caret, putting it at the start of the top row in view if it has none yet.</summary>
    public bool ShowsCaret
    {
        get => _showsCaret;
        set
        {
            _showsCaret = value;
            if (value && Viewport.Width > 0)
                PlaceCaret(Rows(Viewport.Width));
            SetNeedsDraw();
        }
    }

    /// <summary>Whether the mouse moves the caret: a press puts it there, a drag selects and a double-click takes a word.</summary>
    public bool SelectsText { get; init; }

    public event EventHandler? SelectionChanged;

    private void PlaceCaret(IReadOnlyList<LogRow> rows)
    {
        if (_caret is null && rows.Count > 0 && rows[Math.Min(_top, rows.Count - 1)] is var top)
            _caret = (top.Line, top.Start);
    }

    /// <summary>The caret's line among those drawn, and its offset in that line's text.</summary>
    public (int Line, int Offset)? Caret => _caret;

    public void DropCaret()
    {
        _caret = _from = null;
        _goal = null;
        ShowsCaret = false;
    }

    /// <summary>Scrolls to keep the caret in view; <paramref name="extend"/> selects from where it was.</summary>
    public void MoveCaret(CaretMove move, bool extend)
    {
        var rows = Rows(Math.Max(1, Viewport.Width));
        PlaceCaret(rows);
        if (_caret is not { } caret || rows.Count == 0)
            return;
        var at = CaretRow(rows, caret);
        var row = rows[at];
        var column = Math.Min(caret.Offset - row.Start, row.Text.Length);
        var page = Math.Max(1, Viewport.Height - 1);
        int? rowsBy = move switch
        {
            CaretMove.Up => -1,
            CaretMove.Down => +1,
            CaretMove.PageUp => -page,
            CaretMove.PageDown => +page,
            _ => null,
        };
        if (rowsBy is { } by)
        {
            _goal ??= column;
            var target = rows[Math.Clamp(at + by, 0, rows.Count - 1)];
            if (move is CaretMove.PageUp or CaretMove.PageDown)
                ScrollTo(_top + by);
            _caret = (target.Line, target.Start + Math.Min(_goal.Value, target.Text.Length));
        }
        else
        {
            _goal = null;
            var last = rows[^1];
            _caret = move switch
            {
                CaretMove.Left when caret.Offset > 0 => (caret.Line, caret.Offset - 1),
                CaretMove.Left when caret.Line > 0 => (caret.Line - 1, LineEnd(rows, caret.Line - 1)),
                CaretMove.Right when caret.Offset < LineEnd(rows, caret.Line) => (caret.Line, caret.Offset + 1),
                CaretMove.Right when caret.Line < last.Line => (caret.Line + 1, 0),
                CaretMove.RowStart => (row.Line, row.Start),
                CaretMove.RowEnd => (row.Line, row.Start + row.Text.Length),
                CaretMove.Start => (0, 0),
                CaretMove.End => (last.Line, last.Start + last.Text.Length),
                _ => caret,
            };
        }
        _from = extend ? _from ?? caret : null;
        var now = CaretRow(rows, _caret.Value);
        if (now < _top)
            ScrollTo(now);
        else if (now >= _top + Viewport.Height)
            ScrollTo(now - Viewport.Height + 1);
        SetNeedsDraw();
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (!SelectsText || mouse.Position is not { } at)
            return false;
        var rows = Rows(Math.Max(1, Viewport.Width));
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked))
            return SelectWord(CaretAt(rows, at));
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            var dragging = mouse.Flags.HasFlag(MouseFlags.PositionReport);
            if (!dragging)
            {
                SetFocus();
                App?.Mouse.GrabMouse(this);
            }
            var caret = CaretAt(rows, at);
            _from = dragging ? _from ?? _caret : null;
            _caret = caret;
            _goal = null;
            if (at.Y < 0)
                ScrollTo(_top - 1);
            else if (at.Y >= Viewport.Height)
                ScrollTo(_top + 1);
            SetNeedsDraw();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonReleased) && App?.Mouse.IsGrabbed(this) == true)
            App.Mouse.UngrabMouse();
        return false;
    }

    /// <summary>The run of text around <paramref name="at"/> between spaces, without the punctuation around it.</summary>
    internal bool SelectWord((int Line, int Offset)? at)
    {
        if (at is not var (line, offset) || Shown() is var shown && line >= shown.Count)
            return false;
        var text = _lines[shown[line]].Text;
        if (offset >= text.Length || char.IsWhiteSpace(text[offset]))
            return false;
        var start = offset;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
            start--;
        var end = offset;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
            end++;
        while (start < end && WordEdges.Contains(text[start]))
            start++;
        while (end > start && WordEdges.Contains(text[end - 1]))
            end--;
        if (offset < start || offset >= end)
            return false;
        (_from, _caret, _goal) = ((line, start), (line, end), null);
        SetNeedsDraw();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    internal (int Line, int Offset)? CaretAt(IReadOnlyList<LogRow> rows, Point at)
    {
        if (rows.Count == 0)
            return null;
        var row = rows[Math.Clamp(_top + at.Y, 0, rows.Count - 1)];
        var columns = Math.Max(0, at.X - Lead(row.Kind));
        var column = 0;
        while (column < row.Text.Length && row.Text[..(column + 1)].GetColumns() <= columns)
            column++;
        return (row.Line, row.Start + column);
    }

    /// <summary>How many lines the selection quotes, if any.</summary>
    public int Marked => MarkedText().Count;

    public void Unmark()
    {
        _from = null;
        SetNeedsDraw();
    }

    /// <summary>The selected text, a line at a time; a selection ending at the start of a line leaves that line out.</summary>
    public IReadOnlyList<string> MarkedText()
    {
        if (Selected() is not var (start, end))
            return [];
        var shown = Shown();
        var lines = new List<string>();
        for (var line = start.Line; line <= end.Line; line++)
        {
            var text = _lines[shown[line]].Text;
            var from = line == start.Line ? Math.Min(start.Offset, text.Length) : 0;
            var to = line == end.Line ? Math.Min(end.Offset, text.Length) : text.Length;
            lines.Add(text[from..Math.Max(from, to)]);
        }
        if (lines.Count > 1 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private ((int Line, int Offset) Start, (int Line, int Offset) End)? Selected() =>
        _caret is { } caret && _from is { } from && caret != from
            ? from.CompareTo(caret) < 0 ? (from, caret) : (caret, from)
            : null;

    private static int CaretRow(IReadOnlyList<LogRow> rows, (int Line, int Offset) caret)
    {
        var at = 0;
        for (var i = 0; i < rows.Count && (rows[i].Line < caret.Line || rows[i].Line == caret.Line && rows[i].Start <= caret.Offset); i++)
            at = i;
        return at;
    }

    private static int LineEnd(IReadOnlyList<LogRow> rows, int line)
    {
        var last = rows.Last(row => row.Line == line);
        return last.Start + last.Text.Length;
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
        var (from, to) = _selects && _lines.Count > 0 ? Selection(Shown()) : (0, -1);
        var selected = Selected();
        for (var row = 0; row < height; row++)
        {
            var line = _top + row < rows.Count ? rows[_top + row] : new LogRow("", LogLineKind.Prose, Line: -1);
            SetAttribute(AttributeFor(line.Kind, line.Line >= from && line.Line <= to));
            AddStr(0, row, Drawn(line, width));
            if (ReadsMarkdown)
                DrawSpans(line, row);
            if (selected is var (start, end))
                DrawSelected(line, row, start, end, rows);
        }
        if (_showsCaret)
            PlaceCaret(rows);
        if (_showsCaret && _caret is { } caret && rows.Count > 0 && CaretRow(rows, caret) - _top is var at && at >= 0 && at < height)
            DrawCaret(rows[_top + at], at, caret);
        return true;
    }

    /// <summary>A line selected through to the next takes a cell past its end, as an editor shows the line break.</summary>
    private void DrawSelected(LogRow line, int row, (int Line, int Offset) start, (int Line, int Offset) end, IReadOnlyList<LogRow> rows)
    {
        if (line.Line < start.Line || line.Line > end.Line)
            return;
        var from = line.Line == start.Line ? Math.Clamp(start.Offset - line.Start, 0, line.Text.Length) : 0;
        var to = line.Line == end.Line ? Math.Clamp(end.Offset - line.Start, 0, line.Text.Length) : line.Text.Length;
        var lastRow = rows.Count == _top + row + 1 || rows[_top + row + 1].Line != line.Line;
        var text = line.Text[from..Math.Max(from, to)] + (line.Line < end.Line && lastRow ? " " : "");
        if (text.Length == 0)
            return;
        SetAttribute(AttributeFor(line.Kind, selected: true));
        AddStr(Lead(line.Kind) + line.Text[..from].GetColumns(), row, text);
    }

    private void DrawCaret(LogRow line, int row, (int Line, int Offset) caret)
    {
        var column = Math.Clamp(caret.Offset - line.Start, 0, line.Text.Length);
        var attribute = AttributeFor(line.Kind);
        SetAttribute(new Attribute(attribute.Background, attribute.Foreground));
        AddStr(Lead(line.Kind) + line.Text[..column].GetColumns(), row, column < line.Text.Length ? line.Text[column].ToString() : " ");
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
            var start = 0;
            var wrapped = false;
            while (rest.Length > room)
            {
                var cut = rest.LastIndexOf(' ', room - 1);
                if (cut <= 0)
                    cut = room;
                rows.Add(new LogRow(rest[..cut], line.Kind, wrapped, index, start));
                var next = rest[cut..].TrimStart();
                start += rest.Length - next.Length;
                rest = next;
                wrapped = true;
            }
            rows.Add(new LogRow(rest, line.Kind, wrapped, index, start));
        }
        return rows;
    }
}
