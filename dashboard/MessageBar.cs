using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>The line for what just happened: below a dialog's hints, so it never takes their row, or the left end of
/// the window's status bar. No row at all while there's nothing to say.</summary>
public sealed class MessageBar : Label
{
    public MessageBar()
    {
        X = 0;
        Width = Dim.Fill();
        Height = Dim.Func(_ => Lines, this);
        CanFocus = false;
    }

    public int Lines { get; private set; }

    public string Says { get; private set; } = "";

    public void Show(string message, Schemes scheme)
    {
        Says = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";
        SchemeName = SchemeManager.SchemesToSchemeName(scheme);
        Lay();
    }

    public void Clear()
    {
        Says = "";
        Lay();
    }

    private void Lay()
    {
        Text = Says;
        Lines = Says.Length == 0 ? 0 : 1;
    }
}
