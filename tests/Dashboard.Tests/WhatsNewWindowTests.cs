using System.Drawing;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

/// <summary>The window acting on what has become your move: reading it, showing it and taking you to it.</summary>
[Collection("StaticConfiguration")]
public class WhatsNewWindowTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);
    private const string Pitched = """[{"item": 133, "at": "2026-10-02T09:00:30Z", "who": "lead", "what": "Exploring → Pitched"}]""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");
    private readonly PlatformKeyBinding _quit = Application.DefaultKeyBindings![Command.Quit];
    private readonly List<string> _reads = [];
    private readonly Dictionary<string, string> _moves = new() { ["team0"] = "[]", ["team1"] = Pitched };
    private readonly List<WaitingItem> _opened = [];

    public void Dispose()
    {
        Application.SetDefaultKeyBinding(Command.Quit, _quit);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Something_new_on_a_team_reads_that_team_again_and_shows_in_the_menu_bar_from_any_area()
    {
        using var window = Open(Area.Dashboard);

        Settle(window);

        Assert.Equal(["team1"], _reads);
        Assert.Equal("◇ #133", window.Alert.Alerting);
        Assert.Contains(window.Work.Items, item => item is { Team: "team1", Number: 133 });
    }

    [Theory]
    [InlineData(Area.Work)]
    [InlineData(Area.Dashboard)]
    [InlineData(Area.Overseer)]
    public void The_alert_sits_at_the_right_end_of_the_menu_bar_in_every_area(Area area)
    {
        using var window = Open(area);

        Settle(window);

        Assert.Equal(window.Menu.Frame.Y, window.Alert.Frame.Y);
        Assert.Equal(window.Viewport.Width, window.Alert.Frame.Right);
        Assert.Equal("◇ #133", window.Alert.Text);
        Assert.True(MenuTitlesEnd(window) + 3 <= window.Alert.Frame.X, $"{window.Alert.Frame} runs into the menu titles");
    }

    [Fact]
    public void The_status_bar_says_nothing_of_the_alert()
    {
        using var window = Open(Area.Dashboard);

        Settle(window);

        Assert.DoesNotContain("#133", window.Status.Says + window.Status.Message.Says + window.Status.State.Text);
    }

    [Fact]
    public void On_a_narrow_window_the_menu_titles_stay_whole_and_the_alert_is_cut_from_the_left()
    {
        using var window = Open(Area.Dashboard);
        Settle(window);
        var titles = MenuTitlesEnd(window);

        LayOut(window, titles + 3 + 4);

        Assert.Equal(titles, MenuTitlesEnd(window));
        Assert.Equal(titles + 3, window.Alert.Frame.X);
        Assert.Equal("…133", window.Alert.Text);
    }

    [Fact]
    public void The_alert_s_key_is_listed_under_Everywhere()
    {
        using var window = Open(Area.Dashboard);

        Settle(window);

        Assert.Contains(window.Keys.Everywhere.Groups.Single(group => group.Heading == "View").Rows,
            row => row is { Keys: "u", Label: "Go to what's new" });
    }

    [Fact]
    public void Nothing_new_reads_nothing_and_says_nothing()
    {
        _moves["team1"] = "[]";
        using var window = Open(Area.Dashboard);

        Settle(window);

        Assert.Empty(_reads);
        Assert.Equal("", window.Alert.Alerting);
        Assert.DoesNotContain(window.Commands.Enabled, command => command.Id == "work.whatsNew");
    }

    [Fact]
    public void Every_team_s_record_is_read_again_only_once_it_has_been_written_to()
    {
        var asked = 0;
        using var window = Open(Area.Dashboard, onMoves: () => asked++);
        Settle(window);
        Assert.Equal(2, asked);

        var record = Path.Combine(_root, "history.db");
        File.WriteAllText(record, "");
        File.SetLastWriteTimeUtc(record, new DateTime(2026, 10, 2, 9, 1, 0, DateTimeKind.Utc));
        Settle(window);

        Assert.Equal(4, asked);
    }

    [Fact]
    public void U_opens_Work_on_the_team_s_tab_with_the_new_card_selected()
    {
        using var window = Open(Area.Dashboard);
        Settle(window);

        window.NewKeyDownEvent(new Key('u'));
        Settle(window);

        Assert.Equal(Area.Work, window.CurrentArea);
        Assert.Equal("team1", window.Work.Current?.Team);
        Assert.Equal(133, window.Work.Selected?.Number);
    }

    [Fact]
    public void Clicking_the_alert_goes_there_too()
    {
        using var window = Open(Area.Overseer);
        Settle(window);

        window.Alert.InvokeCommand(Command.Accept);
        Settle(window);

        Assert.Equal(Area.Work, window.CurrentArea);
        Assert.Equal(133, window.Work.Selected?.Number);
    }

    [Fact]
    public void Go_to_what_s_new_is_in_the_View_menu_on_u()
    {
        using var window = Open(Area.Work);

        Assert.Contains(window.MenuItems, item => item.Id == "work.whatsNew");
        Assert.Equal(new Key('u'), window.Commands.KeyFor("work.whatsNew"));
        Assert.Contains(AppMenu.Layout.Single(menu => menu.Title == "_View").Ids, id => id == "work.whatsNew");
    }

    [Fact]
    public void A_new_card_wears_a_dot_before_its_title_until_you_open_it()
    {
        using var window = Open(Area.Work);
        Settle(window);
        var pitches = window.Work.Lanes[1].Columns[1];
        Assert.Equal("#133  ● Notice when open files change on disk", pitches.CardText.Single());

        window.Work.Focus(window.Work.Items.Single(item => item.Number == 133), false);
        window.NewKeyDownEvent(Key.Enter);
        Settle(window);

        Assert.Equal([133], _opened.Select(item => item.Number));
        Assert.Equal("#133  Notice when open files change on disk", pitches.CardText.Single());
        Assert.Equal("", window.Alert.Alerting);
        Assert.Empty(window.News.Items);
    }

    [Fact]
    public void Leaving_its_gate_drops_it_from_the_alert()
    {
        using var window = Open(Area.Dashboard);
        Settle(window);

        _moves["team1"] = Pitched[..^1] + """, {"item": 133, "at": "2026-10-02T09:00:40Z", "who": "you", "what": "Pitched → Approved"}]""";
        var record = Path.Combine(_root, "history.db");
        File.WriteAllText(record, "");
        Settle(window);

        Assert.Equal("", window.Alert.Alerting);
    }

    private void Settle(DashboardWindow window)
    {
        for (var i = 0; i < 4; i++)
        {
            window.Refresh();
            LayOut(window);
        }
    }

    private static void LayOut(DashboardWindow window, int width = 120)
    {
        for (var i = 0; i < 2; i++)
        {
            window.Frame = new Rectangle(0, 0, width, 30);
            window.Layout(new Size(width, 30));
        }
    }

    private static int MenuTitlesEnd(DashboardWindow window) =>
        window.Menus.Where(menu => menu.SuperView is not null).Max(menu => menu.Frame.Right);

    private DashboardWindow Open(Area area, Action? onMoves = null)
    {
        Directory.CreateDirectory(_root);
        var config = Path.Combine(_root, "config");
        return new DashboardWindow(
            [("team0", "lead"), ("team1", "lead")],
            _root,
            new DashboardSettings(config),
            new TeamConfigs(config),
            _ => Task.FromResult<string?>(null),
            team =>
            {
                _reads.Add(team);
                return Task.FromResult(new Reading(Waiting(team), null));
            },
            _ => Task.FromResult(new Reading("""{"body": "What it's about"}""", null)),
            _ => { },
            (item, _, _, _, _, _, _, _, _, _, _, _) =>
            {
                _opened.Add(item);
                return null;
            },
            area,
            IconStyle.Unicode,
            clock: new Clock(),
            readMoves: (team, since) =>
            {
                Assert.Equal(Start, since);
                onMoves?.Invoke();
                return Task.FromResult(new Reading(_moves[team], null));
            },
            whatsNew: new WhatsNew(Start));
    }

    private static string Waiting(string team) => team == "team0"
        ? """
          [{"number": 107, "title": "When the dashboard goes quiet", "status": "Pitched",
            "url": "https://github.com/mentaldesk/team0/issues/107", "team": "team0", "turn": "you", "priority": "High", "pitch": true}]
          """
        : """
          [{"number": 133, "title": "Notice when open files change on disk", "status": "Pitched",
            "url": "https://github.com/mentaldesk/team1/issues/133", "team": "team1", "turn": "you", "priority": "Low", "pitch": true}]
          """;

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Start.AddMinutes(1);
    }
}
