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
    /// <summary>Pauses the role chosen in the team's panel, or lets it start again.</summary>
    private void ToggleHold()
    {
        if (Chosen() is not { } watch || Already(RoleKey(watch)))
            return;
        Queue(new Write(WriteKind.Hold, RoleKey(watch), [watch.Held ? "resume" : "pause", watch.Team, watch.Role],
            watch.Held ? "Letting it start again…" : "Pausing this role…",
            $"{watch.Team}'s {watch.Role} is already being {(watch.Held ? "let start again" : "paused")}",
            $"{(watch.Held ? "letting" : "pausing")} {watch.Team}'s {watch.Role}{(watch.Held ? " start again" : "")}"));
    }

    private void PassNow()
    {
        _pass.Start();
        ShowMessage();
    }

    private static string RoleKey(RoleWatch watch) => $"{watch.Team}/{watch.Role}";

    private string UnlessStopped(string label, string resume) =>
        (_overseer.Board.OnHeading ? Chosen() is { Held: true } : CardHeld()) ? resume : label;

    private void ToggleInterrupt()
    {
        if (_overseer.Board.OnHeading)
            InterruptRole();
        else
            InterruptCard();
    }

    /// <summary>The chosen role's run, as on its card: a run with a conversation hands you to it, to come back to the
    /// team's panel, and one too new for one is stopped, asking first where it's a task's. A held role starts again.</summary>
    private void InterruptRole()
    {
        if (Chosen() is not { } watch || Already(RoleKey(watch)))
            return;
        var who = $"{watch.Team}'s {watch.Role}";
        if (!watch.Held && !watch.Running)
            return;
        if (!watch.Held && watch.Runs.Count > 1)
        {
            Reply($"{who} is on {watch.Runs.Count} cards: interrupt one from its card", Severity.Error);
            return;
        }
        var task = watch.Held ? null : watch.Runs.FirstOrDefault()?.Task;
        if (!watch.Held && watch.SessionId is not null)
        {
            _options.HandOver?.Invoke(new AttachHandover(watch.Team, watch.Role, task?.Number));
            return;
        }
        if (!watch.Held && task is not null && !_confirmStop(task))
            return;
        string[] target = task is null ? [watch.Team, watch.Role] : [watch.Team, watch.Role, task.Number.ToString(CultureInfo.InvariantCulture)];
        Queue(new Write(WriteKind.Interrupt, RoleKey(watch), [watch.Held ? "resume" : "stop", .. target],
            watch.Held ? "Letting it start again…" : "Interrupting…",
            $"{who} is already being {(watch.Held ? "let start again" : "interrupted")}",
            $"{(watch.Held ? "letting" : "interrupting")} {who}{(watch.Held ? " start again" : "")}"));
    }

    /// <summary>The card's run, stopped or resumed on its own where it's a task's, or else its role's. A run with a
    /// conversation hands you to it, to come back to the card.</summary>
    private void InterruptCard()
    {
        if (_overseer.Board.SelectedCard is not { } card || _cardRun is not { } run || Already($"{card.Team}/{run.Role}"))
            return;
        var resume = CardHeld();
        if (!resume && !run.State.Running)
            return;
        int? task = run.OnTask ? card.Number : null;
        if (!resume && _card.SessionId is not null)
        {
            _options.HandOver?.Invoke(new AttachHandover(card.Team, run.Role, task) { Card = card.Number });
            return;
        }
        if (!resume && task is not null && !_confirmStop(new RunTask(card.Number, card.Title)))
            return;
        string[] target = task is null ? [card.Team, run.Role] : [card.Team, run.Role, card.Number.ToString(CultureInfo.InvariantCulture)];
        var who = $"{card.Team}'s {run.Role}";
        Queue(new Write(WriteKind.Interrupt, $"{card.Team}/{run.Role}", [resume ? "resume" : "stop", .. target],
            resume ? "Letting it start again…" : "Interrupting…",
            $"{who} is already being {(resume ? "let start again" : "interrupted")}",
            $"{(resume ? "letting" : "interrupting")} {who}{(resume ? " start again" : "")}"));
    }

    /// <summary>The card's run is stopped by you: on its own, or with its role held where it isn't a task's.</summary>
    private bool CardHeld() =>
        _overseer.Board.SelectedCard is { } card && _cardRun is { } run
        && (run.Held || (!run.OnTask && _teams.IsHeld(card.Team, run.Role)));

    /// <summary>Writes every line of the shown session to a file and hands the terminal to your editor with it.</summary>
    private void OpenInEditor()
    {
        if (!_sessionFull || _card.LogPath is not { } path)
            return;
        if (_overseer.Board.SelectedCard is { } card && _cardRun is { } run)
            Edit(card.Team, run.Role, path, _card.Place, card.Number);
        else if (Chosen() is { } watch)
            Edit(watch.Team, watch.Role, path, _card.Place, null);
    }

    private void Edit(string team, string role, string log, LogPlace place, int? card)
    {
        var file = Path.Combine(Path.GetTempPath(), $"a-team-{team}-{role}-{_clock.GetUtcNow():yyyyMMddHHmmss}.log");
        try
        {
            File.WriteAllLines(file, SessionLog.Whole(log));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Reply($"couldn't write the log for your editor: {e.Message}", Severity.Error);
            return;
        }
        _options.HandOver?.Invoke(new EditorHandover(team, role, place, file, EditorHandover.Command(Environment.GetEnvironmentVariable)) { Card = card });
    }

    /// <summary>No dialog is on top of the window and no menu is open over it.</summary>
    private bool Uncovered => (IsModal || !IsRunning) && !_menu.Bar.IsOpen();

    /// <summary>Back from a conversation or the editor, on the card you left once its board is read, or straight away on
    /// the team's panel with the role chosen, and its session full-screen where it was.</summary>
    private void BackToCard(string team)
    {
        if (_backTo is not { } back || back.Team != team)
            return;
        _backTo = null;
        if (back.Number is { } number ? !_overseer.Select(back.Team, number) : !_overseer.SelectHeading(back.Team))
            return;
        if (back.Number is null)
        {
            ShowSelection();
            _team.Choose(back.Role);
        }
        if (back.Full is not { } place)
            return;
        ShowSelection();
        OpenSession();
        _card.Restore(place);
    }

    /// <summary>What Overseer draws besides the cards: the limits, and which cards and roles the agents are on.</summary>
    private void ShowOverseer(DateTimeOffset now)
    {
        if (_area != Area.Overseer)
            return;
        var running = _watches.Where(watch => watch.Running).ToList();
        _overseer.Show(new OverseerState(
            now,
            _limits,
            running.SelectMany(watch => watch.Working.Select(number => (Card: (watch.Team, number), watch.Role)))
                .DistinctBy(worked => worked.Card).ToDictionary(worked => worked.Card, worked => worked.Role),
            running.Where(watch => watch.Status != RoleStatus.Stuck).GroupBy(watch => watch.Team)
                .ToDictionary(team => team.Key, team => (IReadOnlyList<string>)[.. team.Select(watch => watch.Role)]),
            (team, number) => _work.Items.FirstOrDefault(item => item.Team == team && item.Number == number))
        {
            Holding = Holds.Holders(_work.Items),
            Stopped = _teamNames.Where(team => _checks?.Fatal(team) is { StopsTheTeam: true }).ToHashSet(),
            Stuck = _watches.Where(watch => watch.Status == RoleStatus.Stuck).GroupBy(watch => watch.Team)
                .ToDictionary(team => team.Key, team => (IReadOnlyList<string>)[.. team.Select(watch => watch.Role)]),
            GaveUp = _watches.Where(watch => watch.GaveUp is not null).GroupBy(watch => watch.Team)
                .ToDictionary(team => team.Key, team => (IReadOnlyList<string>)[.. team.Select(watch => $"{watch.Role} gave up: {watch.GaveUp}")]),
        });
    }

    private void OverseerEnter()
    {
        if (_overseer.Board.OnMore)
            _overseer.Unfold();
        else if (_cardRun is not null || Chosen() is { LogPath: not null })
            OpenSession();
        ShowMessage();
    }

    /// <summary>The selected card's session, or the chosen role's latest, full-screen in place of the lanes.</summary>
    private void OpenSession()
    {
        _sessionFromRoles = _team.HasFocus;
        _sessionFull = true;
        _card.Full = true;
        ShowSelection();
        Arrange();
        _card.SetFocus();
        ShowMessage();
    }

    private void CloseSession()
    {
        _sessionFull = false;
        _card.Full = false;
        Arrange();
        if (_sessionFromRoles && _team.Visible)
            _team.SetFocus();
        else
            _overseer.SetFocus();
        ShowMessage();
    }

    /// <summary>The team's panel for a selected lane heading, or else the selected card's.</summary>
    private void ShowSelection()
    {
        if (_area == Area.Overseer && _overseer.Board.OnHeading)
            ShowTeam();
        else
            ShowCard();
    }

    /// <summary>The selected card's details and its run, in the panel beside the lanes or behind the full-screen log.</summary>
    private void ShowCard()
    {
        if (_overseer.Board.SelectedCard is not { } card)
        {
            _cardRun = null;
            if (_sessionFull)
                CloseSession();
            return;
        }
        var now = _clock.GetUtcNow();
        _cardRun = CardRun.For(_stateRoot, card.Team, _watches.Where(watch => watch.Team == card.Team).Select(watch => watch.Role), card.Number,
            card.LastRun);
        _card.Show(card, OverseerView.Describe(card, _overseer.Board.Cards, _overseer.State), _cardRun,
            CardRun.Says(_cardRun, now, CardHeld()), CardHint());
    }

    /// <summary>The selected heading's team: a line per role and anything stopping it running, and the chosen role's
    /// latest session behind the full-screen log.</summary>
    private void ShowTeam()
    {
        _cardRun = null;
        var team = _overseer.Board.Selected!.Value.Team;
        _team.Show(team,
            [.. _watches.Where(watch => watch.Team == team).Select(watch => (watch.Role, Icons.Field(Icons.For(watch.Status), _drawn), watch.Line))],
            _checks?.Fatal(team) is { } problem ? RoleWatch.Misconfigured(problem) : null,
            _overseer.MirrorNote(team),
            [.. _watches.Where(watch => watch.Team == team && watch.Status == RoleStatus.Stuck).Select(watch => (watch.Role, watch.Line))]);
        _team.ShowHint(TeamHint());
        if (!_sessionFull)
            return;
        if (Chosen() is not { } chosen)
        {
            CloseSession();
            return;
        }
        var name = OverseerView.RoleName(chosen.Role);
        _card.Show($"{team} · {name}", chosen.LogPath, $"{name} · {chosen.Line}");
    }

    /// <summary>The role chosen in the team's panel, while a lane heading is selected.</summary>
    private RoleWatch? Chosen() =>
        _area == Area.Overseer && _overseer.Board.Selected is { Heading: true } heading && _team.Team == heading.Team && _team.Role is { } role
            ? _watches.FirstOrDefault(watch => watch.Team == heading.Team && watch.Role == role)
            : null;

    private void MoveRole(int step)
    {
        _team.MoveRole(step);
        ShowTeam();
        ShowMessage();
    }

    /// <summary>The keys that work on the chosen role, as they're bound now.</summary>
    private string TeamHint()
    {
        List<string> keys = [];
        if (Tabbed)
            keys.Add($"{KeyHint("assistant")} Assistant");
        if (!_team.HasFocus)
            keys.Add($"{KeyHint("pane.next")} choose a role");
        if (Chosen() is { } watch)
        {
            if (watch.LogPath is not null)
                keys.Add($"{KeyHint("overseer.details")} latest log");
            keys.Add($"{KeyHint("agent.hold")} {(watch.Held ? "let it start again" : "pause")}");
            if (watch.Running && !watch.Held)
                keys.Add($"{KeyHint("agent.interrupt")} interrupt");
        }
        return string.Join(" · ", keys);
    }

    /// <summary>The keys that work on the card from the panel, as they're bound now.</summary>
    private string CardHint()
    {
        List<string> keys = [];
        if (Tabbed)
            keys.Add($"{KeyHint("assistant")} Assistant");
        if (_cardRun is not null)
            keys.Add($"{KeyHint("overseer.details")} full log");
        if (_commands.IsEnabled("agent.interrupt"))
            keys.Add($"{KeyHint("agent.interrupt")} {(CardHeld() ? "let it start again" : "interrupt")}");
        if (_overseer.Board.SelectedCard is not null)
            keys.Add($"{KeyHint("overseer.read")} Read");
        if (ChipInWork() is not null)
            keys.Add($"{KeyHint("overseer.work")} Work");
        if (_overseer.Board.SelectedCard is { Url.Length: > 0 })
            keys.Add($"{KeyHint("overseer.github")} GitHub");
        return string.Join(" · ", keys);
    }

    /// <summary>A letter key as you type it, as the guide writes it.</summary>
    private string KeyHint(string id) => _commands.KeysFor(id, Regions.Overseer) is { Length: 1 } letter ? letter.ToLowerInvariant() : _commands.KeysFor(id, Regions.Overseer);

    private void OpenChip()
    {
        if (_overseer.Board.SelectedCard is { Url.Length: > 0 } card)
            Open(card.Url);
    }

    private void Open(string url)
    {
        Reply(Copying.Said(_openUrl(url)));
    }

    /// <summary>The selected chip's card, where Work is showing it.</summary>
    private WaitingItem? ChipInWork() =>
        _overseer.Board.SelectedCard is { } card && _work.Waiting(card.Team, card.Number) is { } item && _work.Shows(item) ? item : null;

    private void GoToChip()
    {
        if (_overseer.Board.SelectedCard is not { } card)
            return;
        if (ChipInWork() is not { } item)
        {
            Reply($"#{card.Number} isn't in Work: nothing's waiting on you for it");
            return;
        }
        Show(Area.Work);
        _work.Pick(card.Team);
        // A tab that's never been in front has no size to scroll within until it's laid out.
        Layout();
        _work.Focus(item, false);
        ShowMessage();
    }

    /// <summary>The chip's card in Work's reader: with its gates where a column of Work's holds it, and otherwise to read
    /// and comment on.</summary>
    private void ReadChip()
    {
        if (_overseer.Board.SelectedCard is not { } card)
            return;
        if (_work.Waiting(card.Team, card.Number) is not { } item)
            ReadBody(new WaitingItem(card.Number, card.Title, card.Status, card.Url, card.Team, Priority: card.Priority, Pitch: card.Kind == CardKind.Pitch) { Facts = card.Facts },
                (read, body) => ShowBody(read, body, card.Url, gates: false), forReader: true);
        else if (WorkView.ToRank(item))
            ReadRanking(item, url: item.Url);
        else
            ReadBody(item, (read, body) => ShowBody(read, body, item.Url), forReader: true);
    }
}
