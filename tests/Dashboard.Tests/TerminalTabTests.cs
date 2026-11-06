using System.Drawing;
using MentalDesk.Tui;
using MentalDesk.Tui.Chrome;
using MentalDesk.Tui.Shell;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Drivers;

namespace ATeam.Dashboard.Tests;

/// <summary>What the terminal's tab says, and when it gets the terminal's own title back.</summary>
[Collection("StaticConfiguration")]
public class TerminalTabTests : IDisposable
{
    private const string Push = "\x1b[22;0t";
    private const string Pop = "\x1b[23;0t";
    private const string Osc = "\x1b]0;";

    private const string Waiting =
        """
        [{"number": 107, "title": "One", "status": "Pitched", "url": "https://x/107", "team": "team0", "turn": "you", "pitch": true},
         {"number": 108, "title": "Two", "status": "In review", "url": "https://x/108", "team": "team0", "turn": "you"},
         {"number": 469, "title": "Upstream", "status": "Ready", "url": "https://x/469", "team": "team0", "turn": "upstream",
          "upstream": "tui-style-guide#63: open", "reason": "waiting on tui-style-guide#63: open"}]
        """;

    private const string OneLeft =
        """
        [{"number": 108, "title": "Two", "status": "In review", "url": "https://x/108", "team": "team0", "turn": "you"}]
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");
    private readonly List<string> _written = [];
    private readonly TerminalTitle _title;
    private string _waiting = Waiting;

    public TerminalTabTests() => _title = new TerminalTitle(_written.Add).Names([]);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private IEnumerable<string> Titles => _written
        .Where(written => written.StartsWith(Osc) || written is Push or Pop)
        .Select(written => written.StartsWith(Osc) ? written[Osc.Length..^1] : written);

    [Theory]
    [InlineData(new string[0], 0, 0, "a-team")]
    [InlineData(new string[0], 3, 0, "(3) a-team")]
    [InlineData(new string[0], 0, 1, "⚠ 1 a-team")]
    [InlineData(new string[0], 3, 1, "⚠ 1 (3) a-team")]
    [InlineData(new[] { "TuiCode" }, 2, 0, "(2) TuiCode — a-team")]
    [InlineData(new[] { "TuiCode" }, 0, 0, "TuiCode — a-team")]
    [InlineData(new[] { "TuiCode", "a-team" }, 2, 0, "(2) a-team")]
    public void It_reads(string[] opened, int waiting, int errors, string text)
    {
        var title = new TerminalTitle(_ => { }).Names(opened);
        title.Waiting = waiting;
        title.UnseenErrors = errors;

        Assert.Equal(text, title.Text);
    }

    [Fact]
    public void Its_count_is_Work_s_without_what_waits_upstream_and_follows_the_board()
    {
        using var window = Open(Area.Work);
        _title.Show();
        Settle(window);

        Assert.StartsWith("Work · 2 waiting on you", window.WorkTitle);
        Assert.Equal("(2) a-team", _title.Text);

        _waiting = OneLeft;
        window.Commands.Execute("work.refresh");
        Settle(window);

        Assert.StartsWith("Work · 1 waiting on you", window.WorkTitle);
        Assert.Equal(["(2) a-team", "(1) a-team"], Titles.Where(title => title != "a-team"));
    }

    [Fact]
    public void In_Overseer_its_count_follows_the_dispatcher_s_sync()
    {
        using var window = Open(Area.Overseer);
        window.Refresh();
        Settle(window);
        Assert.Equal(2, _title.Waiting);

        _waiting = OneLeft;
        var synced = Path.Combine(_root, "team0", "detail-synced");
        Directory.CreateDirectory(Path.GetDirectoryName(synced)!);
        File.WriteAllText(synced, "");
        File.SetLastWriteTimeUtc(synced, DateTime.UtcNow.AddSeconds(5));
        Settle(window);

        Assert.Equal(1, _title.Waiting);
    }

    [Fact]
    public void An_error_message_counts_until_Messages_opens_and_an_info_message_does_not()
    {
        using var window = Open(Area.Work);

        window.Status.ShowMessage("Copied 3 lines");
        Assert.Equal(0, _title.UnseenErrors);

        window.Status.ShowNews("team0: rate limit", Severity.Error);
        window.Status.ShowNews("team1: rate limit", Severity.Error);
        Assert.Equal(2, _title.UnseenErrors);

        using var messages = new MessagesDialog(window.Status);
        Assert.Equal(0, _title.UnseenErrors);
    }

    [Fact]
    public void The_terminal_s_own_title_is_back_for_each_handover_and_at_the_end()
    {
        var handed = new AttachHandover("team0", "dev");
        var shows = new Queue<Handover?>([handed, null]);
        string? atHandover = null;

        TerminalTab.Run(_title, _ =>
        {
            _title.Waiting += 2;
            return shows.Dequeue();
        }, handover =>
        {
            atHandover = Tab("my shell");
            _written.Add($"{Osc}vim\x07");
            return handover with { Failure = "exited 1" };
        });

        Assert.Equal("my shell", atHandover);
        Assert.Equal([Push, "a-team", "(2) a-team", Pop, Push, "vim", "(2) a-team", "(4) a-team", Pop], Titles);
        Assert.Equal("my shell", Tab("my shell"));
    }

    [Fact]
    public void It_is_given_back_when_the_app_throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TerminalTab.Run(_title, _ => throw new InvalidOperationException(), handover => handover));

        Assert.Equal([Push, "a-team", Pop], Titles);
    }

    [Fact]
    public void Each_window_gets_what_the_handover_brought_back_and_starts_with_no_errors()
    {
        var shows = new Queue<Handover?>([new AttachHandover("team0", "dev"), null]);
        Handover? back = null;
        _title.UnseenErrors = 1;

        TerminalTab.Run(_title, handed =>
        {
            back = handed;
            return shows.Dequeue();
        }, handover => handover with { Failure = "exited 1" });

        Assert.Equal("exited 1", back?.Failure);
        Assert.Equal([Push, "⚠ 1 a-team", Pop, Push, "a-team", Pop], Titles);
    }

    [Fact]
    public void A_dialog_opening_leaves_the_title_as_it_was()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var titled = TerminalTitle.TakeOver(app);
        _title.Show();
        _written.Clear();
        using var dialog = new AboutDialog("1.2.3");

        app.Begin(dialog);
        dialog.Layout();

        Assert.False(dialog.Border.Settings.HasFlag(BorderSettings.TerminalTitle));
        Assert.Empty(Titles);
    }

    private string Tab(string own)
    {
        var tab = own;
        Stack<string> saved = [];
        foreach (var written in Titles)
        {
            if (written == Push)
                saved.Push(tab);
            else if (written == Pop)
                tab = saved.Pop();
            else
                tab = written;
        }
        return tab;
    }

    private DashboardWindow Open(Area area)
    {
        Directory.CreateDirectory(_root);
        var config = Path.Combine(_root, "config");
        return new DashboardWindow(
            [("team0", "lead")],
            _root,
            new DashboardSettings(config),
            new TeamConfigs(config),
            _ => Task.FromResult<string?>(null),
            _ => Task.FromResult(new Reading(_waiting, null)),
            _ => Task.FromResult(new Reading("{}", null)),
            Opening.Opened,
            (_, _, _, _, _, _, _, _, _, _, _, _) => null,
            area,
            IconStyle.Unicode,
            readBoard: _ => Task.FromResult(new Reading("[]", null)),
            terminalTitle: _title);
    }

    private static void Settle(DashboardWindow window)
    {
        for (var i = 0; i < 4; i++)
        {
            window.Refresh();
            window.Frame = new Rectangle(0, 0, 120, 30);
            window.Layout(new Size(120, 30));
        }
    }
}
