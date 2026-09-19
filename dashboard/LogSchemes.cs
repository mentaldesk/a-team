using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard;

/// <summary>The named schemes the log draws with, beyond Terminal.Gui's built-ins.</summary>
public static class LogSchemes
{
    public const string Success = "Success";

    public static void Register()
    {
        var baseScheme = SchemeManager.GetScheme(Schemes.Base);
        SchemeManager.AddScheme(Success, baseScheme with
        {
            Normal = new Attribute(StandardColor.Green, baseScheme.Normal.Background),
        });
    }
}
