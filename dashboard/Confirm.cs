using MentalDesk.Tui.Dialogs;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>Builds MentalDesk.Tui's <see cref="ConfirmDialog"/> with its keys routed, as an AppShell would.</summary>
public static class Confirm
{
    internal const int Wide = 74;
    private const int Frame = 4;

    internal static readonly Key Chord = Key.Enter.WithCtrl;

    /// <summary>A confirm that goes ahead with <paramref name="action"/> on Ctrl+Enter and cancels on Esc, its text
    /// wrapped to <paramref name="width"/>.</summary>
    public static ConfirmDialog Create(string title, string text, ConfirmAction action, int width = Wide, string? cancel = null)
    {
        var dialog = new ConfirmDialog(title, Lines(text, width), [action]);
        if (cancel is not null)
            dialog.CancelButton.Text = $" Esc {cancel} ";
        return dialog.Routed();
    }

    public static bool Ask(IApplication app, ConfirmDialog dialog)
    {
        DialogHost.Run(app, dialog);
        return dialog.Confirmed;
    }

    /// <summary>The widest text that fits a screen <paramref name="screen"/> columns wide.</summary>
    internal static int Fit(int screen) => Math.Clamp(screen - Frame, 1, Wide);

    /// <summary>Each line of the text wrapped on its own, so a blank line between paragraphs stays.</summary>
    internal static List<string> Lines(string text, int width) =>
        [.. text.Split('\n').SelectMany(paragraph => Wrap(paragraph, width))];

    /// <summary>The text broken at spaces into lines no wider than <paramref name="width"/>.</summary>
    internal static List<string> Wrap(string text, int width)
    {
        List<string> lines = [];
        var line = "";
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                lines.Add(line);
                line = word;
            }
            else
                line = line.Length == 0 ? word : $"{line} {word}";
        }
        lines.Add(line);
        return lines;
    }
}
