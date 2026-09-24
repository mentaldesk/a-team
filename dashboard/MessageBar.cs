using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>The window's line for what just happened, and at its right end the state the area is in: no rows
/// at all while there's neither.</summary>
public sealed class MessageBar : Label
{
    private const int Gap = 1;
    private int _width;

    public MessageBar()
    {
        X = 0;
        Width = Dim.Fill();
        Height = Dim.Func(_ => Lines, this);
        CanFocus = false;
        SubViewLayout += (_, _) => Fit();
    }

    public int Lines { get; private set; }

    /// <summary>What just happened, on the left.</summary>
    public string Says { get; private set; } = "";

    /// <summary>The state the area is in, on the right.</summary>
    public string Status { get; private set; } = "";

    public void Show(string message, Schemes scheme)
    {
        Says = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";
        SchemeName = SchemeManager.SchemesToSchemeName(scheme);
        Lay();
    }

    public void ShowStatus(string status)
    {
        Status = status;
        Lay();
    }

    public void Clear()
    {
        Says = "";
        Lay();
    }

    /// <summary>The row as it's drawn: the message, then the status against the right edge. A message with no
    /// room left for it is elided rather than allowed to push the status off.</summary>
    internal static string Line(string message, string status, int width)
    {
        if (status.Length == 0)
            return message;
        if (width <= status.Length)
            return status;
        var room = width - status.Length - Gap;
        var left = message.Length <= room
            ? message
            : room <= 1 ? "" : string.Concat(message.AsSpan(0, room - 1), "…");
        return left.PadRight(width - status.Length) + status;
    }

    private void Fit()
    {
        if (_width == Viewport.Width)
            return;
        _width = Viewport.Width;
        Lay();
    }

    private void Lay()
    {
        Text = Line(Says, Status, _width);
        Lines = Says.Length == 0 && Status.Length == 0 ? 0 : 1;
    }
}
