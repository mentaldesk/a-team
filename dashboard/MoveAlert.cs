using System.Text;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard;

/// <summary>What has just become your move, at the right end of the menu bar: clickable, in the theme's highlight
/// colour and claiming no key. It slides in from the right edge and out to it, and where the menu titles leave it too
/// little room it's cut from the left.</summary>
public sealed class MoveAlert : Button
{
    /// <summary>Frames a slide takes each way.</summary>
    internal const int SlideSteps = 3;

    internal static readonly TimeSpan SlideEvery = TimeSpan.FromMilliseconds(40);

    private const string Cut = "…";

    private readonly Queue<string> _frames = [];
    private string _alerting = "";

    public MoveAlert()
    {
        Y = 0;
        Height = 1;
        NoDecorations = true;
        NoPadding = true;
        ShadowStyle = ShadowStyles.None;
        HotKeySpecifier = (Rune)0xffff;
        CanFocus = false;
        Accepting += (_, args) =>
        {
            args.Handled = true;
            if (_alerting.Length > 0)
                Chosen?.Invoke();
        };
        FrameChanged += (_, _) => Fit();
    }

    /// <summary>Raised when the alert is clicked while it says something.</summary>
    internal event Action? Chosen;

    /// <summary>The alert showing or being slid to.</summary>
    internal string Alerting => _alerting;

    /// <summary>The frame showing now, before it's cut to fit.</summary>
    internal string Shown { get; private set; } = "";

    /// <summary>Slides the alert out to the right and <paramref name="alert"/> in from the right edge; with no app to
    /// time the frames, it changes at once.</summary>
    public void Show(string alert)
    {
        if (alert == _alerting)
            return;
        var from = _frames.Count > 0 ? _alerting : Shown;
        _alerting = alert;
        _frames.Clear();
        foreach (var frame in Slide(from, alert))
            _frames.Enqueue(frame);
        if (App is null)
        {
            _frames.Clear();
            ShowFrame(alert);
            return;
        }
        App.AddTimeout(SlideEvery, () =>
        {
            if (_frames.Count == 0)
                return false;
            ShowFrame(_frames.Dequeue());
            if (_frames.Count > 0)
                return true;
            ShowFrame(_alerting);
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

    /// <summary><paramref name="text"/> in <paramref name="width"/> columns, its left end swapped for an ellipsis
    /// where it doesn't fit.</summary>
    internal static string Fitted(string text, int width)
    {
        if (text.GetColumns() <= width)
            return text;
        if (width <= 0)
            return "";
        var runes = text.EnumerateRunes().Select(rune => rune.ToString()).ToList();
        var kept = new List<string>();
        var columns = Cut.GetColumns();
        for (var i = runes.Count - 1; i >= 0 && columns + runes[i].GetColumns() <= width; i--)
        {
            kept.Insert(0, runes[i]);
            columns += runes[i].GetColumns();
        }
        return Cut + string.Concat(kept);
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
    private void ShowFrame(string text)
    {
        var shown = text.TrimStart();
        if (Shown == shown)
            return;
        Shown = shown;
        Fit();
        SuperView?.SetNeedsLayout();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void Fit()
    {
        var text = Fitted(Shown, Frame.Width);
        if (Text != text)
            Text = text;
    }

    protected override bool OnGettingAttributeForRole(in VisualRole role, ref Attribute currentAttribute)
    {
        currentAttribute = Highlight;
        return true;
    }

    /// <summary>The menu bar's own scheme has no highlight, so it's the one the theme highlights with everywhere else.</summary>
    internal static Attribute Highlight => SchemeManager.GetScheme(Schemes.Base).GetAttributeForRole(VisualRole.Highlight);
}
