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
        var rows = Wrap(_lines, width);
        _maxTop = Math.Max(0, rows.Count - height);
        _top = _following ? _maxTop : Math.Min(_top, _maxTop);
        var normal = GetAttributeForRole(VisualRole.Normal);
        var error = SchemeManager.GetScheme(Schemes.Error).GetAttributeForRole(VisualRole.Normal);
        var success = SchemeManager.TryGetScheme(LogSchemes.Success, out var scheme)
            ? scheme.GetAttributeForRole(VisualRole.Normal)
            : normal;
        for (var row = 0; row < height; row++)
        {
            var line = _top + row < rows.Count ? rows[_top + row] : new LogLine("", LogLineKind.Prose);
            SetAttribute(line.Kind switch
            {
                LogLineKind.ToolError or LogLineKind.ResultError => error,
                LogLineKind.ResultOk => success,
                _ => normal,
            });
            AddStr(0, row, line.Text.PadRight(width));
        }
        return true;
    }

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
