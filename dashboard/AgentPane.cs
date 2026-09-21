namespace ATeam.Dashboard;

/// <summary>One agent: running or idle and its timing, why it last started, and a tail of its latest session.</summary>
public sealed class AgentPane : FrameView
{
    private readonly string _stateDir;
    private readonly string _name;
    private readonly SessionLog _log = new();
    private readonly Label _status;
    private readonly Label _why;
    private readonly LogView _body;
    private bool _running;
    private string _timing = "";

    public AgentPane(string team, string role, string stateDir, bool expandToolCalls)
    {
        Team = team;
        _name = $"{team} · {role}";
        _stateDir = stateDir;
        CanFocus = true;
        _status = new Label { X = 0, Y = 0, Width = Dim.Fill() };
        _why = new Label { X = 0, Y = 1, Width = Dim.Fill() };
        _body = new LogView { X = 0, Y = 2, Width = Dim.Fill(), Height = Dim.Fill(), Expanded = expandToolCalls };
        Add(_status, _why, _body);
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
        _running = state.Running;
        Paused = paused;
        _timing = Describe(state, now, nextCheck, paused);
        UpdateHeader();

        var why = state.Reasons.Count == 0 ? "" : "why: " + string.Join("; ", state.Reasons);
        if (_why.Text != why)
            _why.Text = why;

        if (_log.Refresh(state.LogPath))
            _body.Lines = _log.Lines.Count == 0
                ? [new LogLine("(no session yet)", LogLineKind.Prose)]
                : [.. _log.Lines];
    }

    private void UpdateHeader()
    {
        var title = Header(_name, HasFocus, _running, Paused, _body.Following, _body.Expanded);
        if (Title != title)
            Title = title;
        if (_status.Text != _timing)
            _status.Text = _timing;
    }

    internal static string Header(string name, bool selected, bool running, bool paused, bool following, bool expanded) =>
        $"{(selected ? "▶ " : "")}{Glyph(running, paused)} {name}{(expanded ? " [tool calls]" : "")}{(following ? "" : " [scrolled]")}";

    private static string Glyph(bool running, bool paused) => paused ? "⏸" : running ? "●" : "○";

    internal static string Describe(AgentState state, DateTimeOffset now, DateTimeOffset? nextCheck, bool paused)
    {
        if (state.Running)
            return state.LastStart is { } started ? $"running {Clock(now - started)}" : "running";
        var ran = state.LastStart is { } last ? $"ran {Ago(now - last)} ago" : "never run";
        return $"{ran} · {(paused ? "paused" : NextCheck(now, nextCheck))}";
    }

    private static string NextCheck(DateTimeOffset now, DateTimeOffset? nextCheck) => nextCheck switch
    {
        null => "dispatcher hasn't run",
        { } next when next - now >= TimeSpan.Zero => $"next check {Clock(next - now)}",
        { } next when now - next < TimeSpan.FromMinutes(1) => "checking now",
        _ => "dispatcher not running",
    };

    private static string Clock(TimeSpan span) => span.TotalHours >= 1
        ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
        : $"{span.Minutes}:{span.Seconds:00}";

    private static string Ago(TimeSpan span) => span.TotalMinutes < 1
        ? "<1m"
        : span.TotalHours < 1
            ? $"{(int)span.TotalMinutes}m"
            : span.TotalDays < 1
                ? $"{(int)span.TotalHours}h{span.Minutes:00}m"
                : $"{(int)span.TotalDays}d{span.Hours}h";
}
