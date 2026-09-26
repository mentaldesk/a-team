using System.Text;

namespace ATeam.Dashboard;

/// <summary>The window's last row: the keys you can press, each one clickable, and at its right end what the
/// area is showing. Terminal.Gui's own StatusBar lays out Shortcuts with a border between each, so the hints
/// are laid out here instead.</summary>
public sealed class StatusBar : View
{
    internal const string Scheme = "StatusBar";
    private const string Separator = " · ";
    private const int Gap = 1;
    private readonly View _hints;
    private readonly Label _state = new() { X = Pos.AnchorEnd(), Y = 0, CanFocus = false };

    public StatusBar()
    {
        X = 0;
        Width = Dim.Fill();
        Height = 1;
        CanFocus = false;
        SchemeName = Scheme;
        _hints = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Func(_ => Math.Max(0, Viewport.Width - Room()), this),
            Height = 1,
            CanFocus = false,
        };
        Add(_hints, _state);
    }

    /// <summary>The row as it's drawn from the left: the version, then each hint.</summary>
    internal string Says => string.Concat(_hints.SubViews.Select(view => view.Text));

    internal Label State => _state;

    internal IReadOnlyList<Button> Hints => [.. _hints.SubViews.OfType<Button>()];

    /// <summary>The keys for the mode the window is in now. Clicking one runs the command it names; where
    /// several commands share a hint, like the scroll keys, it runs the first of them.</summary>
    public void Show(string version, IReadOnlyList<HintedCommand> hints, Func<string, bool> run)
    {
        foreach (var view in _hints.SubViews.ToArray())
        {
            _hints.Remove(view);
            view.Dispose();
        }
        var lead = new Label { Text = version, X = 0, Y = 0, CanFocus = false };
        _hints.Add(lead);
        Pos x = Pos.Right(lead);
        foreach (var hint in hints)
        {
            var separator = new Label { Text = Separator, X = x, Y = 0, CanFocus = false };
            var button = new Button
            {
                Text = hint.Text,
                X = Pos.Right(separator),
                Y = 0,
                NoDecorations = true,
                NoPadding = true,
                ShadowStyle = ShadowStyles.None,
                HotKeySpecifier = (Rune)0xffff,
                CanFocus = false,
            };
            button.Accepting += (_, args) => args.Handled = run(hint.Id);
            _hints.Add(separator, button);
            x = Pos.Right(button);
        }
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>The right end: when the area was last read, then what it's filtered to.</summary>
    public void ShowState(string stamp, string filter)
    {
        var state = string.Join(Separator, new[] { stamp, filter }.Where(part => part.Length > 0));
        if (_state.Text == state)
            return;
        _state.Text = state;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>What the right end leaves the hints, so a long hint bar is cut off rather than drawn over it.</summary>
    private int Room() => _state.Text.Length == 0 ? 0 : _state.Text.Length + Gap;
}
