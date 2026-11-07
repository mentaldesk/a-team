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
    public void Refresh()
    {
        _attention.Browsing(_area == Area.Work ? _work.Current?.Team : null);
        Settle();
        SettleWrites();
        SettleTeamReads();
        SettleMoves();
        ReadMoves();
        SettleBoards();
        SettleSyncs();
        SettleTrend();
        ShowTitle();
        var now = _clock.GetUtcNow();
        if (_area == Area.Work && Uncovered && (_askedAt is not { } asked || now - asked >= ReadEvery))
            ReadWaiting();
        if (_area == Area.Work && Uncovered && _readAt is not null)
            foreach (var team in _teamNames)
                Sync(team);
        ReadSynced();
        RefreshWatches(now);

        _broke = DispatcherState.Broke(_stateRoot, BrokeLines);
        ShowPassFailed();
        if (_dispatcherOpen)
        {
            ShowWholeLog();
            ShowDispatcher(now);
        }

        ShowOverseer(now);
        ShowSelection();
        Arrange();
        ShowNews();
        ShowAsking();
        _menu.Refresh();
        ShowMessage();
    }

    private void RefreshWatches(DateTimeOffset now)
    {
        DateTimeOffset? nextCheck = long.TryParse(ReadText(_nextPass), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
        var paused = _watches.Select(watch => watch.Team).Distinct().ToDictionary(team => team, _teams.IsPaused);
        _checks?.Follow(paused.Keys);
        ShowChecks();
        var passing = DispatcherState.Passing(_stateRoot);
        foreach (var watch in _watches)
            watch.Refresh(now, nextCheck, paused[watch.Team], _teams.IsHeld(watch.Team, watch.Role), _checks?.Fatal(watch.Team), passing);
    }

    /// <summary>A team's failed check stays on the bar as news until a later check passes.</summary>
    private void ShowChecks()
    {
        foreach (var (team, failed) in _checks?.Answered() ?? [])
        {
            if (_checkFailures.Remove(team, out var was))
                _bar.Withdraw(was);
            if (failed is not null)
                _checkFailures[team] = Announce(failed, Severity.Error);
        }
    }

    /// <summary>A pass that broke before it could log why stays on the bar until a later pass logs again.</summary>
    private void ShowPassFailed()
    {
        if (_broke.Count > 0 == _passFailed is not null)
            return;
        if (_passFailed is { } was)
            _bar.Withdraw(was);
        _passFailed = _broke.Count > 0 ? Announce($"dispatcher: last pass failed · {_broke[^1]}", Severity.Error) : null;
    }

    /// <summary>Reads every team's moves since start whenever the record has been written to.</summary>
    private void ReadMoves()
    {
        if (_options.ReadMoves is null || _moving is not null)
            return;
        var stamp = (Written(Path.Combine(_stateRoot, "history.db")), Written(Path.Combine(_stateRoot, "history.db-wal")));
        if (stamp == _recordStamp)
            return;
        _recordStamp = stamp;
        IReadOnlyList<string> teams = [.. _teamNames];
        _moving = (teams, Task.WhenAll(teams.Select(team => _options.ReadMoves(team, _news.Since))));
    }

    private static DateTime Written(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;

    /// <summary>A team with something new is read again, so its card is there when you get to it.</summary>
    private void SettleMoves()
    {
        if (_moving is not { Read.IsCompleted: true } moving)
            return;
        _moving = null;
        if (moving.Read.Status != TaskStatus.RanToCompletion)
            return;
        for (var i = 0; i < moving.Teams.Count; i++)
            if (moving.Read.Result[i].Failure is null or { Length: 0 }
                && _news.Read(moving.Teams[i], Dashboard.Move.Parse(moving.Read.Result[i].Output)))
                ReadTeam(moving.Teams[i]);
    }

    private void ReadTeam(string team)
    {
        if (!_teamReads.ContainsKey(team))
            _teamReads[team] = (_readWaiting(team), _clock.GetUtcNow());
    }

    /// <summary>A team's own read replaces only its cards.</summary>
    private void SettleTeamReads()
    {
        foreach (var (team, (read, asked)) in _teamReads.Where(entry => entry.Value.Read.IsCompleted).ToList())
        {
            _teamReads.Remove(team);
            SettleTeam(team, TeamReads.Of(read), asked);
        }
    }

    /// <summary>One team's read: its cards replace only its own, and one that failed says so in its tab, or on the
    /// bar where it keeps cards already on screen.</summary>
    private void SettleTeam(string team, Reading read, DateTimeOffset asked)
    {
        if (_readFailures.Remove(team, out var was))
            _bar.Withdraw(was);
        if (read.Failure is { Length: > 0 } failure)
        {
            if (_work.Fail(team, Reason(failure), RetryHint("work.retry"), () => RetryTeam(team)))
                _readFailures[team] = Announce($"{team}: {Reason(failure)}", Severity.Error);
            return;
        }
        var items = WaitingItem.Parse(read.Output);
        Sync(team);
        if (Synced.Parse(read.Output) is { } synced)
            _synced[team] = synced;
        _news.Read(team, items, asked);
        _work.Show(team, items);
        _work.ShowRed(RedMain.Parse(read.Output), team);
        GoToWaiting();
    }

    private void RetryTeam(string team)
    {
        _work.Reading(team);
        ReadTeam(team);
        ShowMessage();
    }

    private string RetryHint(string id) => $"{_commands.KeysFor(id)} retry";

    private void ShowNews()
    {
        _alert.Show(_news.Alert(_drawn));
        _work.ShowNew(_news.Items);
    }

    /// <summary>Work, on the newest new card's tab with it selected; where Work hasn't read it yet, once it has.</summary>
    private void GoToNew()
    {
        if (_news.Newest is not { } newest)
            return;
        Show(Area.Work);
        _work.Pick(newest.Team);
        _goingTo = newest;
        GoToWaiting();
    }

    private void GoToWaiting()
    {
        if (_goingTo is not { } going || _area != Area.Work)
            return;
        if (_work.Items.FirstOrDefault(item => item.Team == going.Team && item.Number == going.Number) is not { } item)
            return;
        _goingTo = null;
        _work.Pick(going.Team);
        _work.Focus(item, false);
        ShowMessage();
    }

    /// <summary>Opening a card is seeing it.</summary>
    private void Seen(WaitingItem item)
    {
        _news.Seen(item.Team, item.Number, _clock.GetUtcNow());
        ShowNews();
    }

    private void ShowDispatcher(DateTimeOffset now)
    {
        var state = DispatcherState.Read(_stateRoot, now, _home);
        if (_broke.Count > 0)
            state = state.Failed();
        var width = _dispatchFrame.Frame.Width > 0 ? _dispatchFrame.Frame.Width - TitleMargin : int.MaxValue;
        var title = state.Title(width);
        if (_dispatchFrame.Title != title)
            _dispatchFrame.Title = title;
        var scheme = state.Error ? ErrorScheme : BaseScheme;
        if (_dispatchFrame.SchemeName != scheme)
            _dispatchFrame.SchemeName = scheme;
    }

    /// <summary>Reads dispatch.log afresh only when it has changed: it is never trimmed.</summary>
    private void ShowWholeLog()
    {
        var file = new FileInfo(_dispatchLog);
        var stamp = file.Exists ? (file.Length, file.LastWriteTimeUtc) : default;
        if (stamp != _wholeRead || _whole.Count == 0)
        {
            _wholeRead = stamp;
            _whole = ReadTail(_dispatchLog, int.MaxValue);
        }
        IReadOnlyList<LogLine> lines = _broke.Count == 0
            ? _whole
            : [.. _whole, new LogLine(BrokeRule, LogLineKind.DispatchFailed), .. _broke.Select(line => new LogLine(line, LogLineKind.DispatchFailed))];
        if (!_dispatchAll.Lines.SequenceEqual(lines))
            _dispatchAll.Lines = lines;
    }

    /// <summary>When Overseer's boards were last read, for the status bar.</summary>
    internal static string Stamped(DateTimeOffset? at, DateTimeOffset now) =>
        at is { } read ? $"read {Ages.Ago(now - read)} ago" : "";

    /// <summary>Reads every team's gates at once. A second go while one is running is refused, not queued.</summary>
    private void ReadWaiting()
    {
        _askedAt = _clock.GetUtcNow();
        if (_reading is null)
        {
            _readingAsked = _askedAt.Value;
            _activityRead = Activity();
            _reading = new TeamReads(_teamNames, _readWaiting);
            foreach (var team in _teamNames)
                _work.Reading(team);
        }
        ShowTitle();
        ShowMessage();
    }

    private string[] Activity() => [.. _watches.Select(watch => watch.Activity)];

    /// <summary>Reads every team's whole board at once, and what's waiting for Work's reasons with it. A second go
    /// while one is running is refused, not queued.</summary>
    private void ReadBoards()
    {
        if (_options.ReadBoard is null)
            return;
        if (_boarding is null)
        {
            _boarding = new TeamReads(_teamNames, _options.ReadBoard);
            foreach (var team in _teamNames)
                _overseer.Reading(team);
        }
        if (_reading is null)
            ReadWaiting();
        ShowMessage();
    }

    /// <summary>Reads again only the boards that couldn't be read.</summary>
    private void RetryBoards(IReadOnlyList<string> teams)
    {
        if (_options.ReadBoard is null)
            return;
        _boarding ??= new TeamReads([], _options.ReadBoard);
        foreach (var team in teams)
        {
            _overseer.Reading(team);
            _boarding.Add(team);
        }
        ShowMessage();
    }

    /// <summary>Each board as it lands: one that failed says so in its lane, or on the bar where it keeps chips
    /// already on screen, and leaves the other lanes and their stamps as they were.</summary>
    private void SettleBoards()
    {
        if (_boarding is not { } boarding)
            return;
        foreach (var (team, read) in boarding.Landed())
        {
            if (_boardFailures.Remove(team, out var was))
                _bar.Withdraw(was);
            if (read.Failure is { Length: > 0 } failure)
            {
                if (_overseer.Fail(team, Reason(failure), RetryHint("overseer.retry"), () => RetryBoards([team])))
                    _boardFailures[team] = Announce($"{team}: {Reason(failure)}", Severity.Error);
                continue;
            }
            _boardReadAt[team] = _clock.GetUtcNow();
            Sync(team);
            _overseer.Show(team, BoardCard.Parse(read.Output), MirrorLag.Parse(read.Output));
            BackToCard(team);
        }
        if (boarding.Done)
            _boarding = null;
    }

    /// <summary>How soon after a team's last sync with GitHub another read of it syncs again.</summary>
    internal static readonly TimeSpan SyncEvery = TimeSpan.FromMinutes(1);

    /// <summary>Brings <paramref name="team"/>'s record up to date with GitHub behind what's on screen, which the
    /// reads take from the record as it stands.</summary>
    private void Sync(string team)
    {
        var now = _clock.GetUtcNow();
        if (_options.SyncBoard is null || _syncedAt.TryGetValue(team, out var at) && now - at < SyncEvery)
            return;
        _syncedAt[team] = now;
        _syncing ??= new TeamReads([], _options.SyncBoard);
        _syncing.Add(team);
    }

    /// <summary>A team synced is read again, for its cards and when they were synced even where it failed, and Overseer's
    /// lane only where it has been read.</summary>
    private void SettleSyncs()
    {
        if (_syncing is not { } syncing)
            return;
        foreach (var (team, read) in syncing.Landed())
        {
            _detailSynced[team] = DetailSynced(team);
            ReadTeam(team);
            if (read.Failure is { Length: > 0 })
                continue;
            if (_options.ReadBoard is not null && _boardReadAt.ContainsKey(team))
                (_boarding ??= new TeamReads([], _options.ReadBoard)).Add(team);
        }
        if (syncing.Done)
            _syncing = null;
    }

    /// <summary>A team is read again whenever the dispatcher's own sync has stored its cards' detail.</summary>
    private void ReadSynced()
    {
        foreach (var team in _teamNames)
        {
            var at = DetailSynced(team);
            if (_detailSynced.TryGetValue(team, out var was) && was != at && _readAt is not null)
                ReadTeam(team);
            _detailSynced[team] = at;
        }
    }

    private DateTime DetailSynced(string team) => Written(Path.Combine(_stateRoot, team, "detail-synced"));
}
