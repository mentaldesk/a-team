namespace ATeam.Dashboard;

/// <summary>What <see cref="IconStyle.Auto"/> means in the terminal actually drawing. No terminal reports its
/// font, but kitty, WezTerm and Ghostty each bundle a Nerd Font and fall back to it whatever font the reader
/// picked, so in those three the glyph is certain to draw. Anything else, including a session where tmux or ssh
/// has masked the variables, gets the plain vocabulary.</summary>
public static class TerminalIcons
{
    /// <summary>A variable one of those three sets, and the value it has to hold; null is any value at all.</summary>
    private static readonly (string Variable, string? Value)[] BundledNerdFont =
    [
        ("TERM", "xterm-kitty"),
        ("KITTY_WINDOW_ID", null),
        ("TERM_PROGRAM", "WezTerm"),
        ("WEZTERM_PANE", null),
        ("TERM", "xterm-ghostty"),
        ("GHOSTTY_RESOURCES_DIR", null),
    ];

    public static IconStyle Detect(Func<string, string?> environment) =>
        BundledNerdFont.Any(signal => environment(signal.Variable) is { Length: > 0 } value &&
                                      (signal.Value is null ||
                                       string.Equals(value, signal.Value, StringComparison.OrdinalIgnoreCase)))
            ? IconStyle.NerdFont
            : IconStyle.Unicode;
}
