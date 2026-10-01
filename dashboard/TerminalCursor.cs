using System.Globalization;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>Colours the terminal's own cursor with OSC 12 and restores it with OSC 112, copied from the style guide's
/// <c>MentalDesk.Tui</c>.</summary>
public sealed class TerminalCursor(Action<string> write)
{
    public Color? Asked { get; private set; }

    public static TerminalCursor ForConsole() => new(sequence =>
    {
        Console.Out.Write(sequence);
        Console.Out.Flush();
    });

    public void Colour(Color colour)
    {
        write($"\x1b]12;{Hex(colour)}\x07");
        Asked = colour;
    }

    public void Restore()
    {
        if (Asked is null) return;
        write("\x1b]112\x07");
        Asked = null;
    }

    public static string Hex(Color colour) =>
        string.Create(CultureInfo.InvariantCulture, $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}");
}
