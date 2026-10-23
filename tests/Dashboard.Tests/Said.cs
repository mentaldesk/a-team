namespace ATeam.Dashboard.Tests;

internal static class Said
{
    /// <summary>What the status bar shows after the focus word: the message, or else the resting text.</summary>
    public static string Says(this DashboardWindow window) => window.Status.Message ?? window.Status.RestingText;
}
