using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>How a key reads on screen: Terminal.Gui's own name, with the long page keys abbreviated.</summary>
public static class KeyNames
{
    public static string Short(Key key) =>
        key == Key.PageUp ? "PgUp"
        : key == Key.PageDown ? "PgDn"
        : key == Key.Empty ? ""
        : key.ToString();
}
