using System.Text;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard;

/// <summary>The last row of a window or a dialog: the keys you can press, each one clickable, or what's going on,
/// and at its right end what the area is showing, then, in a window that has one, what has just become your move. Terminal.Gui's own StatusBar lays out Shortcuts with a border between
/// each, so the hints are laid out here instead.</summary>
public sealed class StatusBar : View
{
    internal const string Scheme = "StatusBar";
    private const string Separator = " · ";
    private const int Gap = 1;
    private const int AlertGap = 3;

    /// <summary>The alert's slot, so the state doesn't shift as one alert gives way to another.</summary>
    private const int AlertSlot = 12;

    /// <summary>Frames a slide takes each way.</summary>
    internal const int SlideSteps = 3;

    internal static readonly TimeSpan SlideEvery = TimeSpan.FromMilliseconds(40);

    private readonly View _hints;
    private readonly MessageBar _message = new() { Y = 0 };
    private readonly Label _state = new() { Y = 0, CanFocus = false };
    private readonly AlertButton _alert = new();
    private readonly Queue<string> _frames = [];
    private string _alerting = "";

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
        _message.Width = Dim.Func(_ => Math.Max(0, Viewport.Width - Room()), this);
        _state.X = Pos.Func(_ => Math.Max(0, Viewport.Width - _state.Text.GetColumns() - AlertRoom()), this);
        _alert.X = Pos.AnchorEnd();
        _alert.Accepting += (_, args) =>
        {
            args.Handled = true;
            if (_alerting.Length > 0)
                AlertChosen?.Invoke();
        };
        Add(_hints, _message, _state, _alert);
    }

    /// <summary>Whether the row shows an <see cref="Alert"/> at its right end.</summary>
    public bool Alerts { get; init; }

    /// <summary>Raised when the alert is clicked while it says something.</summary>
    internal event Action? AlertChosen;

    internal Button AlertView => _alert;

    /// <summary>The alert the bar is showing or sliding to.</summary>
    internal string Alerting => _alerting;

    /// <summary>The row as it's drawn from the left: the version, then each hint.</summary>
    internal string Says => string.Concat(_hints.SubViews.Select(view => view.Text));

    internal Label State => _state;

    internal MessageBar Message => _message;

    internal IReadOnlyList<Button> Hints => [.. _hints.SubViews.OfType<Button>()];

    /// <summary>The keys for the mode the window is in now, after <paramref name="version"/> where there is
    /// one. Clicking a hint runs the command it names; where several commands share a hint, like the scroll
    /// keys, it runs the first of them.</summary>
    public void Show(string version, IReadOnlyList<HintedCommand> hints, Func<string, bool> run)
    {
        foreach (var view in _hints.SubViews.ToArray())
        {
            _hints.Remove(view);
            view.Dispose();
        }
        Pos x = 0;
        if (version.Length > 0)
        {
            var lead = new Label { Text = version, X = 0, Y = 0, CanFocus = false };
            _hints.Add(lead);
            x = Pos.Right(lead);
        }
        foreach (var hint in hints)
        {
            if (_hints.SubViews.Count > 0)
            {
                var separator = new Label { Text = Separator, X = x, Y = 0, CanFocus = false };
                _hints.Add(separator);
                x = Pos.Right(separator);
            }
            var button = new Button
            {
                Text = hint.Text,
                X = x,
                Y = 0,
                NoDecorations = true,
                NoPadding = true,
                ShadowStyle = ShadowStyles.None,
                HotKeySpecifier = (Rune)0xffff,
                CanFocus = false,
                Enabled = hint.Enabled,
            };
            button.Accepting += (_, args) => args.Handled = run(hint.Id);
            _hints.Add(button);
            x = Pos.Right(button);
        }
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>The left end, in place of hints: what's selected, what's running or what just happened. Only an
    /// error takes a colour of its own.</summary>
    public void Say(string message, Schemes scheme)
    {
        _message.Show(message, scheme);
        if (scheme != Schemes.Error)
            _message.SchemeName = Scheme;
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

    /// <summary>Slides the alert out to the right and <paramref name="alert"/> in from the right edge; with no app to
    /// time the frames, it changes at once.</summary>
    public void Alert(string alert)
    {
        if (alert == _alerting)
            return;
        var from = _frames.Count > 0 ? _alerting : _alert.Text;
        _alerting = alert;
        _frames.Clear();
        foreach (var frame in Slide(from, alert))
            _frames.Enqueue(frame);
        if (App is null)
        {
            _frames.Clear();
            ShowAlert(alert);
            return;
        }
        App.AddTimeout(SlideEvery, () =>
        {
            if (_frames.Count == 0)
                return false;
            ShowAlert(_frames.Dequeue());
            if (_frames.Count > 0)
                return true;
            ShowAlert(_alerting);
            return false;
        });
    }

    /// <summary>Each frame of the change from <paramref name="from"/> to <paramref name="to"/>, right-aligned in the
    /// width of the wider: the old one leaving to the right a step at a time, then the new one arriving from the
    /// right edge.</summary>
    internal static IReadOnlyList<string> Slide(string from, string to)
    {
        var width = Math.Max(from.GetColumns(), to.GetColumns());
        var frames = new List<string>();
        if (from.Length > 0)
            for (var step = 1; step <= SlideSteps; step++)
                frames.Add(Shifted(from, width, Steps(from, step)));
        if (to.Length > 0)
            for (var step = SlideSteps - 1; step >= 0; step--)
                frames.Add(Shifted(to, width, Steps(to, step)));
        return frames;
    }

    private static int Steps(string text, int step) => (text.GetColumns() * step + SlideSteps - 1) / SlideSteps;

    /// <summary><paramref name="text"/> at the right of <paramref name="width"/> columns, pushed
    /// <paramref name="shift"/> further right and cut off at the edge.</summary>
    private static string Shifted(string text, int width, int shift)
    {
        var room = text.GetColumns() - shift;
        var kept = new StringBuilder();
        var columns = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var cells = rune.ToString().GetColumns();
            if (columns + cells > room)
                break;
            kept.Append(rune.ToString());
            columns += cells;
        }
        var left = width - text.GetColumns() + shift;
        return new string(' ', left) + kept + new string(' ', Math.Max(0, width - left - columns));
    }

    /// <summary>A frame's leading blanks are only where it starts, so the highlight covers the text alone.</summary>
    private void ShowAlert(string text)
    {
        var shown = text.TrimStart();
        if (_alert.Text == shown)
            return;
        _alert.Text = shown;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>What the right end leaves the left, so a long hint bar or message is cut off rather than drawn over it.</summary>
    private int Room() => (_state.Text.Length == 0 ? 0 : _state.Text.GetColumns() + Gap) + AlertRoom();

    private int AlertRoom() =>
        Alerts && (_alerting.Length > 0 || _frames.Count > 0) ? Math.Max(AlertSlot, _alert.Text.GetColumns()) + AlertGap : 0;

    /// <summary>A clickable alert in the theme's highlight colour, claiming no key. The bar's own scheme has no
    /// highlight, so it's the one the theme highlights with everywhere else.</summary>
    private sealed class AlertButton : Button
    {
        internal AlertButton()
        {
            Y = 0;
            Width = Dim.Func(_ => Text.GetColumns());
            Height = 1;
            NoDecorations = true;
            NoPadding = true;
            ShadowStyle = ShadowStyles.None;
            HotKeySpecifier = (Rune)0xffff;
            CanFocus = false;
        }

        protected override bool OnGettingAttributeForRole(in VisualRole role, ref Attribute currentAttribute)
        {
            currentAttribute = Highlight;
            return true;
        }

        internal static Attribute Highlight => SchemeManager.GetScheme(Schemes.Base).GetAttributeForRole(VisualRole.Highlight);
    }
}
