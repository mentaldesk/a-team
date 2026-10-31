using MentalDesk.Tui.Keys;

namespace ATeam.Dashboard.Tests;

internal static class Chords
{
    /// <summary>Presses each key of <paramref name="chord"/>, written as <see cref="KeyChord.Parse"/> reads it, handing it to
    /// the window's chord first as the app does, and says whether the last was handled.</summary>
    internal static bool Press(this DashboardWindow window, string chord)
    {
        var handled = false;
        foreach (var key in KeyChord.Parse(chord))
            handled = window.Chording(key) || window.NewKeyDownEvent(key);
        return handled;
    }
}
