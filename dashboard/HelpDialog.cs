using System.Text;
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

    private const string HintText = "Esc close";
    private const int Gap = 4;
    private const int Inset = 2;

    private readonly Label _keys;
    private readonly Button _hint;

    public HelpDialog(IReadOnlyList<CommandDescriptor> commands)
    {
        var rows = Rows(commands);
        var wide = Math.Max(rows.Max(row => row.Length), HintText.Length) + (Inset * 2);
        var tall = rows.Count + 2;

        Title = "Help";
        Width = Dim.Func(_ => Fits(wide + GetAdornmentsThickness().Horizontal, SuperView?.Viewport.Width), this);
        Height = Dim.Func(_ => Fits(tall + GetAdornmentsThickness().Vertical, SuperView?.Viewport.Height), this);

        _keys = new Label
        {
            X = Inset,
            Y = 0,
            Width = Dim.Fill(Inset),
            Height = Dim.Fill(2),
            Text = string.Join('\n', rows),
            TextFormatter = { WordWrap = false, MultiLine = true },
        };
        _hint = new Button
        {
            Text = HintText,
            X = Pos.Center(),
            Y = Pos.AnchorEnd(1),
            NoDecorations = true,
            NoPadding = true,
            ShadowStyle = ShadowStyles.None,
            HotKeySpecifier = (Rune)0xffff,
            CanFocus = false,
        };
        _hint.Accepting += (_, args) => args.Handled = Close();
        Add(_keys, _hint);
    }

    internal Label Keys => _keys;

    internal Button Hint => _hint;

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
        return [.. Curated.Select((row, index) => $"{keys[index].PadRight(width)}{new string(' ', Gap)}{row.Text}")];
    }

    private static string KeysFor(IReadOnlyList<CommandDescriptor> commands, string[] ids) =>
        string.Join('/', ids
            .Select(id => commands.FirstOrDefault(command => command.Id == id)?.Key ?? Key.Empty)
            .Where(key => key != Key.Empty)
            .Select(Named)
            .Distinct());

    /// <summary>How a key reads in help: the four arrows are one row, and everything else reads as it does elsewhere.</summary>
    private static string Named(Key key) =>
        key == Key.CursorUp || key == Key.CursorDown || key == Key.CursorLeft || key == Key.CursorRight
            ? "arrows"
            : KeyNames.Short(key);

    public static void Show(IApplication app, CommandRegistry commands)
    {
        using var dialog = new HelpDialog(commands.Registered);
        app.Run(dialog);
    }
}
