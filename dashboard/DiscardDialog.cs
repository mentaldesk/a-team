namespace ATeam.Dashboard;

/// <summary>Asks before a dialog closes on something you've written that hasn't been sent.</summary>
public sealed class DiscardDialog(string title, string says) : ConfirmDialog(title, says, "discard")
{
    internal const string Says = "What you've written hasn't been posted. Closing the reader throws it away.";
    internal const string IdeaSays = "What you've written hasn't been added. Closing the dialog throws it away.";

    internal static readonly (string Title, string Says) Comment = ("Discard your comment?", Says);
    internal static readonly (string Title, string Says) Idea = ("Discard your idea?", IdeaSays);

    public static bool Show(IApplication app) => Show(app, Comment);

    public static bool Show(IApplication app, (string Title, string Says) what)
    {
        using var dialog = new DiscardDialog(what.Title, what.Says);
        return Ask(app, dialog);
    }
}
