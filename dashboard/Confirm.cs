using MentalDesk.Tui.Dialogs;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>Builds MentalDesk.Tui's <see cref="ConfirmDialog"/> and gives it its keys, which the library binds only
/// under an AppShell the dashboard doesn't have.</summary>
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
        // A focused Button presses on Enter before the dialog sees the key, so each button routes the keys too.
        foreach (var view in dialog.ButtonRow.Prepend<View>(dialog))
            view.KeyDown += (_, key) => key.Handled = Handle(dialog, action, key);
        return dialog;
    }

    public static bool Ask(IApplication app, ConfirmDialog dialog)
    {
        app.Run(dialog);
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

    private static bool Handle(ConfirmDialog dialog, ConfirmAction action, Key key)
    {
        if (key == action.Key)
            Press(dialog.ButtonFor(action));
        else if (key == Key.Esc)
            Press(dialog.CancelButton);
        else if (key == Key.CursorLeft || key == Key.CursorRight)
            Move(dialog, key == Key.CursorLeft ? -1 : +1);
        else
            return key == Key.Enter;
        return true;
    }

    private static void Press(Button button) => button.InvokeCommand(Command.Accept);

    private static void Move(ConfirmDialog dialog, int by)
    {
        var buttons = dialog.ButtonRow.ToList();
        var at = buttons.FindIndex(button => button.HasFocus);
        buttons[Math.Clamp(at < 0 ? 0 : at + by, 0, buttons.Count - 1)].SetFocus();
    }
}
