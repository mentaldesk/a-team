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
    /// <summary>The last read landed, began under five minutes ago, and no run has started or finished since.</summary>
    private bool UpToDate() =>
        _askedAt is { } asked && _readAt >= asked && _clock.GetUtcNow() - asked < ReadEvery
        && _activityRead.SequenceEqual(Activity());

    private void ToggleOnlyMine()
    {
        _work.ShowOnlyMine(!_work.OnlyMine);
        _settings.WriteOnlyMine(_work.OnlyMine);
        _menu.Refresh();
        ShowMessage();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Hands the terminal to <c>a-team try</c> for the card's PR, from its own row or the PR's.</summary>
    private void Try()
    {
        if (_work.Selected is { Triable: true } item)
            HandOverTry(item);
    }

    private void HandOverTry(WaitingItem item, ReaderPlace? reader = null)
    {
        if (_options.HandOver is null)
            return;
        _attention.Open(item, WorkView.GateOf(item));
        Seen(item);
        _attention.Did("tried");
        _attention.HandOver();
        _options.HandOver(new TryHandover(item, _work.SelectedCard is null, _work.Items, _readAt, reader) { Synced = new Dictionary<string, Synced>(_synced), From = _area });
    }

    /// <summary>Writes the document pitch's file for your editor, and hands the terminal to it.</summary>
    private void HandOverEdit(WaitingItem item, DocumentPitch document, ReaderPlace reader)
    {
        if (_options.HandOver is null)
            return;
        var file = DocumentHandover.FileFor(item, document);
        try
        {
            File.WriteAllText(file, document.Text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _attention.Close();
            Reply($"couldn't write {document.Path} for your editor: {e.Message}", Severity.Error);
            return;
        }
        _attention.Did("edited");
        _attention.HandOver();
        _options.HandOver(new DocumentHandover(item, document, file, EditorHandover.Command(Environment.GetEnvironmentVariable),
            _work.SelectedCard is null, _work.Items, _readAt, reader) { Synced = new Dictionary<string, Synced>(_synced), From = _area });
    }

    /// <summary>Back from a try, the cards as they were with no re-read; from an attach or the editor, Overseer. Either way, what
    /// went wrong if it failed.</summary>
    private void Resume(Handover handover)
    {
        _resume = handover;
        var failure = handover.Failure;
        if (handover is TeamsChanged changed)
            _left = changed.Left;
        if (handover is VisionHandover vision)
            _left = vision.Left;
        if (handover is AttachHandover attached)
            _backTo = (attached.Team, attached.Card, attached.Role, null);
        if (handover is EditorHandover editor)
            _backTo = (editor.Team, editor.Card, editor.Role, editor.Place);
        if (handover is TryHandover tried)
        {
            _attention.TakeBack();
            if (tried.Reader is null)
                _attention.Close();
            _readAt = _askedAt = tried.ReadAt;
            Stamp(tried.Synced);
            _work.Show(tried.Items);
            ReadTrend();
            if (tried.Reader is { } place)
            {
                _reopen = (tried.Item, place, failure, null, false);
                failure = null;
            }
            BackToChip(tried.From, tried.Item);
        }
        if (handover is DocumentHandover edited)
        {
            _attention.TakeBack();
            _readAt = _askedAt = edited.ReadAt;
            Stamp(edited.Synced);
            _work.Show(edited.Items);
            ReadTrend();
            _reopen = (edited.Item, edited.Reader, failure, edited.Said, edited.Committed);
            failure = null;
            BackToChip(edited.From, edited.Item);
        }
        if (_backTo is { Number: null } role)
        {
            RefreshWatches(_clock.GetUtcNow());
            BackToCard(role.Team);
        }
        if (failure is { Length: > 0 })
            Reply(failure, Severity.Error);
        ShowMessage();
    }

    private void BackToChip(Area from, WaitingItem item)
    {
        if (from == Area.Overseer)
            _backTo = (item.Team, item.Number, "", null);
    }

    private void Stamp(IReadOnlyDictionary<string, Synced> synced)
    {
        foreach (var (team, stamp) in synced)
            _synced[team] = stamp;
    }

    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        if (newIsRunning)
        {
            FocusResumed();
            // Invoke would run it here, before the run loop starts, and the loop would then drop a try's RequestStop.
            App?.AddTimeout(TimeSpan.Zero, () =>
            {
                ReopenReader();
                return false;
            });
            App?.AddTimeout(OverseerView.SpinEvery, () =>
            {
                if (_area == Area.Overseer)
                    _overseer.Spin();
                return IsRunning;
            });
        }
    }

    /// <summary>Back from a try or an edit started in the reader, the same reader, where it was: read again where the
    /// edit was committed.</summary>
    internal void ReopenReader()
    {
        if (_reopen is not { } reopen)
            return;
        _reopen = null;
        if (reopen.Reread)
            ReadBody(reopen.Item, (read, body) => ShowBody(read, body, reopen.Place.Url, reopen.Place.Top, reopen.Failure, said: reopen.Said),
                forReader: true);
        else
            ShowBody(reopen.Item, reopen.Place.Body, reopen.Place.Url, reopen.Place.Top, reopen.Failure, said: reopen.Said);
    }

    internal void FocusResumed()
    {
        if (_resume is not { } resume)
            return;
        _resume = null;
        if (_area != Area.Work)
            return;
        switch (resume)
        {
            case TryHandover tried when !_work.Focus(tried.Item, tried.OnPr):
                _work.FocusFirstCard();
                break;
            case DocumentHandover edited when !_work.Focus(edited.Item, edited.OnPr):
                _work.FocusFirstCard();
                break;
        }
    }

    private void OpenSelected()
    {
        if (_work.SelectedUrl is { Length: > 0 } url)
            Open(url);
    }

    /// <summary>The team of the selected column in Work, or of the selected chip or heading in Overseer.</summary>
    internal string? SelectedTeam() => _area == Area.Work ? _work.Team : _overseer.Board.Selected?.Team;

    /// <summary>Hands the terminal to the vision interview for <paramref name="team"/>, to come back where it is now.</summary>
    /// <summary>Asks for a review whatever the team's trigger, then runs a pass so the Lead starts on it now.</summary>
    private void ReviewVision(string? team)
    {
        if (team is null || Already($"{team}/vision"))
            return;
        Queue(new Write(WriteKind.VisionReview, $"{team}/vision", ["board", team, "vision-review", "you"],
            "Asking for a vision review…", $"A vision review for {team} is already being asked for",
            $"asking for {team}'s vision review", Team: team));
    }

    private void WriteVision(string? team)
    {
        if (team is null)
            return;
        _options.HandOver?.Invoke(new VisionHandover(team, _area, _area == Area.Work ? _work.Place : _left));
    }

    private void OpenBoard()
    {
        if (SelectedTeam() is not { } team)
            return;
        if (_teams.BoardUrl(team) is not { } url)
        {
            Reply($"{team}'s config names no project board", Severity.Error);
            return;
        }
        Open(url);
    }

    /// <summary>Opens the reader with the ranks under it, starting on the card's own.</summary>
    private void SetPriority()
    {
        if (_work.SelectedCard is { } item)
            ReadRanking(item);
    }

    private void ReadSelected() => ReadSelected(declining: false);

    /// <summary>A card in Triage is waiting for a rank, so it opens with the ranks under it.</summary>
    private void ReadSelected(bool declining, bool shelving = false)
    {
        if ((declining || shelving ? _work.SelectedCard : _work.Selected) is not { } item)
            return;
        if (_work.InTriage && _work.SelectedCard is { } card)
        {
            ReadRanking(card, declining);
            return;
        }
        var url = _work.SelectedUrl;
        ReadBody(item, (read, body) => ShowBody(read, body, url, declining: declining, shelving: shelving), forReader: true);
    }

    private void ReadRanking(WaitingItem item, bool declining = false, string? url = null, bool gates = true)
    {
        url ??= _work.SelectedUrl;
        ReadBody(item, (read, body) => ShowBody(read, body, url, rank: Priorities.Starting(read), declining: declining, gates: gates),
            forReader: true);
    }

    private void ReadBody(WaitingItem item, Action<WaitingItem, IssueBody> then, bool forReader = false)
    {
        if (_readingBody is not null)
            return;
        _progress = $"Reading #{item.Number}…";
        ShowMessage();
        _readingBody = (item, Read(item, forReader), then);
    }

    /// <summary>The reads start at once; a conversation or history that won't read still leaves the body to show.</summary>
    private async Task<IssueBody> Read(WaitingItem item, bool forReader)
    {
        var body = _readBody(item);
        var conversation = forReader ? _options.ReadConversation?.Invoke(item) : null;
        var history = forReader ? _options.ReadHistory?.Invoke(item) : null;
        var criteria = forReader && item.HasCriteria ? _options.ReadCriteria?.Invoke(item) : null;
        var opened = forReader ? _opened.Last(item.Team, item.Number) : null;
        var read = await Settled(body, reading => IssueBody.Of(reading, item.Number),
            new IssueBody(Failure: $"couldn't read #{item.Number}")).ConfigureAwait(false);
        if (read.Failure is not null)
            return read;
        read = read with { Opened = opened };
        if (conversation is not null)
            read = read.With(await Settled(conversation, reading => Conversation.Of(reading, item.Number),
                new Conversation([], $"couldn't read the conversation on #{item.Number}")).ConfigureAwait(false), opened);
        if (forReader && item.Document && _options.ReadDocument?.Invoke(item) is { } reading
            && await Settled(reading, read => read.Failure is null ? DocumentPitch.Parse(read.Output) : null, null).ConfigureAwait(false) is { } document)
            read = read.With(document);
        if (criteria is not null)
            read = await Settled(criteria, reading => reading.Failure is { Length: > 0 } why
                    ? read.Headed($"## Criteria · couldn't be read: {why}")
                    : Checklist.Parse(reading.Output) is { } checklist ? read.With(checklist) : read,
                read.Headed($"## Criteria · couldn't be read")).ConfigureAwait(false);
        if (forReader)
            read = read.Headed(item.PrHeading);
        if (history is not null)
            read = read with
            {
                History = await Settled(history, reading => History.Of(reading, item.Number),
                    new History([], Failure: $"couldn't read #{item.Number}'s history")).ConfigureAwait(false),
            };
        return read;
    }

    private static async Task<T> Settled<T>(Task<Reading> reading, Func<Reading, T> of, T otherwise)
    {
        try
        {
            return of(await reading.ConfigureAwait(false));
        }
        catch (Exception)
        {
            return otherwise;
        }
    }

    /// <summary>Nothing to read opens no dialog: the bar says why and you stay on the board. With
    /// <paramref name="rank"/> it opens anyway, saying why, since you came to rank it. Without
    /// <paramref name="gates"/> it offers only commenting and GitHub: the card isn't waiting on you.</summary>
    private void ShowBody(WaitingItem item, IssueBody body, string? url, int? top = null, string? failure = null,
        Rank? rank = null, bool declining = false, string? said = null, bool shelving = false, bool gates = true)
    {
        if (rank is null && body.Failure is { Length: > 0 } unread)
            Reply(unread, Severity.Error);
        else if (rank is null && string.IsNullOrWhiteSpace(body.Text))
            Reply($"#{item.Number} has no description", Severity.Error);
        else
        {
            _approvable = gates && item.Approvable ? item : null;
            _shown = item;
            _shownBody = body;
            _attention.Open(item, gates ? WorkView.GateOf(item) : Attention.Other);
            Seen(item);
            _idea = gates && NewIdeaEnabled() ? new ReaderIdea(_commands.KeyFor("work.new"), description => AddIdea(item.Team, description)) : null;
            _image = new ReaderImage();
            if (body.Checklist is { } listed)
                _unticked[Write.KeyOf(item)] = listed.Unticked;
            var chosen = _showBody(item, body, () => url is { Length: > 0 } ? _openUrl(url) : null, _approvable is null ? null : Approve,
                gates && item.Acceptable ? new ReaderCommand(_commands.KeyFor("work.accept"), "accept", Accept, item.Unacceptable.Length == 0) : null,
                new ReaderComment(_commands.KeyFor("work.comment"), "comment", body => Post(item, body), _clock,
                    gates && item.Declinable ? new ReaderDecline(_commands.KeyFor("work.decline"), reason => Decline(item, reason), declining) : null,
                    gates && item.Shelvable ? new ReaderShelve(_commands.KeyFor("work.notNow"), (weeks, reason) => Shelve(item, weeks, reason), shelving) : null,
                    Attaching(), _image, gates && item.Held ? new ReaderRelease(comment => Release(item, comment)) : null,
                    gates && item.Fixable && ToConsider(item, body).Count > 0 ? new ReaderCommand(_commands.KeyFor("work.fix"), "ask the Dev to fix", AskFix) : null,
                    gates && body.Checklist is not null ? new ReaderTick((criterion, ticked) => Tick(item, criterion, ticked), SettleWrites,
                        unticked => _unticked[Write.KeyOf(item)] = unticked) : null,
                    gates && item.Secret is not null ? new ReaderCommand(_commands.KeyFor("work.secret"), "give secret", GiveSecret) : null),
                gates && item.Triable ? new ReaderTry(_commands.KeyFor("work.try"), (shown, at) => _triedFrom = (shown, at), top, failure) : null,
                gates && item.Document ? new ReaderEdit(_commands.KeyFor("work.edit"), (shown, at) => _editFrom = (shown, at),
                    body.Document is { } document ? document.Uneditable(item.Number) : $"couldn't read what #{item.Number} changes",
                    top, said, failure) : null,
                _commands.KeyFor("work.copyBranch"),
                _idea,
                key => (key == _commands.KeyFor("help") || key == _commands.KeyFor("commands")) && _commands.Press(key),
                rank);
            _idea = null;
            _image = null;
            _opened.Open(item.Team, item.Number, _clock.GetUtcNow());
            _approvable = null;
            _shown = null;
            _shownBody = null;
            var reranked = chosen is { } ranked && item.Reranks(ranked) ? chosen : null;
            if (reranked is not null)
                _attention.Did("ranked");
            if (_triedFrom is null && _editFrom is null)
                _attention.Close();
            if (_leaving == item)
            {
                _leaving = null;
                return;
            }
            if (reranked is { } to)
                SetRank(item, to);
            if (_triedFrom is { } from)
            {
                _triedFrom = null;
                HandOverTry(item, new ReaderPlace(from.Body, url, from.Top));
            }
            if (_editFrom is { } edit && body.Document is { Editable: true } edited)
            {
                _editFrom = null;
                HandOverEdit(item, edited, new ReaderPlace(edit.Body, url, edit.Top));
            }
        }
    }

    private Attaching? Attaching() => _options.Attaching?.Invoke(_commands.KeyFor("work.attach"));

    private bool NewIdeaEnabled() => _options.NewIdea is not null && _teamNames.Count > 0;

    /// <summary>Adds an idea to the team whose lane you're in, or the first team's, or from what the reader shows.</summary>
    private void NewIdea()
    {
        if (_idea?.Start is { } fromReader)
            fromReader();
        else
            AddIdea(_area == Area.Work && _work.Current is { } lane ? lane.Team : _teamNames[0]);
    }

    /// <summary>Reads Work again to show the idea added, and says which it was.</summary>
    private string? AddIdea(string team, string description = "")
    {
        if (_options.NewIdea?.Invoke(_teamNames, team, description, Attaching()) is not { } added)
            return null;
        _added = $"#{added.Number} added to {added.Team}'s ideas";
        ReadWaiting();
        return _added;
    }
}
