using System.Drawing;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>What this is and which version of it you're running.</summary>
public sealed class AboutDialog : Dialog
{
    private const string HintText = "Esc close";
    private const int Inset = 2;

    private readonly LoadingView _van;
    private readonly Label _about;
    private readonly Button _hint;
    private readonly int _textWide;
    private readonly int _textTall;

    public AboutDialog(string version)
    {
        var rows = Rows(version);
        _textWide = Math.Max(rows.Max(row => row.Length), HintText.Length) + (Inset * 2);
        _textTall = rows.Count + 2;

        Title = "About";
        Width = Dim.Func(_ => Fits(Wanted().Width, SuperView?.Viewport.Width), this);
        Height = Dim.Func(_ => Fits(Wanted().Height, SuperView?.Viewport.Height), this);

        _van = new LoadingView { X = Pos.Center(), Y = 0 };
        _about = new Label
        {
            X = Inset,
            Y = Pos.Func(_ => _van.Visible ? _van.Frame.Bottom + 1 : 0, this),
            Width = Dim.Fill(Inset),
            Height = rows.Count,
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
        SubViewLayout += (_, _) =>
        {
            if (!Closed && Roomy())
                _van.Start();
            else
                _van.Stop();
        };
        Add(_van, _about, _hint);
    }

    internal LoadingView Van => _van;

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

    private Size WithVan()
    {
        var edges = GetAdornmentsThickness();
        return new Size(
            Math.Max(_textWide, LoadingView.Cells + 2 + (Inset * 2)) + edges.Horizontal,
            LoadingView.Rows + 2 + 1 + _textTall + edges.Vertical);
    }

    private bool Roomy() =>
        SuperView?.Viewport.Size is not { } room || WithVan() is var wanted && wanted.Width <= room.Width && wanted.Height <= room.Height;

    private Size Wanted()
    {
        if (Roomy())
            return WithVan();
        var edges = GetAdornmentsThickness();
        return new Size(_textWide + edges.Horizontal, _textTall + edges.Vertical);
    }

    private bool Close()
    {
        Closed = true;
        _van.Stop();
        RequestStop();
        return true;
    }

    public static void Show(IApplication app, string version)
    {
        using var dialog = new AboutDialog(version);
        app.Run(dialog);
    }
}
