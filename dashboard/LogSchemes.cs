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
    public const string Reader = "Reader";
    public const string Warning = "Warning";
    public const string Overdue = "Overdue";
    public const string Related = "Related";

    // Anything under 0.1 GetBrighterColor doubles, so that's the floor for a step it takes as asked.
    private const double Lift = 0.12;
    private const double WarningTint = 0.3;
    private const double ReadableInk = 2;
    private static readonly double[] RelatedTints = [0.5, 0.4, 0.3, 0.2, 0.1];
    private static readonly Color Amber = new(0xFF, 0xC1, 0x07);

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
        var reader = baseScheme with { Normal = baseScheme.GetAttributeForRole(VisualRole.Editable) };
        SchemeManager.AddScheme(Reader, reader);
        MarkdownSchemes.Register(reader, ThemeManager.Theme);
        var form = Banded(SchemeManager.GetScheme(Schemes.Dialog));
        SchemeManager.AddScheme(Form, form);
        SchemeManager.AddScheme(Warning, Tinted(SchemeManager.GetScheme(Schemes.Dialog), Amber, WarningTint));
        SchemeManager.AddScheme(Overdue, Tinted(baseScheme, Amber, WarningTint));
        var selection = baseScheme.GetAttributeForRole(VisualRole.Focus).Background;
        SchemeManager.AddScheme(Related, Tinted(baseScheme, selection, RelatedTint(baseScheme.Normal.Background, selection)));
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

    /// <summary>The dialog's colours over a background pulled toward <paramref name="tint"/>, so a block stands out
    /// from the form around it in light and dark themes alike.</summary>
    private static Scheme Tinted(Scheme dialog, Color tint, double amount)
    {
        var background = Mix(dialog.Normal.Background, tint, amount);
        return dialog with
        {
            Normal = dialog.Normal with { Background = background },
            HotNormal = dialog.HotNormal with { Background = background },
            Highlight = dialog.Highlight with { Background = background },
            Disabled = dialog.Disabled with { Background = background },
        };
    }

    /// <summary>The most of the selection's colour a lit chip can take with every Priority's number still readable on it.</summary>
    private static double RelatedTint(Color background, Color selection) =>
        RelatedTints.FirstOrDefault(tint => Priorities.Inks.All(ink => Contrast(ink, Mix(background, selection, tint)) >= ReadableInk), RelatedTints[^1]);

    private static Color Mix(Color from, Color to, double amount)
    {
        int Step(byte a, byte b) => (int)Math.Round(a + (b - a) * amount);
        return new Color(Step(from.R, to.R), Step(from.G, to.G), Step(from.B, to.B));
    }

    /// <summary>The WCAG contrast ratio between two colours.</summary>
    internal static double Contrast(Color foreground, Color background)
    {
        var (a, b) = (Luminance(foreground), Luminance(background));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(Color colour)
    {
        static double Channel(byte value)
        {
            var v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(colour.R) + 0.7152 * Channel(colour.G) + 0.0722 * Channel(colour.B);
    }
}
