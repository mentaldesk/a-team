using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;

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
    Held,
    StoppedByYou,
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
    private string _name;
    private readonly SessionLog _log = new();
    private readonly Label _above;
    private readonly Label _below;
    private readonly Label _statusRow;
    private readonly Label _why;
    private readonly LogView _body;
    private PaneStatus _status = PaneStatus.NeverRun;
    private IconStyle _icons = IconStyle.Unicode;
    private string _timing = "";
    private readonly RunSelection _runs = new();
    private string? _shownLog;
    private DateTimeOffset _now;
    private (DateTimeOffset? NextCheck, bool Paused, bool Held) _refreshed;

    public AgentPane(string team, string role, string stateDir, bool expandToolCalls)
    {
        Team = team;
        Role = role;
        _name = $"{team} · {role}";
        _stateDir = stateDir;
        CanFocus = true;
        _above = new Label { X = 0, Y = 0, Width = Dim.Fill(), Height = 0 };
        _statusRow = new Label { X = 0, Y = Pos.Bottom(_above), Width = Dim.Fill() };
        _why = new Label { X = 0, Y = Pos.Bottom(_statusRow), Width = Dim.Fill() };
        _body = new LogView { X = 0, Y = Pos.Bottom(_why), Width = Dim.Fill(), Height = Dim.Fill(), Expanded = expandToolCalls };
        _below = new Label { X = 0, Y = Pos.Bottom(_body), Width = Dim.Fill(), Height = 0 };
        Add(_statusRow, _why, _body, _above, _below);
        HasFocusChanged += (_, _) => UpdateHeader();
        FrameChanged += (_, _) => UpdateHeader();
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

    public bool Selects
    {
        get => _body.Selects;
        set
        {
            _body.Selects = value;
            UpdateHeader();
        }
    }

    public void MoveLine(int step, bool extend)
    {
        _body.MoveSelection(step, extend);
        UpdateHeader();
    }

    public LogCopy CopySelection() => _body.CopySelection();

    public LogCopy CopyAll() => _body.CopyAll();

    /// <summary>Draws the title's and the session's icons from the vocabulary in effect.</summary>
    public void ShowIcons(IconStyle style)
    {
        _body.ShowIcons(style);
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

    internal string Role { get; }

    internal bool Paused { get; private set; }

    internal bool Running { get; private set; }

    internal bool Held { get; private set; }

    internal string? SessionId => _log.SessionId;

    /// <summary>Shows the next run up or down; false past either end, so the selection can move on to the next pane.</summary>
    public bool MoveRun(int step)
    {
        if (!_runs.Move(step))
            return false;
        Refresh(_now, _refreshed.NextCheck, _refreshed.Paused, _refreshed.Held);
        return true;
    }

    public void Refresh(DateTimeOffset now, DateTimeOffset? nextCheck, bool paused, bool held)
    {
        _refreshed = (nextCheck, paused, held);
        var role = AgentState.Read(_stateDir);
        _runs.Update(role.Runs, role.Latest);
        var state = _runs.Current is { } run ? AgentState.Read(run.Dir) with { Stopped = role.Stopped } : role;
        Paused = paused;
        Running = role.Running;
        Held = held;
        _name = Name(Team, Role, state.Task);

        var switched = state.LogPath != _shownLog;
        _shownLog = state.LogPath;
        if (_log.Refresh(state.LogPath) || switched)
            _body.Lines = _log.Lines.Count == 0
                ? [new LogLine("(no session yet)", LogLineKind.Prose)]
                : [.. _log.Lines];

        _status = Status(state, paused, held, _log.Verdict);
        _timing = Describe(state, now, nextCheck, _status, held);
        _now = now;
        UpdateHeader();

        var why = state.Reasons.Count == 0 ? "" : "why: " + string.Join("; ", state.Reasons);
        if (_why.Text != why)
            _why.Text = why;
    }

    private void UpdateHeader()
    {
        var title = Header(_name, HasFocus, _status, _body.Following, _body.Expanded, _icons, Frame.Width > 0 ? Frame.Width - TitleMargin : int.MaxValue).ToString();
        if (Title != title)
            Title = title;
        if (_statusRow.Text != _timing)
            _statusRow.Text = _timing;
        var shown = Math.Max(0, _runs.Shown);
        Fill(_above, [.. _runs.Runs.Take(shown)]);
        Fill(_below, [.. _runs.Runs.Skip(shown + 1)]);

        var schemes = SchemesFor(_status, _timing);
        Use(this, schemes.Frame);
        Use(_above, schemes.Body);
        Use(_below, schemes.Body);
        Use(_statusRow, schemes.Status);
        Use(_why, schemes.Why);
        Use(_body, schemes.Body);
    }

    private void Fill(Label label, IReadOnlyList<DevRun> runs)
    {
        var bars = string.Join("\n", Bars(runs, _now, _icons, Math.Max(0, Frame.Width - 2)));
        if (label.Text == bars)
            return;
        label.Text = bars;
        label.Height = runs.Count;
        if (label == _below)
            _body.Height = Dim.Fill(runs.Count);
    }

    private static void Use(View view, string scheme)
    {
        if (view.SchemeName != scheme)
            view.SchemeName = scheme;
    }

    /// <summary>A held role that isn't running wins, then pause, then running, then how the last run went.</summary>
    internal static PaneStatus Status(AgentState state, bool paused, bool held, RunVerdict verdict) =>
        held && !state.Running ? (verdict == RunVerdict.None ? PaneStatus.StoppedByYou : PaneStatus.Held)
            : paused ? PaneStatus.Paused
            : state.Running ? PaneStatus.Running
            : verdict switch
            {
                RunVerdict.Ok => PaneStatus.Ok,
                RunVerdict.Error => PaneStatus.Failed,
                _ => state.LastStart is null ? PaneStatus.NeverRun : PaneStatus.CutShort,
            };

    /// <summary>The border's corners and the space either side of the title.</summary>
    private const int TitleMargin = 4;

    internal static string Name(string team, string role, RunTask? task) =>
        task is null ? $"{team} · {role}" : $"{team} · {role} · #{task.Number} {task.Title}";

    /// <summary>The name gives way to fit <paramref name="width"/>; the icons and the view markers never do.</summary>
    internal static PaneTitle Header(
        string name, bool selected, PaneStatus status, bool following, bool expanded, IconStyle style,
        int width = int.MaxValue)
    {
        var icons = (selected ? Icons.Field(Icon.Selected, style) : "") + Icons.Field(Icons.For(status), style);
        var markers = $"{(expanded ? " [tool calls]" : "")}{(following ? "" : " [scrolled]")}";
        return new(icons, Elide(name, width - icons.GetColumns() - markers.Length) + markers);
    }

    /// <summary>One line per live run not shown: its icon, its task, and how long it has been running, at the right.</summary>
    internal static IReadOnlyList<string> Bars(IReadOnlyList<DevRun> others, DateTimeOffset now, IconStyle style, int width)
    {
        var icon = Icons.Field(Icons.For(PaneStatus.Running), style);
        return [.. others.Select(run =>
        {
            var task = run.Task is { } t ? $"#{t.Number} {t.Title}" : "a run";
            var elapsed = run.Started is { } started ? Clock(now - started) : "";
            var room = width - icon.GetColumns() - elapsed.Length - 1;
            var name = Elide(task, room);
            return $"{icon}{name}{new string(' ', Math.Max(1, room - name.Length + 1))}{elapsed}";
        })];
    }

    private static string Elide(string text, int width) =>
        width < 1 ? "" : text.Length <= width ? text : string.Concat(text.AsSpan(0, width - 1), "…");

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

    internal static string Describe(
        AgentState state, DateTimeOffset now, DateTimeOffset? nextCheck, PaneStatus status, bool held = false)
    {
        if (state.Running)
            return (state.LastStart is { } started ? $"running {Clock(now - started)}" : "running") + (held ? " · held" : "");
        if (status == PaneStatus.StoppedByYou)
            return state.Stopped is { } stopped ? $"stopped by you {Ago(now - stopped)} ago · held" : "stopped by you · held";
        var ran = state.LastStart is { } last ? $"ran {Ago(now - last)} ago" : "never run";
        var cut = status == PaneStatus.CutShort ? " · cut short" : "";
        var next = status switch
        {
            PaneStatus.Paused => "paused",
            PaneStatus.Held => "held",
            _ => NextCheck(now, nextCheck),
        };
        return $"{ran}{cut} · {next}";
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
