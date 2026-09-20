using System.Reflection;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

public sealed class DashboardWindow : Window
{
    private const int DispatchLines = 4;
    private static readonly Key Settings = new Key(',').WithCtrl;
    private readonly List<AgentPane> _panes = [];
    private readonly TextView _dispatch;
    private readonly string _dispatchLog;
    private readonly string _nextPass;

    public DashboardWindow(IReadOnlyList<(string Team, string Role)> agents, string stateRoot)
    {
        Title = $"a-team {Version()} · Tab/arrows: select agent · PgUp/PgDn/Home/End: scroll · Ctrl+,: settings · Esc: quit";
        _dispatchLog = Path.Combine(stateRoot, "dispatch.log");
        _nextPass = Path.Combine(stateRoot, "next-pass");

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
            CanFocus = false,
        };
        _dispatch = new TextView { Width = Dim.Fill(), Height = Dim.Fill(), ReadOnly = true, CanFocus = false };
        dispatchFrame.Add(_dispatch);
        Add(dispatchFrame);
    }

    public void Refresh()
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? nextCheck = long.TryParse(ReadText(_nextPass), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
        foreach (var pane in _panes)
            pane.Refresh(now, nextCheck);

        var tail = ReadTail(_dispatchLog, DispatchLines);
        if (_dispatch.Text != tail)
            _dispatch.Text = tail;
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Settings)
            return OpenSettings();
        if (key == Key.Tab || key == Key.CursorRight || key == Key.CursorDown)
            return Select(+1);
        if (key == Key.Tab.WithShift || key == Key.CursorLeft || key == Key.CursorUp)
            return Select(-1);

        var pane = Selected();
        if (pane is null)
            return base.OnKeyDown(key);
        if (key == Key.PageUp)
            pane.Page(-1);
        else if (key == Key.PageDown)
            pane.Page(+1);
        else if (key == Key.Home)
            pane.Home();
        else if (key == Key.End)
            pane.End();
        else
            return base.OnKeyDown(key);
        return true;
    }

    private bool OpenSettings()
    {
        if (App is null)
            return false;
        SettingsDialog.Show(App, ThemeSetting.Live());
        return true;
    }

    private bool Select(int step)
    {
        if (_panes.Count == 0)
            return false;
        var current = Selected() is { } pane ? _panes.IndexOf(pane) : -1;
        _panes[(current + step + _panes.Count) % _panes.Count].SetFocus();
        return true;
    }

    private AgentPane? Selected() => _panes.FirstOrDefault(pane => pane.HasFocus);

    private static string Version() =>
        typeof(DashboardWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "";

    private static string? ReadText(string path)
    {
        try { return File.ReadAllText(path).Trim(); }
        catch (IOException) { return null; }
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
