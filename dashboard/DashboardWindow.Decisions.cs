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
    /// <summary>Approves the pitch the reader is showing once you've said so. Like a rank, the card stays put until the
    /// board takes it. True once it's under way, or already was.</summary>
    private bool Approve()
    {
        if (_approvable is not { } item)
            return false;
        if (Already(Write.KeyOf(item)))
            return true;
        if (!_confirmApprove(item))
            return false;
        _attention.Did("approved");
        Queue(new Write(WriteKind.Approve, Write.KeyOf(item), ["board", item.Team, "approve", "you", item.Number.ToString()],
            $"Approving #{item.Number}…", $"#{item.Number} is already being approved", $"approving #{item.Number}", item));
        return true;
    }

    private void Approved(WaitingItem item)
    {
        _work.Approved(item);
        Announce($"#{item.Number} approved");
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>The item the reader is showing, or else the one selected on the board, when it's In review.</summary>
    private WaitingItem? Acceptable() =>
        (_shown ?? (_area == Area.Work ? _work.Selected : null)) is { Acceptable: true } item ? item : null;

    /// <summary>Merges the task's PR or closes the pitch once you've said so, or says why it can't yet. True once
    /// it's under way, or already was.</summary>
    private bool Accept()
    {
        if (Acceptable() is not { } item)
            return false;
        if (Already(Write.KeyOf(item)))
            return true;
        if (item.Unacceptable is { Length: > 0 } why)
        {
            Reply(why, Severity.Error);
            return false;
        }
        if (!_confirmAccept(item with { Unticked = _unticked.GetValueOrDefault(Write.KeyOf(item), item.Unticked) }))
            return false;
        _attention.Did("accepted");
        var (doing, being, queued) = item.Document ? ($"Merging #{item.Number}…", "merged", $"merging #{item.Number}")
            : item.Pitch ? ($"Closing #{item.Number}…", "closed", $"closing #{item.Number}")
            : ($"Merging PR #{item.Pr}…", "merged", $"merging PR #{item.Pr}");
        Queue(new Write(WriteKind.Accept, Write.KeyOf(item), ["board", item.Team, "accept", "you", item.Number.ToString()],
            doing, $"#{item.Number} is already being {being}", queued, item));
        return true;
    }

    /// <summary>In the write queue, so it outlives the reader and a try, and two ticks in a row can't undo each other.</summary>
    private Task<string?> Tick(WaitingItem item, Criterion criterion, bool ticked)
    {
        _attention.Did("ticked");
        var outcome = new TaskCompletionSource<string?>();
        Queue(new Write(WriteKind.Tick, $"{Write.KeyOf(item)}/tick",
            ["board", item.Team, "tick", "you", item.Number.ToString(), criterion.Index.ToString(), ticked ? "on" : "off", criterion.Text],
            $"Saving the tick on #{item.Number}…", "", $"saving a tick on #{item.Number}") { Outcome = outcome });
        return outcome.Task;
    }

    private async Task<string?> Post(WaitingItem item, string body)
    {
        var refused = await WithFile(item, "comment", body).ConfigureAwait(false);
        if (refused is null or { Length: 0 })
            _attention.Did("commented");
        return refused;
    }

    /// <summary>Closes the item as not planned with your reason. The reader closes on it at once, and the card leaves
    /// once the board has taken it.</summary>
    private Task<string?> Decline(WaitingItem item, string reason)
    {
        if (_writes.Holding(Write.KeyOf(item)) is { } already)
            return Task.FromResult<string?>(already.Already);
        var file = Path.GetTempFileName();
        File.WriteAllText(file, reason);
        _attention.Did("declined");
        _leaving = item;
        Queue(new Write(WriteKind.Decline, Write.KeyOf(item), ["board", item.Team, "decline", "you", item.Number.ToString(), file],
            $"Declining #{item.Number}…", $"#{item.Number} is already being declined", $"declining #{item.Number}", item,
            Scratch: file));
        return Task.FromResult<string?>(null);
    }

    private void Declined(WaitingItem item)
    {
        _leaving = null;
        _work.Leave(item);
        Announce($"#{item.Number} declined");
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Shelves the pitch for <paramref name="weeks"/>, with your reason if you gave one. Like a decline, the
    /// reader closes on it at once, and the card leaves once the board has taken it.</summary>
    private Task<string?> Shelve(WaitingItem item, int weeks, string reason)
    {
        if (_writes.Holding(Write.KeyOf(item)) is { } already)
            return Task.FromResult<string?>(already.Already);
        var file = Path.GetTempFileName();
        File.WriteAllText(file, reason);
        _attention.Did("shelved");
        _leaving = item;
        Queue(new Write(WriteKind.Shelve, Write.KeyOf(item),
            ["board", item.Team, "shelve", "you", item.Number.ToString(), weeks.ToString(), file],
            $"Shelving #{item.Number}…", $"#{item.Number} is already being shelved", $"shelving #{item.Number}", item,
            Scratch: file, Weeks: weeks));
        return Task.FromResult<string?>(null);
    }

    /// <summary>The item the reader is showing, or else the one selected on the board, where there's a point to ask for.</summary>
    private WaitingItem? Fixable() =>
        _shown is { } shown
            ? shown.Fixable && _shownBody is { } body && ToConsider(shown, body).Count > 0 ? shown : null
            : _area == Area.Work && _work.Selected is { Fixable: true } item ? item : null;

    private static IReadOnlyList<string> ToConsider(WaitingItem item, IssueBody body) =>
        Considered.Points(body.Remarks.Select(said => said.Remark), item.Pr);

    /// <summary>Asks which of the Reviewer's points to fix, reading its review first unless the reader already has.
    /// True once a comment is under way.</summary>
    private bool AskFix()
    {
        if (Fixable() is not { } item || Already(Write.KeyOf(item)))
            return false;
        if (_shown is not null)
            return _shownBody is { } shown && AskFix(item, shown);
        ReadBody(item, (read, body) =>
        {
            ShowMessage();
            AskFix(read, body);
        }, forReader: true);
        return false;
    }

    private bool AskFix(WaitingItem item, IssueBody body)
    {
        var points = ToConsider(item, body);
        if (points.Count == 0)
        {
            Reply($"couldn't read what the Reviewer left on PR #{item.Pr}", Severity.Error);
            return false;
        }
        if (_askFix(item, points) is not { Length: > 0 } comment)
            return false;
        var file = Path.GetTempFileName();
        File.WriteAllText(file, comment);
        _attention.Did("asked");
        Queue(new Write(WriteKind.AskFix, Write.KeyOf(item), ["board", item.Team, "comment", "you", item.Pr.ToString(), file],
            $"Asking the Dev about PR #{item.Pr}…", $"#{item.Number} is already being asked about", $"asking the Dev about PR #{item.Pr}", item, Scratch: file));
        return true;
    }

    /// <summary>Holds the selected Ready task with the reason you give, or takes the hold off one that's held.</summary>
    private void HoldOrRelease()
    {
        if (_work.SelectedCard is not { Holdable: true } item || Already(Write.KeyOf(item)))
            return;
        if (item.Held)
        {
            Release(item, "");
            return;
        }
        if (_askHold(item) is not { Length: > 0 } reason)
            return;
        var file = Path.GetTempFileName();
        File.WriteAllText(file, reason);
        _attention.Did("held");
        Queue(new Write(WriteKind.HoldTask, Write.KeyOf(item), ["board", item.Team, "hold", "you", item.Number.ToString(), file],
            $"Holding #{item.Number}…", $"#{item.Number} is already being held", $"holding #{item.Number}", item, Scratch: file));
    }

    private WaitingItem? AskingSecret() => _shown is { Secret: not null } read ? read
        : _area == Area.Work && _work.SelectedCard is { Secret: not null } card ? card : null;

    /// <summary>Opens <b>Give a secret</b>. The value goes from the dialog to GitHub and nowhere else: what the board is told
    /// afterwards is only that it was given. True once it has been.</summary>
    private bool GiveSecret()
    {
        if (AskingSecret() is not { Secret: { } secret } item || _options.Secrets is not { } secrets || Already(Write.KeyOf(item)))
            return false;
        if (!_giveSecret(item, secrets.IsSet(secret), value => secrets.Store(secret, value)))
            return false;
        _attention.Did("gave");
        if (_shown is not null)
            _leaving = item;
        Queue(new Write(WriteKind.GaveSecret, Write.KeyOf(item), ["board", item.Team, "gave", "you", item.Number.ToString()],
            $"Freeing #{item.Number}…", $"#{item.Number} is already being freed", $"freeing #{item.Number}, whose secret is saved", item));
        return true;
    }

    private void GaveSecret(WaitingItem item)
    {
        _leaving = null;
        _work.Leave(item);
        Announce($"#{item.Number} back in the Dev's queue · its secret is saved");
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Posts what you wrote, if anything, then takes the hold off, whoever set it. Like a decline, the reader
    /// closes on it at once, and the card leaves once the board has taken it.</summary>
    private Task<string?> Release(WaitingItem item, string comment)
    {
        if (_writes.Holding(Write.KeyOf(item)) is { } already)
            return Task.FromResult<string?>(already.Already);
        _attention.Did("released");
        if (_shown is not null)
            _leaving = item;
        var release = new Write(WriteKind.Release, Write.KeyOf(item), ["board", item.Team, "release", "you", item.Number.ToString()],
            $"Releasing #{item.Number}…", $"#{item.Number} is already being released", $"releasing #{item.Number}", item);
        if (string.IsNullOrWhiteSpace(comment))
        {
            Queue(release);
            return Task.FromResult<string?>(null);
        }
        return PostThenRelease(item, comment, release);
    }

    private async Task<string?> PostThenRelease(WaitingItem item, string comment, Write release)
    {
        var refused = await WithFile(item, "comment", comment).ConfigureAwait(false);
        if (refused is { Length: > 0 })
        {
            _leaving = null;
            return refused;
        }
        Queue(release);
        return null;
    }

    private void Held(WaitingItem item)
    {
        _added = $"#{item.Number} held";
        ReadWaiting();
    }

    private void Released(WaitingItem item)
    {
        _leaving = null;
        _work.Leave(item);
        Announce(item.GaveUp ? $"#{item.Number} freed · a run starts for it on the next pass" : $"#{item.Number} back in the Dev's queue");
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void Shelved(WaitingItem item, int weeks)
    {
        _leaving = null;
        _work.Leave(item);
        Announce($"#{item.Number} back on {ReaderShelve.BackOn(_clock.GetUtcNow(), weeks)}");
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private async Task<string?> WithFile(WaitingItem item, string command, string text)
    {
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, text).ConfigureAwait(false);
            return await _run(["board", item.Team, command, "you", item.Number.ToString(), file]).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private void Merged(WaitingItem item)
    {
        _work.Leave(item);
        Announce(item.Document ? $"merged #{item.Number}" : item.Pitch ? $"accepted #{item.Number}" : $"merged PR #{item.Pr}");
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Writes the rank. The board decides what the field will take, so an unknown value comes back as a
    /// refusal rather than being guessed at here.</summary>
    private void SetRank(WaitingItem item, Rank rank)
    {
        if (Already(Write.KeyOf(item)))
            return;
        Queue(new Write(WriteKind.Rank, Write.KeyOf(item), ["board", item.Team, "priority", "you", item.Number.ToString(), Priorities.Value(rank)],
            $"Setting #{item.Number} to {rank}…", $"#{item.Number} is already being ranked", $"setting #{item.Number} to {rank}",
            item, rank));
    }

    /// <summary>What the board took: the card moves to the column its new Priority puts it in, and the bar says so.</summary>
    private void Ranked(WaitingItem item, Rank rank)
    {
        _work.Ranked(item, rank);
        Announce($"#{item.Number} · set to {rank}");
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Picked up by the next refresh, so a command runs off the draw loop and reports back on it.</summary>
    private void Settle()
    {
        if (_pass.Finished() is { } passed)
            Announce(passed.Text, SeverityOf(passed.Scheme));

        if (_readingBody is { Read.IsCompleted: true } body)
        {
            _readingBody = null;
            _progress = null;
            body.Then(body.Item, body.Read.Result);
        }

        if (_reading is not { } reading)
            return;
        foreach (var (team, read) in reading.Landed())
            SettleTeam(team, read, _readingAsked);
        if (!reading.Done)
            return;
        _reading = null;
        if (!reading.AnyRead)
            return;
        _readAt = _clock.GetUtcNow();
        if (_area == Area.Work && _work.Selected is null)
        {
            if (_left is not { } left || !_work.Focus(left))
                _work.FocusFirstCard();
            _left = null;
        }
        GoToWaiting();
        if (_added is { } added)
        {
            _added = null;
            Announce(added);
        }
        ReadTrend();
    }

    /// <summary>After the reader has closed, so a write it queued that's already done lands on the same refresh.</summary>
    private void SettleWrites()
    {
        var settled = _writes.Settle();
        if (settled.Count == 0)
            return;
        _work.ShowWriting(Writing());
        foreach (var (write, failure) in settled)
            write.Outcome?.TrySetResult(failure);
        foreach (var (write, refused) in settled.Where(done => done.Failure is not { Length: > 0 }))
            Settled(write);
        var refusals = settled.Where(done => done.Failure is { Length: > 0 })
            .Select(done => $"{done.Write.Queued}: {done.Failure}").ToList();
        if (refusals.Count > 0)
            Announce(string.Join(" · ", refusals), Severity.Error);
    }

    /// <summary>Nothing moves until the board has taken it: a refused write leaves its card as it was.</summary>
    private void Settled(Write write)
    {
        switch (write.Kind)
        {
            case WriteKind.Accept:
                Merged(write.Item!);
                break;
            case WriteKind.Approve:
                Approved(write.Item!);
                break;
            case WriteKind.Rank:
                Ranked(write.Item!, write.Rank);
                break;
            case WriteKind.Decline:
                Declined(write.Item!);
                break;
            case WriteKind.Shelve:
                Shelved(write.Item!, write.Weeks);
                break;
            case WriteKind.HoldTask:
                Held(write.Item!);
                break;
            case WriteKind.AskFix:
                Announce($"asked the Dev to fix PR #{write.Item!.Pr}");
                break;
            case WriteKind.Release:
                Released(write.Item!);
                break;
            case WriteKind.GaveSecret:
                GaveSecret(write.Item!);
                break;
            case WriteKind.VisionReview:
                _pass.Start();
                Announce($"vision review asked for · {write.Team}: the Lead starts it now");
                break;
        }
        if (write.Item is { } card && _area == Area.Overseer)
            ReadLane(card.Team);
    }
}
