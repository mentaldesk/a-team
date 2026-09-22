using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>The handful of keys worth memorising, starting with the one that finds the rest.</summary>
public sealed class HelpDialog : Dialog
{
    /// <summary>What's worth memorising, in the order it's worth learning. The keys come from the registry.</summary>
    private static readonly (string Text, string[] Ids)[] Curated =
    [
        ("every command, by name", ["commands"]),
        ("select an agent", ["agent.next", "agent.right", "agent.left", "agent.down", "agent.up"]),
        ("expand the selected agent", ["agent.expand"]),
        ("scroll the log", ["log.pageUp", "log.pageDown"]),
        ("settings", ["settings"]),
        ("back, or quit", ["agent.collapse"]),
    ];

    private readonly Label _keys;

    public HelpDialog(IReadOnlyList<CommandDescriptor> commands)
    {
        var rows = Rows(commands);
        var wide = rows.Max(row => row.Length);
        var tall = rows.Count + 1;

        Title = "Help";
        Width = Dim.Func(_ => Fits(wide + GetAdornmentsThickness().Horizontal, SuperView?.Viewport.Width), this);
        Height = Dim.Func(_ => Fits(tall + GetAdornmentsThickness().Vertical, SuperView?.Viewport.Height), this);

        _keys = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Text = string.Join('\n', rows),
            TextFormatter = { WordWrap = false, MultiLine = true },
        };
        var hints = new StatusBar([Shortcut("Esc", "close", () => Close())]) { CanFocus = false };
        Add(_keys, hints);
    }

    internal Label Keys => _keys;

    internal bool Closed { get; private set; }

    /// <summary>Enter reaches a Dialog as Accept and would close it; Help has nothing to confirm.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => true;

    protected override bool OnKeyDown(Key key) => key == Key.Esc ? Close() : base.OnKeyDown(key);

    private static int Fits(int wanted, int? available) => available is { } room ? Math.Min(wanted, room) : wanted;

    private bool Close()
    {
        Closed = true;
        RequestStop();
        return true;
    }

    private static List<string> Rows(IReadOnlyList<CommandDescriptor> commands)
    {
        var keys = Curated.Select(row => KeysFor(commands, row.Ids)).ToList();
        var width = keys.Max(key => key.Length);
        return [.. Curated.Select((row, index) => $"{keys[index].PadRight(width)}  {row.Text}")];
    }

    private static string KeysFor(IReadOnlyList<CommandDescriptor> commands, string[] ids) =>
        string.Join('/', ids
            .Select(id => commands.FirstOrDefault(command => command.Id == id)?.Key ?? Key.Empty)
            .Where(key => key != Key.Empty)
            .Select(Named)
            .Distinct());

    /// <summary>How a key reads in help: the four arrows are one row, and the long page keys are abbreviated.</summary>
    private static string Named(Key key) =>
        key == Key.CursorUp || key == Key.CursorDown || key == Key.CursorLeft || key == Key.CursorRight ? "arrows"
        : key == Key.PageUp ? "PgUp"
        : key == Key.PageDown ? "PgDn"
        : key.ToString();

    private static Shortcut Shortcut(string keys, string what, Action action) =>
        new() { Title = what, HelpText = keys, Key = Key.Empty, Action = action, CanFocus = false };

    public static void Show(IApplication app, CommandRegistry commands)
    {
        using var dialog = new HelpDialog(commands.Registered);
        app.Run(dialog);
    }
}
