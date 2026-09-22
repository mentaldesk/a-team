using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>The window's line for what just happened: no rows at all while there's nothing to say.</summary>
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

    public void Show(string message, Schemes scheme)
    {
        Text = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";
        SchemeName = SchemeManager.SchemesToSchemeName(scheme);
        Lines = Text.Length == 0 ? 0 : 1;
    }

    public void Clear()
    {
        Text = "";
        Lines = 0;
    }
}
