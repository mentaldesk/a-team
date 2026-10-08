namespace ATeam.Dashboard;

/// <summary>Asks before the reader closes on a comment that hasn't been posted.</summary>
public sealed class DiscardDialog() : ConfirmDialog("Discard your comment?", Says, "discard")
{
    internal const string Says = "What you've written hasn't been posted. Closing the reader throws it away.";

    public static bool Show(IApplication app)
    {
        using var dialog = new DiscardDialog();
        return Ask(app, dialog);
    }
}
