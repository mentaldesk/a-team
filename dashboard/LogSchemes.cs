using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard;

/// <summary>The named schemes the dashboard draws with, beyond Terminal.Gui's built-ins.</summary>
public static class LogSchemes
{
    public const string Success = "Success";
    public const string Dimmed = "Dimmed";
    public const string Form = "Form";

    // Anything under 0.1 GetBrighterColor doubles, so that's the floor for a step it takes as asked.
    private const double Lift = 0.12;

    public static void Register()
    {
        var baseScheme = SchemeManager.GetScheme(Schemes.Base);
        SchemeManager.AddScheme(Success, baseScheme with
        {
            Normal = new Attribute(StandardColor.Green, baseScheme.Normal.Background),
        });
        SchemeManager.AddScheme(Dimmed, baseScheme with
        {
            Normal = baseScheme.GetAttributeForRole(VisualRole.Disabled),
        });
        var form = Banded(SchemeManager.GetScheme(Schemes.Dialog));
        SchemeManager.AddScheme(Form, form);
        Priorities.Register(baseScheme, form);
    }

    /// <summary>The band a dialog's own controls sit in: the Dialog scheme with its background a step away from
    /// the text, so the band is a region of its own in every theme — lighter on a dark one, darker on a light
    /// one. Focus keeps the dialog's colours, so the control under the keyboard still reads as focused.</summary>
    private static Scheme Banded(Scheme dialog)
    {
        var background = dialog.Normal.Background;
        var lifted = background.GetBrighterColor(Lift, background.IsDarkColor());
        return dialog with
        {
            Normal = dialog.Normal with { Background = lifted },
            HotNormal = dialog.HotNormal with { Background = lifted },
            Highlight = dialog.Highlight with { Background = lifted },
            Disabled = dialog.Disabled with { Background = lifted },
        };
    }
}
