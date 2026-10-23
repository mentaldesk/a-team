using System.Globalization;
using MentalDesk.Tui.Copying;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>Copies text and words the outcome as the style guide does, through the terminal as well where the app
/// installed <see cref="TerminalClipboard"/>, so a copy reaches the user's own machine over SSH.</summary>
public static class Copying
{
    public static CopyOutcome Copy(IClipboard? clipboard, string text) => clipboard switch
    {
        TerminalClipboard terminal => terminal.Copy(text),
        { } other when other.TrySetClipboardData(text) => Counted(text),
        _ => new CopyOutcome.Failed("there is no clipboard"),
    };

    /// <summary>What to say about a copy: <paramref name="named"/> is said in place of its counts.</summary>
    public static (string Text, Schemes Scheme) Said(CopyOutcome outcome, string? named = null, bool truncated = false) =>
        outcome switch
        {
            CopyOutcome.Copied copied =>
                ((named is null ? copied.Message : $"Copied {named}{(copied.ThroughTerminal ? " through the terminal" : "")}")
                    + (truncated ? " (line truncated)" : ""), Schemes.Accent),
            _ => (outcome.Message, Schemes.Error),
        };

    private static CopyOutcome.Copied Counted(string text)
    {
        text = text.ReplaceLineEndings("\n");
        var lines = (text.EndsWith('\n') ? text[..^1] : text).Split('\n');
        return new(lines.Length, lines.Sum(line => new StringInfo(line).LengthInTextElements));
    }
}
