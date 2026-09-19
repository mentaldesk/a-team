namespace ATeam.Dashboard;

public sealed class DashboardWindow : Window
{
    private const int DispatchLines = 4;
    private readonly List<AgentPane> _panes = [];
    private readonly TextView _dispatch;
    private readonly string _dispatchLog;

    public DashboardWindow(IReadOnlyList<(string Team, string Role)> agents, string stateRoot)
    {
        Title = "a-team";
        _dispatchLog = Path.Combine(stateRoot, "dispatch.log");

        for (var i = 0; i < agents.Count; i++)
        {
            var (team, role) = agents[i];
            var pane = new AgentPane(team, role, Path.Combine(stateRoot, team, role))
            {
                X = i == 0 ? 0 : Pos.Right(_panes[i - 1]),
                Y = 0,
                Width = i == agents.Count - 1 ? Dim.Fill() : Dim.Percent(100 / agents.Count),
                Height = Dim.Fill(DispatchLines + 2),
            };
            _panes.Add(pane);
            Add(pane);
        }

        var dispatchFrame = new FrameView
        {
            Title = "dispatcher",
            X = 0,
            Y = Pos.AnchorEnd(DispatchLines + 2),
            Width = Dim.Fill(),
            Height = DispatchLines + 2,
        };
        _dispatch = new TextView { Width = Dim.Fill(), Height = Dim.Fill(), ReadOnly = true };
        dispatchFrame.Add(_dispatch);
        Add(dispatchFrame);
    }

    public void Refresh()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pane in _panes)
            pane.Refresh(now);

        var tail = ReadTail(_dispatchLog, DispatchLines);
        if (_dispatch.Text != tail)
            _dispatch.Text = tail;
    }

    private static string ReadTail(string path, int count)
    {
        try
        {
            return string.Join('\n', File.ReadLines(path).TakeLast(count));
        }
        catch (IOException) { return "(the dispatcher hasn't run yet)"; }
    }
}
