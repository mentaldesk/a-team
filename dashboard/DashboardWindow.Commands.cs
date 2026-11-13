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
    /// <summary>While a menu is open it owns the keyboard: its own keys would otherwise run a command as well.</summary>
    protected override bool OnKeyDown(Key key)
    {
        _bar.KeyPressed();
        if (key == MacCopy && !_menu.Bar.IsOpen() && _commands.IsEnabled(CopyLines))
            return _commands.Execute(CopyLines);
        if (InAssistant && _commands.PendingChord is null && IsTyping(key))
            return base.OnKeyDown(key);
        if (InAssistant && key == Key.Esc)
        {
            _assistant!.Stop();
            return true;
        }
        return (!_menu.Bar.IsOpen() && Press(key)) || base.OnKeyDown(key);
    }

    /// <summary>Partway through a chord, the app hands its next key here before any view, so a question box doesn't type it.</summary>
    internal bool Chording(Key key) => _commands.PendingChord is not null && !_menu.Bar.IsOpen() && Press(key);

    /// <summary>Runs or carries on a chord, showing it on the status bar while it's partway, and saying so when a key
    /// ends one without finishing it.</summary>
    private bool Press(Key key)
    {
        var typed = _commands.PendingChord;
        var handled = _commands.Press(key, out var cancelled);
        _bar.SetChord(_commands.PendingChord);
        if (cancelled)
            Reply(key == Key.Esc ? $"{typed} cancelled" : $"{typed} {KeyChord.Display(key)} isn't a key");
        return handled;
    }

    private void RegisterCommands()
    {
        bool OnWork() => _area == Area.Work;
        bool OnOverseer() => _area == Area.Overseer;
        bool Scrollable() => _sessionFull || _dispatcherOpen;
        bool Reading() => Reader() is not null;
        string[] details = ["Show the session full-screen", "Show the whole lane"];
        string[] hold = ["Pause this role", "Let this role start again"];
        string[] interrupt = ["Interrupt", "Let it start again"];
        string[] mine = ["Show only mine", "Show all"];
        string[] holdTask = ["Hold", "Release hold"];
        _commands
            .Register("assistant.send", "Send question", () => _assistant?.Send(), Key.Enter, isEnabled: () => InAssistant, scopes: [Regions.Assistant])
            .Register("assistant.stop", "Stop answering", () => _assistant?.Stop(), Key.Esc,
                isEnabled: () => InAssistant && _assistant!.Controller.IsAnswering, scopes: [Regions.Assistant])
            .Register("assistant.clear", "Start a new conversation", () => _assistant?.Clear(), Key.L.WithCtrl, isEnabled: () => InAssistant,
                scopes: [Regions.Assistant])
            .Register("assistant.toolCalls", "Show tool calls in full", () => _assistant?.ToggleToolCalls(), chord: "Ctrl+T T", isEnabled: () => InAssistant,
                scopes: [Regions.Assistant])
            .Register("pane.next", "Next pane", SwitchPane, Key.F6, isEnabled: () => _assistant is { Visible: true } || PanelShown)
            .Register("pane.previous", "Previous pane", SwitchPane, Key.F6.WithShift, isEnabled: () => _assistant is { Visible: true } || PanelShown)
            .Register("log.lineDown", "Select the next line of the log", () => Reader()?.MoveLine(+1, extend: false), Key.CursorDown, isEnabled: Reading, scopes: [Regions.Log])
            .Register("log.lineUp", "Select the line above in the log", () => Reader()?.MoveLine(-1, extend: false), Key.CursorUp, isEnabled: Reading, scopes: [Regions.Log])
            .Register("log.extendDown", "Extend the selection down", () => Reader()?.MoveLine(+1, extend: true), Key.CursorDown.WithShift, isEnabled: Reading, scopes: [Regions.Log])
            .Register("log.extendUp", "Extend the selection up", () => Reader()?.MoveLine(-1, extend: true), Key.CursorUp.WithShift, isEnabled: Reading, scopes: [Regions.Log])
            .Register("log.pageUp", "Scroll the log up", () => Scroll(session => session.Page(-1), log => log.Page(-1)), Key.PageUp, isEnabled: Scrollable, scopes: [Regions.Log])
            .Register("log.pageDown", "Scroll the log down", () => Scroll(session => session.Page(+1), log => log.Page(+1)), Key.PageDown, isEnabled: Scrollable, scopes: [Regions.Log])
            .Register("log.top", "Jump to the top of the log", () => Scroll(session => session.Home(), log => log.Home()), Key.Home, isEnabled: Scrollable, scopes: [Regions.Log])
            .Register("log.bottom", "Jump to the bottom of the log", () => Scroll(session => session.End(), log => log.End()), Key.End, isEnabled: Scrollable, scopes: [Regions.Log])
            .Register("log.toolCalls", "Show tool calls in full", () => Reader()?.ToggleToolCalls(), chord: "Ctrl+T T", isEnabled: Reading, scopes: [Regions.Log])
            .Register(CopyLines, () => "Copy the selected lines", () => Copy(session => session.CopySelection()), new Key('l'), isEnabled: Reading,
                menuLabel: () => "Copy selected lines", scopes: [Regions.Log])
            .Register("log.copyAll", () => "Copy the whole log", () => Copy(session => session.CopyAll()), new Key('a'), isEnabled: Reading,
                menuLabel: () => "Copy whole log", scopes: [Regions.Log])
            .Register("log.editor", () => "Open the whole log in your editor", OpenInEditor, new Key('e'), isEnabled: Reading,
                menuLabel: () => "Open whole log in editor", scopes: [Regions.Log])
            .Register("work.right", "Select the column to the right", () => _work.MoveColumn(+1), Key.CursorRight, isEnabled: OnWork, scopes: [Regions.Work])
            .Register("work.left", "Select the column to the left", () => _work.MoveColumn(-1), Key.CursorLeft, isEnabled: OnWork, scopes: [Regions.Work])
            .Register("work.down", "Select the card below", () => _work.MoveCard(+1), Key.CursorDown, isEnabled: OnWork, scopes: [Regions.Work])
            .Register("work.up", "Select the card above", () => _work.MoveCard(-1), Key.CursorUp, isEnabled: OnWork, scopes: [Regions.Work])
            .Register("work.read", "Open", ReadSelected, Key.Enter, isEnabled: () => OnWork() && _work.Selected is not null, onCard: true, scopes: [Regions.Work])
            .Register("work.priority", "Set priority", SetPriority, new Key('p'), isEnabled: () => OnWork() && _work.SelectedCard is not null, onCard: true, scopes: [Regions.Work])
            .Register("work.try", "Try", Try, new Key('t'), isEnabled: () => OnWork() && _work.Selected is { Triable: true }, onCard: true, scopes: [Regions.Work])
            .Register("work.copyBranch", "Copy branch name", CopyBranch, new Key('y'),
                isEnabled: () => OnWork() && _work.Selected is { Branch.Length: > 0 }, onCard: true, scopes: [Regions.Work])
            .Register("work.github", "Open on GitHub", OpenSelected, new Key('g'), isEnabled: () => OnWork() && _work.SelectedUrl is { Length: > 0 }, onCard: true, scopes: [Regions.Work])
            .Register("team.board", "Open team's board on GitHub", OpenBoard, new Key('b'), isEnabled: () => SelectedTeam() is not null)
            .Register("team.vision", () => SelectedTeam() is { } team ? $"Write the vision with me · {team}" : "Write the vision with me",
                () => WriteVision(SelectedTeam()), isEnabled: () => _options.HandOver is not null && SelectedTeam() is not null, name: "Write the vision with me")
            .Register("team.visionReview", () => SelectedTeam() is { } team ? $"Review the vision · {team}" : "Review the vision",
                () => ReviewVision(SelectedTeam()), isEnabled: () => SelectedTeam() is not null, name: "Review the vision")
            .Register("work.approve", "Approve the pitch you're reading", () => Approve(), new Key('a'), isEnabled: () => _approvable is not null)
            .Register("work.accept", "Accept", () => Accept(), new Key('a'), isEnabled: () => Acceptable() is not null, onCard: true, scopes: [Regions.Work])
            .Register("work.comment", "Comment on the item you're reading", () => { }, new Key('c'), isEnabled: () => _shown is not null)
            .Register("work.edit", "Edit the document pitch you're reading", () => { }, new Key('e'), isEnabled: () => _shown is { Document: true })
            .Register("work.attach", "Browse for an image to attach to what you're writing", () => { }, Key.U.WithCtrl,
                isEnabled: () => _shown is not null && _options.Attaching is not null)
            .Register("work.viewImage", () => "View image", () => _image?.View(), isEnabled: () => _image?.Viewable() == true)
            .Register("work.decline", "Decline", () => ReadSelected(declining: true), new Key('x'),
                isEnabled: () => OnWork() && _work.SelectedCard is { Declinable: true }, onCard: true, scopes: [Regions.Work])
            .Register("work.notNow", "Not now", () => ReadSelected(declining: false, shelving: true), new Key('l'),
                isEnabled: () => OnWork() && _work.SelectedCard is { Shelvable: true }, onCard: true, scopes: [Regions.Work])
            .Register("work.hold", () => holdTask[_work.SelectedCard is { Held: true } ? 1 : 0], HoldOrRelease, new Key('h'),
                isEnabled: () => OnWork() && _work.SelectedCard is { Holdable: true }, onCard: true, scopes: [Regions.Work], menuLabels: holdTask)
            .Register("work.secret", "Give secret…", () => GiveSecret(), new Key('k'), isEnabled: () => AskingSecret() is not null, onCard: true, scopes: [Regions.Work])
            .Register("work.fix", "Ask the Dev to fix…", () => AskFix(), new Key('f'), isEnabled: () => Fixable() is not null, onCard: true, scopes: [Regions.Work])
            .Register("work.nextTeam", () => "Next team", () => _work.MoveTeam(+1), Key.PageDown.WithCtrl, isEnabled: OnWork, scopes: [Regions.Work])
            .Register("work.previousTeam", () => "Previous team", () => _work.MoveTeam(-1), Key.PageUp.WithCtrl, isEnabled: OnWork, scopes: [Regions.Work])
            .Register("work.team", () => "Go to team…", GoToTeam, isEnabled: OnWork, menuLabel: () => "Team…", scopes: [Regions.Work])
            .Register("work.new", "New idea", NewIdea, Key.N.WithCtrl, isEnabled: NewIdeaEnabled)
            .Register("work.mine", () => "Show only what's your move", ToggleOnlyMine, chord: "Ctrl+T M", isEnabled: OnWork,
                menuLabel: () => mine[_work.OnlyMine ? 1 : 0], scopes: [Regions.Work], menuLabels: mine)
            .Register("work.retry", () => "Read this team again", () => RetryTeam(_work.Current!.Team), new Key('r'),
                isEnabled: () => OnWork() && _work.Failed, scopes: [Regions.Work])
            .Register("work.refresh", () => "Read what's waiting again", ReadWaiting, Key.F5, isEnabled: OnWork,
                menuLabel: () => "Refresh", scopes: [Regions.Work])
            .Register("overseer.right", "Select the chip to the right", () => _overseer.MoveColumn(+1), Key.CursorRight, isEnabled: OnOverseer, scopes: [Regions.Overseer])
            .Register("overseer.left", "Select the chip to the left", () => _overseer.MoveColumn(-1), Key.CursorLeft, isEnabled: OnOverseer, scopes: [Regions.Overseer])
            .Register("overseer.down", "Select the chip below", () => _overseer.MoveRow(+1), Key.CursorDown, isEnabled: OnOverseer, scopes: [Regions.Overseer])
            .Register("overseer.up", "Select the chip above", () => _overseer.MoveRow(-1), Key.CursorUp, isEnabled: OnOverseer, scopes: [Regions.Overseer])
            .Register("overseer.nextLane", "Select the next team's lane", () => _overseer.MoveLane(+1), Key.Tab, isEnabled: OnOverseer, scopes: [Regions.Overseer])
            .Register("overseer.previousLane", "Select the previous team's lane", () => _overseer.MoveLane(-1), Key.Tab.WithShift, isEnabled: OnOverseer, scopes: [Regions.Overseer])
            .Register("overseer.pageDown", "Scroll the lanes down", () => _overseer.Page(+1), Key.PageDown, isEnabled: OnOverseer, scopes: [Regions.Overseer])
            .Register("overseer.pageUp", "Scroll the lanes up", () => _overseer.Page(-1), Key.PageUp, isEnabled: OnOverseer, scopes: [Regions.Overseer])
            .Register("overseer.details", () => details[_overseer.Board.OnMore ? 1 : 0], OverseerEnter, Key.Enter,
                isEnabled: () => OnOverseer() && (_overseer.Board.OnMore || _cardRun is not null || Chosen() is { LogPath: not null }),
                scopes: [Regions.Overseer, Regions.Team], menuLabels: details)
            .Register("overseer.fold", () => "Fold the lane", () => _overseer.Fold(), Key.Esc,
                isEnabled: () => OnOverseer() && _overseer.CanFold, scopes: [Regions.Overseer])
            .Register("overseer.read", () => "Read the card", ReadChip, new Key('o'),
                isEnabled: () => OnOverseer() && _overseer.Board.SelectedCard is not null, menuLabel: () => "Read t_he card", scopes: [Regions.Overseer])
            .Register("overseer.work", () => "Go to the card in Work", GoToChip, new Key('w'),
                isEnabled: () => OnOverseer() && ChipInWork() is not null, scopes: [Regions.Overseer])
            .Register("overseer.priority", () => "Set priority", RankChip, new Key('p'),
                isEnabled: () => OnOverseer() && _overseer.Board.SelectedCard is not null, menuLabel: () => "Set pri_ority", scopes: [Regions.Overseer])
            .Register("overseer.github", () => "Open on GitHub", OpenChip, new Key('g'),
                isEnabled: () => OnOverseer() && _overseer.Board.SelectedCard is { Url.Length: > 0 }, scopes: [Regions.Overseer])
            .Register("overseer.retry", () => "Read the boards that failed again", () => RetryBoards(_overseer.FailedTeams), new Key('r'),
                isEnabled: () => OnOverseer() && _overseer.Failed, scopes: [Regions.Overseer])
            .Register("overseer.refresh", () => "Read every board again", ReadBoards, Key.F5, isEnabled: OnOverseer,
                menuLabel: () => "Refresh", scopes: [Regions.Overseer])
            .Register("team.down", "Choose the role below", () => MoveRole(+1), Key.CursorDown, isEnabled: () => _team.HasFocus, scopes: [Regions.Team])
            .Register("team.up", "Choose the role above", () => MoveRole(-1), Key.CursorUp, isEnabled: () => _team.HasFocus, scopes: [Regions.Team])
            .Register("view.work", "Work", () => Show(Area.Work), chord: "Ctrl+G W")
            .Register("view.overseer", "Overseer", () => Show(Area.Overseer), chord: "Ctrl+G O")
            .Register("assistant", () => "Assistant", ToggleAssistant, isEnabled: () => _assistant is not null, chord: "Ctrl+G A")
            .Register("view.details", () => "Details", FocusDetails, isEnabled: () => PanelShown, chord: "Ctrl+G I")
            .Register("view.dispatcher", "Dispatcher log", OpenDispatcher, isEnabled: () => !_dispatcherOpen)
            .Register("work.whatsNew", () => "Go to what's new", GoToNew, isEnabled: () => _news.Newest is not null, menuLabel: () => "What's new", chord: "Ctrl+G N")
            .Register("agent.hold", () => Chosen() is { Held: true } ? hold[1] : hold[0], ToggleHold, new Key('h'),
                isEnabled: () => OnOverseer() && _overseer.Board.OnHeading && Chosen() is not null,
                scopes: [Regions.Overseer, Regions.Team, Regions.Log], menuLabels: hold)
            .Register("agent.interrupt", () => _overseer.Board.OnHeading
                    ? UnlessStopped("Interrupt the chosen role", "Let the chosen role start again")
                    : UnlessStopped("Interrupt the agent on this card", "Let the card's agent start again"), ToggleInterrupt, new Key('i'),
                isEnabled: () => OnOverseer() && (_overseer.Board.OnHeading
                    ? Chosen() is { Running: true } or { Held: true }
                    : _cardRun is { State.Running: true } || CardHeld()),
                menuLabel: () => UnlessStopped(interrupt[0], interrupt[1]), scopes: [Regions.Overseer, Regions.Team, Regions.Log], menuLabels: interrupt)
            .Register("dispatch.pass", "Run a dispatch pass now", PassNow)
            .Register("commands", "Commands", OpenCommands, Key.E.WithCtrl, isEnabled: HasApp)
            .Register("settings", "Settings", () => OpenSettings(), chord: "Ctrl+,", isEnabled: HasApp)
            .Register("teams", () => "Teams", () => OpenTeams(), isEnabled: HasApp, menuLabel: () => "_Teams", chord: "Ctrl+G T")
            .Register("teams.new", "New team", () => OpenTeams(newTeam: true), isEnabled: HasApp)
            .Register("help", () => "Show keys", OpenKeys, Key.F1, isEnabled: HasApp, menuLabel: () => "Show _keys")
            .Register("guide", "Guide", () => _showGuide(_area == Area.Work ? GuideDialog.Work : GuideDialog.Overseer))
            .Register("messages", () => "Messages", OpenMessages, isEnabled: HasApp, menuLabel: () => "Messages…")
            .Register("about", "About", OpenAbout, isEnabled: HasApp)
            .Register("trends", "Trends", OpenTrends, isEnabled: () => _options.ShowTrends is not null)
            .Register("spending", "Spending", OpenSpending, isEnabled: () => _options.ShowSpending is not null)
            .Register("decisions", () => SelectedTeam() is { } team ? $"Decisions… · {team}" : "Decisions…",
                () => _options.ShowDecisions?.Invoke(SelectedTeam()!), isEnabled: () => _options.ShowDecisions is not null && SelectedTeam() is not null,
                menuLabel: () => "Decisions…")
            .Register("agent.collapse", () => _dispatcherOpen ? "Back from the dispatcher's log" : "Back to Overseer", Collapse, Key.Esc,
                isEnabled: () => _dispatcherOpen || _sessionFull, menuLabel: () => "Back", scopes: [Regions.Log])
            .Register("quit", "Quit", Quit, Key.Q.WithCtrl);
        foreach (var (id, label, run) in PaneWidths.Commands)
            _commands.Register(id, label, () => run(_paneWidths.Open!), isEnabled: () => _paneWidths.Open?.CanResize == true);
        for (var tab = 0; tab < 9; tab++)
        {
            var index = tab;
            _commands.Register($"work.team{index + 1}",
                () => index < _work.Lanes.Count ? $"Go to {_work.Lanes[index].Team}" : $"Go to team {index + 1}",
                () => _work.PickTab(index), new Key((char)('1' + index)), isEnabled: () => OnWork() && index < _work.Lanes.Count,
                scopes: [Regions.Work], name: $"Go to team {index + 1}");
        }
    }

    // Point Terminal.Gui's own Quit binding at our quit key: removing it leaves PopoverImpl binding Key.Empty, which throws.
    private void SyncQuitKey()
    {
        var key = _commands.KeyFor("quit");
        if (key != Key.Empty)
            Application.SetDefaultKeyBinding(Command.Quit, Bind.All(key));
    }

    private bool HasApp() => App is not null;

    /// <summary>Writes that haven't started are your decisions, so quitting asks before it drops them.</summary>
    private void Quit()
    {
        if (_writes.Queued.Count > 0 && !_confirmQuit(_writes.Running, _writes.Queued))
            return;
        _writes.Drop();
        App?.RequestStop();
    }

    /// <summary>Says what's already under way on <paramref name="key"/>, if anything is.</summary>
    private bool Already(string key)
    {
        if (_writes.Holding(key) is not { } already)
            return false;
        Reply(already.Already);
        return true;
    }

    /// <summary>Queues <paramref name="write"/> behind any already under way, so nothing waits on it.</summary>
    private void Queue(Write write)
    {
        _writes.Add(write);
        _work.ShowWriting(Writing());
        ShowMessage();
    }

    private IEnumerable<WaitingItem> Writing() =>
        [.. _writes.Running is { Item: { } running } ? [running] : Array.Empty<WaitingItem>(),
            .. _writes.Queued.Select(write => write.Item).OfType<WaitingItem>()];

    /// <summary>The status bar's one hint, as TuiCode words it: every other key is in the sheet it opens. Only a card
    /// asking for a secret adds its own, since nothing else on it says how to give one.</summary>
    private void ShowKeysHint()
    {
        var keys = _commands.Keys.For("help").FirstOrDefault()?.Display;
        var secret = AskingSecret() is not null ? _commands.Keys.For("work.secret").FirstOrDefault()?.Display.ToLowerInvariant() : null;
        _hintsForSecret = secret is not null;
        _bar.Hints.Show([
            .. secret is null ? [] : new (string, Action)[] { ($"{secret} Give secret", () => _commands.Execute("work.secret")) },
            .. keys is null ? [] : new (string, Action)[] { ($"Press {keys} for help", () => _commands.Execute("help")) }]);
    }
}
