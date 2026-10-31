using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>A view showing one agent session's log, which the log keys act on: an expanded agent's pane, or the
/// selected card's session in Overseer.</summary>
internal interface ISessionView
{
    void Page(int direction);

    void Home();

    void End();

    void MoveLine(int step, bool extend);

    LogCopy CopySelection();

    LogCopy CopyAll();

    void ToggleToolCalls();

    string? LogPath { get; }
}

/// <summary>Overseer's panel for the selected card: its details, then the session of the agent on it, following
/// live. Full-screen, it drops the details and selects lines like an expanded agent's pane.</summary>
public sealed class CardPanel : FrameView, ISessionView
{
    private static readonly string BaseScheme = SchemeManager.SchemesToSchemeName(Schemes.Base)!;

    private readonly Label _details = new() { X = 0, Y = 0, Width = Dim.Fill(), Height = 0, CanFocus = false };
    private readonly Label _session = new() { X = 0, Width = Dim.Fill(), CanFocus = false };
    private readonly Line _rule = new() { X = 0, Width = Dim.Fill() };
    private readonly LogView _log;
    private readonly Label _hint = new() { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), CanFocus = false, SchemeName = LogSchemes.Dimmed };
    private readonly SessionLog _read = new();
    private string? _shown;
    private bool _full;

    public CardPanel(bool expandToolCalls)
    {
        CanFocus = true;
        SchemeName = BaseScheme;
        _session.Y = Pos.Bottom(_details);
        _rule.Y = Pos.Bottom(_session);
        _log = new LogView { X = 0, Y = Pos.Bottom(_rule), Width = Dim.Fill(), Height = Dim.Fill(1), Expanded = expandToolCalls };
        Add(_details, _session, _rule, _log, _hint);
    }

    internal string Details => _details.Text;

    /// <summary>The details' lines and a blank one under them.</summary>
    private int DetailRows => _details.Text.Split('\n').Length + 1;

    internal string Session => _session.Text;

    internal string Hint => _hint.Text;

    internal IReadOnlyList<LogLine> Lines => _log.Lines;

    public string? LogPath => _shown;

    /// <summary>The session id <c>claude --resume</c> picks the shown run's conversation up from.</summary>
    internal string? SessionId => _read.SessionId;

    internal LogPlace Place => _log.Place;

    /// <summary>Full-screen: the details give way to the log, whose lines can be selected and copied.</summary>
    public bool Full
    {
        get => _full;
        set
        {
            _full = value;
            _log.Selects = value;
            _details.Visible = !value;
            _details.Height = value ? 0 : DetailRows;
            _hint.Visible = !value;
            _log.Height = Dim.Fill(value ? 0 : 1);
            SetNeedsLayout();
        }
    }

    /// <summary>Shows <paramref name="card"/>'s details, and the log of <paramref name="run"/>, read on from where it was
    /// when it's still the same log.</summary>
    public void Show(BoardCard card, IReadOnlyList<string> details, CardRun? run, string says, string hint)
    {
        Set(this, $"#{card.Number} {card.Title}");
        var text = string.Join("\n", details);
        if (_details.Text != text)
        {
            _details.Text = text;
            if (!_full)
                _details.Height = DetailRows;
        }
        if (_session.Text != says)
            _session.Text = says;
        if (_hint.Text != hint)
            _hint.Text = hint;
        var path = run?.State.LogPath;
        var switched = path != _shown;
        _shown = path;
        if (_read.Refresh(path) || switched)
            _log.Lines = _read.Lines.Count == 0 && path is not null ? [new LogLine("(no session yet)", LogLineKind.Prose)] : [.. _read.Lines];
        _rule.Visible = path is not null;
    }

    internal void Restore(LogPlace place) => _log.Restore(place, _log.Lines);

    public void ShowIcons(IconStyle style) => _log.ShowIcons(style);

    public void Page(int direction) => _log.Page(direction);

    public void Home() => _log.Home();

    public void End() => _log.End();

    public void MoveLine(int step, bool extend) => _log.MoveSelection(step, extend);

    public LogCopy CopySelection() => _log.CopySelection();

    public LogCopy CopyAll() => _log.CopyAll();

    public void ToggleToolCalls() => _log.ToggleToolCalls();

    private static void Set(View view, string title)
    {
        if (view.Title != title)
            view.Title = title;
    }
}
