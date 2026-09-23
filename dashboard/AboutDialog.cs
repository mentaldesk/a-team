using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>What this is and which version of it you're running.</summary>
public sealed class AboutDialog : Dialog
{
    private const string HintText = "Esc close";
    private const int Inset = 2;

    private readonly Label _about;
    private readonly Button _hint;

    public AboutDialog(string version)
    {
        var rows = Rows(version);
        var wide = Math.Max(rows.Max(row => row.Length), HintText.Length) + (Inset * 2);

        Title = "About";
        Width = Dim.Func(_ => Fits(wide + GetAdornmentsThickness().Horizontal, SuperView?.Viewport.Width), this);
        Height = Dim.Func(_ => Fits(rows.Count + 2 + GetAdornmentsThickness().Vertical, SuperView?.Viewport.Height), this);

        _about = new Label
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
        Add(_about, _hint);
    }

    internal Label About => _about;

    internal bool Closed { get; private set; }

    /// <summary>Enter reaches a Dialog as Accept and would close it; About has nothing to confirm.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => true;

    protected override bool OnKeyDown(Key key) => key == Key.Esc ? Close() : base.OnKeyDown(key);

    internal static List<string> Rows(string version) =>
    [
        $"a-team {version}",
        "A small team of agents that work one repo.",
        "https://github.com/mentaldesk/a-team",
    ];

    private static int Fits(int wanted, int? available) => available is { } room ? Math.Min(wanted, room) : wanted;

    private bool Close()
    {
        Closed = true;
        RequestStop();
        return true;
    }

    public static void Show(IApplication app, string version)
    {
        using var dialog = new AboutDialog(version);
        app.Run(dialog);
    }
}
