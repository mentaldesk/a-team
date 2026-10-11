using MentalDesk.Tui.Dialogs;

namespace ATeam.Dashboard;

/// <summary>Asks before a dialog closes on something you've written that hasn't been sent.</summary>
public static class DiscardDialog
{
    internal const string Says = "What you've written hasn't been posted. Closing the reader throws it away.";
    internal const string IdeaSays = "What you've written hasn't been added. Closing the dialog throws it away.";
    internal const string KeepWriting = "Keep writing";

    internal static readonly ConfirmAction Discard = new("Discard", ButtonKind.Danger, Confirm.Chord);

    internal static readonly (string Title, string Says) Comment = ("Discard your comment?", Says);
    internal static readonly (string Title, string Says) Idea = ("Discard your idea?", IdeaSays);

    internal static ConfirmDialog Create((string Title, string Says) what, int width = Confirm.Wide) =>
        Confirm.Create(what.Title, what.Says, Discard, width, KeepWriting);

    public static bool Show(IApplication app) => Show(app, Comment);

    public static bool Show(IApplication app, (string Title, string Says) what)
    {
        using var dialog = Create(what, Confirm.Fit(app.Screen.Width));
        return Confirm.Ask(app, dialog);
    }
}
