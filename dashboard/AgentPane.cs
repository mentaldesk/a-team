namespace ATeam.Dashboard;

/// <summary>One agent: its state in the title, and a tail of its latest session below.</summary>
public sealed class AgentPane : FrameView
{
    private readonly string _stateDir;
    private readonly string _name;
    private readonly SessionLog _log = new();
    private readonly Label _why;
    private readonly TextView _body;

    public AgentPane(string team, string role, string stateDir)
    {
        _name = $"{team} · {role}";
        _stateDir = stateDir;
        Title = _name;
        CanFocus = true;
        _why = new Label { X = 0, Y = 0, Width = Dim.Fill() };
        _body = new TextView
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = true,
        };
        Add(_why, _body);
    }

    public void Refresh(DateTimeOffset now)
    {
        var state = AgentState.Read(_stateDir);
        Title = $"{(state.Running ? "●" : "○")} {_name} · {Describe(state, now)}";
        var why = state.Reasons.Count == 0 ? "" : "why: " + string.Join("; ", state.Reasons);
        if (_why.Text != why)
            _why.Text = why;

        if (!_log.Refresh(state.LogPath))
            return;
        _body.Text = _log.Lines.Count == 0 ? "(no session yet)" : string.Join('\n', _log.Lines);
        _body.MoveEnd();
    }

    private static string Describe(AgentState state, DateTimeOffset now)
    {
        if (state.LastStart is not { } start)
            return "never run";
        var ago = Ago(now - start);
        return state.Running ? $"running {ago}" : $"idle, ran {ago} ago";
    }

    private static string Ago(TimeSpan span) => span.TotalMinutes < 1
        ? "<1m"
        : span.TotalHours < 1
            ? $"{(int)span.TotalMinutes}m"
            : span.TotalDays < 1
                ? $"{(int)span.TotalHours}h{span.Minutes:00}m"
                : $"{(int)span.TotalDays}d{span.Hours}h";
}
