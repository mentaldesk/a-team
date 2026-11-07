using System.Globalization;
using System.Reflection;
using AssistantPane = MentalDesk.Tui.Assistant.AssistantPane;
using MentalDesk.Tui;
using MentalDesk.Tui.Chrome;
using MentalDesk.Tui.Commands;
using MentalDesk.Tui.Keys;
using MentalDesk.Tui.Links;
using MentalDesk.Tui.Shell;
using Terminal.Gui;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;

namespace ATeam.Dashboard;

public sealed partial class DashboardWindow
{
    /// <summary>Reads the record after the cards, so it counts the read that has just been taken.</summary>
    private void ReadTrend()
    {
        if (_options.ReadTrend is not null && _trending is null)
            _trending = Task.WhenAll(_teamNames.Select(team => _options.ReadTrend(team)));
    }

    /// <summary>A trend that couldn't be read leaves the title with what's waiting now.</summary>
    private void SettleTrend()
    {
        if (_trending is not { IsCompleted: true } read)
            return;
        _trending = null;
        var trends = read.Status == TaskStatus.RanToCompletion ? read.Result.Select(WorkTrend.Of).ToList() : null;
        _trends = trends is null || trends.Contains(null) ? [] : [.. trends.OfType<WorkTrend>()];
    }

    private void ShowTitle()
    {
        int? waiting = _readAt is null ? null : _work.Items.Count(item => item.WaitsOnYou);
        if (waiting is { } count)
            _terminalTitle.Waiting = count;
        var title = WorkTrend.Title(waiting, _trends, _clock.GetUtcNow());
        if (_reading is not null && !_work.Unread)
            title += " · Reading…";
        else if (_syncing is not null)
            title += " · Syncing…";
        if (_title.Text != title)
            _title.Text = title;
    }

    internal string WorkTitle => _title.Text;

    /// <summary>Where you are and when it was read, then what's running or what's selected, under any message.</summary>
    private void ShowMessage()
    {
        if (_hintsForSecret != AskingSecret() is not null)
            ShowKeysHint();
        var resting =
            _progress is { Length: > 0 } ? _progress
            : _writes.Status() is { Length: > 0 } writing ? writing
            : _pass.Progress is { } passing ? passing
            : _dispatcherOpen ? ""
            : _area == Area.Work && _work.Selected is { } card && (card.Reason.Length > 0 || card.Holding.Count > 0 || card.Facts.Length > 0) ? card.Line
            : _area == Area.Work && _work.Region is { } region ? region
            : _area == Area.Overseer && _overseer.Region is { } chip ? chip
            : "";
        var stamp = _dispatcherOpen ? "" : _area switch
        {
            Area.Work => _work.Current is { } lane && _synced.TryGetValue(lane.Team, out var synced) ? synced.Stamp(_clock.GetUtcNow()) : "",
            _ => Stamped(BoardReadAt(), _clock.GetUtcNow()),
        };
        var place = InAssistant ? "Assistant" : _dispatcherOpen ? "Dispatcher log" : _area switch
        {
            Area.Work => _work.OnlyMine ? MyItems : AllItems,
            _ => "Overseer",
        };
        var focus = string.Join(" · ", new[] { place, stamp }.Where(part => part.Length > 0));
        if (_bar.FocusWord != focus)
            _bar.SetFocusWord(focus);
        if (_bar.RestingText != resting)
            _bar.RestingText = resting;
    }

    /// <summary>When the selected lane's board was last read, or the latest board where none is selected.</summary>
    private DateTimeOffset? BoardReadAt() =>
        _overseer.Board.Selected is { } place && _boardReadAt.TryGetValue(place.Team, out var read) ? read
        : _boardReadAt.Count > 0 ? _boardReadAt.Values.Max() : null;

    private void Reply((string Text, Schemes Scheme) said) => Reply(said.Text, SeverityOf(said.Scheme));

    private StatusMessage Reply(string text, Severity severity = Severity.Info) => _bar.ShowMessage(FirstLine(text), severity);

    private StatusMessage Announce(string text, Severity severity = Severity.Info) => _bar.ShowNews(FirstLine(text), severity);

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";

    /// <summary>Why a board read failed, without the name of the script that said so.</summary>
    private static string Reason(string failure) => FirstLine(failure) is var line && line.StartsWith("board.sh: ", StringComparison.Ordinal)
        ? line["board.sh: ".Length..]
        : line;

    private static Severity SeverityOf(Schemes scheme) => scheme == Schemes.Error ? Severity.Error : Severity.Info;

    private void OpenMessages()
    {
        if (App is not { } app)
            return;
        using var dialog = new MessagesDialog(_bar).Routed();
        DialogHost.Run(app, dialog);
    }
}
