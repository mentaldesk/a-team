using MentalDesk.Tui.Dialogs;

namespace ATeam.Dashboard;

/// <summary>Asks before quitting drops writes that haven't started, naming them.</summary>
public static class QuitDialog
{
    internal static readonly ConfirmAction Quit = new("Quit", ButtonKind.Danger, Confirm.Chord);

    internal static string Says(Write? running, IReadOnlyList<Write> queued)
    {
        var waiting = $"{Join(queued.Select(write => write.Queued))} {(queued.Count == 1 ? "is" : "are")} still queued.";
        var dropped = queued.Count == 1 ? "it" : "them";
        return running is null
            ? $"{Capitalised(waiting)}\n\nQuitting drops {dropped}."
            : $"{running.Doing.TrimEnd('…')}, and {waiting}\n\nQuitting lets that finish and drops {dropped}.";
    }

    internal static ConfirmDialog Create(Write? running, IReadOnlyList<Write> queued, int width = Confirm.Wide) =>
        Confirm.Create("Quit?", Says(running, queued), Quit, width, "Stay");

    public static bool Show(IApplication app, Write? running, IReadOnlyList<Write> queued)
    {
        using var dialog = Create(running, queued, Confirm.Fit(app.Screen.Width));
        return Confirm.Ask(app, dialog);
    }

    private static string Join(IEnumerable<string> parts)
    {
        var all = parts.ToList();
        return all.Count == 1 ? all[0] : $"{string.Join(", ", all[..^1])} and {all[^1]}";
    }

    private static string Capitalised(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
