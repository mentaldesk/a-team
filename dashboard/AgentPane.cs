using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>What a pane says about its agent: the state its title icon and colour come from.</summary>
public enum PaneStatus
{
    NeverRun,
    Ok,
    Failed,
    CutShort,
    Running,
    Paused,
}

/// <summary>The scheme each part of a pane draws from.</summary>
public readonly record struct PaneSchemes(string Frame, string Status, string Why, string Body);

/// <summary>A pane's title: the icons it wears, and the bare name they're drawn in front of.</summary>
public readonly record struct PaneTitle(string Icons, string Name)
{
    public override string ToString() => Icons + Name;
}

/// <summary>One agent: how its last run went and its timing, why it last started, and a tail of its latest session.</summary>
public sealed class AgentPane : FrameView
{
    private const string Stopped = "dispatcher not running";
    private static readonly string BaseScheme = SchemeManager.SchemesToSchemeName(Schemes.Base)!;
    private static readonly string ErrorScheme = SchemeManager.SchemesToSchemeName(Schemes.Error)!;

    private readonly string _stateDir;
    private readonly string _name;
    private readonly SessionLog _log = new();
    private readonly Label _statusRow;
    private readonly Label _why;
    private readonly LogView _body;
    private PaneStatus _status = PaneStatus.NeverRun;
    private IconStyle _icons = IconStyle.Auto;
    private string _timing = "";

    public AgentPane(string team, string role, string stateDir, bool expandToolCalls)
    {
        Team = team;
        _name = $"{team} · {role}";
        _stateDir = stateDir;
        CanFocus = true;
        _statusRow = new Label { X = 0, Y = 0, Width = Dim.Fill() };
        _why = new Label { X = 0, Y = 1, Width = Dim.Fill() };
        _body = new LogView { X = 0, Y = 2, Width = Dim.Fill(), Height = Dim.Fill(), Expanded = expandToolCalls };
        Add(_statusRow, _why, _body);
        HasFocusChanged += (_, _) => UpdateHeader();
        UpdateHeader();
    }

    public void Page(int direction)
    {
        _body.Page(direction);
        UpdateHeader();
    }

    public void Home()
    {
        _body.Home();
        UpdateHeader();
    }

    public void End()
    {
        _body.End();
        UpdateHeader();
    }

    /// <summary>Draws the title's icons from the vocabulary the reviewer picked.</summary>
    public void ShowIcons(IconStyle style)
    {
        if (_icons == style)
            return;
        _icons = style;
        UpdateHeader();
    }

    public void ToggleToolCalls()
    {
        _body.ToggleToolCalls();
        UpdateHeader();
    }

    internal bool Expanded => _body.Expanded;

    internal string Team { get; }

    internal bool Paused { get; private set; }

    public void Refresh(DateTimeOffset now, DateTimeOffset? nextCheck, bool paused)
    {
        var state = AgentState.Read(_stateDir);
        Paused = paused;

        if (_log.Refresh(state.LogPath))
            _body.Lines = _log.Lines.Count == 0
                ? [new LogLine("(no session yet)", LogLineKind.Prose)]
                : [.. _log.Lines];

        _status = Status(state, paused, _log.Verdict);
        _timing = Describe(state, now, nextCheck, _status);
        UpdateHeader();

        var why = state.Reasons.Count == 0 ? "" : "why: " + string.Join("; ", state.Reasons);
        if (_why.Text != why)
            _why.Text = why;
    }

    private void UpdateHeader()
    {
        var title = Header(_name, HasFocus, _status, _body.Following, _body.Expanded, _icons).ToString();
        if (Title != title)
            Title = title;
        if (_statusRow.Text != _timing)
            _statusRow.Text = _timing;

        var schemes = SchemesFor(_status, _timing);
        Use(this, schemes.Frame);
        Use(_statusRow, schemes.Status);
        Use(_why, schemes.Why);
        Use(_body, schemes.Body);
    }

    private static void Use(View view, string scheme)
    {
        if (view.SchemeName != scheme)
            view.SchemeName = scheme;
    }

    /// <summary>Pause wins, then running, then how the last run went.</summary>
    internal static PaneStatus Status(AgentState state, bool paused, RunVerdict verdict) =>
        paused ? PaneStatus.Paused
            : state.Running ? PaneStatus.Running
            : verdict switch
            {
                RunVerdict.Ok => PaneStatus.Ok,
                RunVerdict.Error => PaneStatus.Failed,
                _ => state.LastStart is null ? PaneStatus.NeverRun : PaneStatus.CutShort,
            };

    internal static PaneTitle Header(
        string name, bool selected, PaneStatus status, bool following, bool expanded, IconStyle style) =>
        new(
            (selected ? Icons.Field(Icon.Selected, style) : "") + Icons.Field(Icons.For(status), style),
            $"{name}{(expanded ? " [tool calls]" : "")}{(following ? "" : " [scrolled]")}");

    /// <summary>A failed run reddens the frame and the status row; only the body is never red.</summary>
    internal static PaneSchemes SchemesFor(PaneStatus status, string timing)
    {
        var failed = status is PaneStatus.Failed or PaneStatus.CutShort;
        return new PaneSchemes(
            failed ? ErrorScheme : BaseScheme,
            failed || timing.EndsWith(Stopped, StringComparison.Ordinal) ? ErrorScheme : BaseScheme,
            LogSchemes.Dimmed,
            BaseScheme);
    }

    internal static string Describe(AgentState state, DateTimeOffset now, DateTimeOffset? nextCheck, PaneStatus status)
    {
        if (state.Running)
            return state.LastStart is { } started ? $"running {Clock(now - started)}" : "running";
        var ran = state.LastStart is { } last ? $"ran {Ago(now - last)} ago" : "never run";
        var cut = status == PaneStatus.CutShort ? " · cut short" : "";
        return $"{ran}{cut} · {(status == PaneStatus.Paused ? "paused" : NextCheck(now, nextCheck))}";
    }

    private static string NextCheck(DateTimeOffset now, DateTimeOffset? nextCheck) => nextCheck switch
    {
        null => "dispatcher hasn't run",
        { } next when next - now >= TimeSpan.Zero => $"next check {Clock(next - now)}",
        { } next when now - next < TimeSpan.FromMinutes(1) => "checking now",
        _ => Stopped,
    };

    private static string Clock(TimeSpan span) => span.TotalHours >= 1
        ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
        : $"{span.Minutes}:{span.Seconds:00}";

    internal static string Ago(TimeSpan span) => span.TotalMinutes < 1
        ? "<1m"
        : span.TotalHours < 1
            ? $"{(int)span.TotalMinutes}m"
            : span.TotalDays < 1
                ? $"{(int)span.TotalHours}h{span.Minutes:00}m"
                : $"{(int)span.TotalDays}d{span.Hours}h";
}
