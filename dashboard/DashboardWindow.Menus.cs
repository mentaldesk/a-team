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
    private void Copy(Func<CardPanel, LogCopy> copy)
    {
        if (Reader() is not { } pane)
            return;
        var copied = copy(pane);
        Reply(Copying.Said(Copying.Copy(_options.Clipboard ?? App?.Clipboard, copied.Text), truncated: copied.Capped));
    }

    private void CopyBranch()
    {
        if (_work.Selected is not { Branch.Length: > 0 } card)
            return;
        Reply(Copying.Said(Copying.Copy(_options.Clipboard ?? App?.Clipboard, card.Branch), card.Branch));
    }

    private void OpenCommands()
    {
        if (App is { } app)
            CommandsDialog.Show(app, _commands, InCommands);
    }

    private void GoToTeam()
    {
        if (App is { } app && CommandsDialog.Pick(app, "Go to team", _teamNames) is { } team)
            _work.Pick(team);
    }

    private void OpenKeys()
    {
        if (App is { } app)
            KeysSheet.Show(app, Keys);
    }

    internal KeySheet Keys => KeysSheet.For(_commands, Offered, Region);

    /// <summary>While the reader or the Guide is open, only what works from it: the rest acts on the board underneath.</summary>
    private IReadOnlyList<CommandDescriptor> Offered =>
        _shown is null && _paneWidths.Open is null
            ? _commands.Enabled
            : [.. _commands.Enabled.Where(command => command.Id is "help" or "commands"
                || _shown is not null && command.Id is "work.new" or "work.viewImage"
                || PaneWidths.Commands.Any(resize => resize.Id == command.Id))];

    /// <summary>What's offered, and going to a chip's card in Work where it isn't there, so running it can say why.</summary>
    internal IReadOnlyList<CommandDescriptor> InCommands =>
        _shown is null && _paneWidths.Open is null && _area == Area.Overseer && _overseer.Board.SelectedCard is not null && ChipInWork() is null
            ? [.. _commands.Registered.Where(command => command.Id == "overseer.work" || _commands.IsEnabled(command.Id))]
            : Offered;

    /// <summary>Where the window is now, for the keys that work only there.</summary>
    internal CommandScope Region =>
        InAssistant ? Regions.Assistant
        : _dispatcherOpen || _sessionFull ? Regions.Log
        : _area == Area.Work ? Regions.Work
        : _team.HasFocus ? Regions.Team
        : Regions.Overseer;

    private void OpenAbout()
    {
        if (App is { } app)
            AboutDialog.Show(app, _version);
    }

    /// <summary>What's waiting now is Work's count, once it has read one.</summary>
    private void OpenTrends() =>
        _options.ShowTrends?.Invoke([.. _teamNames.Select(team => (team, _readAt is null ? (int?)null : _work.Items.Count(item => item.Team == team && item.WaitsOnYou)))]);

    private void OpenSpending() => _options.ShowSpending?.Invoke(_teamNames, OpenPitch);

    /// <summary>The reader opens over Spending once the pitch has been read.</summary>
    private void OpenPitch(string team, SpendingItem pitch)
    {
        var item = new WaitingItem(pitch.Number, pitch.Title, pitch.Status, pitch.Url, team, Pitch: true);
        ReadBody(item, (read, body) => ShowBody(read, body, pitch.Url), forReader: true);
    }

    private void OpenSettings()
    {
        if (App is not { } app)
            return;
        SettingsScreen.Show(app, _settings, _commands, KeyOverrides, ShowIcons, _auto, _showGuide, _options.MacKeys);
        _limits = _settings.ReadLimits();
        SyncQuitKey();
        ShowKeysHint();
        _menu.Refresh();
    }

    private void OpenTeams(bool newTeam = false)
    {
        if (App is not { } app)
            return;
        var before = _teams.Names();
        var (removed, vision) = TeamsDialog.Show(app, _teams, _options.Start, newTeam, _showGuide, _checks, Icons.Resolve(_settings.ReadIcons(), _auto));
        if (vision is not null && _teams.Names().Contains(vision))
        {
            WriteVision(vision);
            return;
        }
        if (_teams.Names().Except(before).Any() || RolesChanged())
        {
            _options.HandOver?.Invoke(new TeamsChanged(_area, _area == Area.Work ? _work.Place : _left));
            return;
        }
        Forget(removed);
        _menu.Refresh();
    }

    /// <summary>Whether a team now runs other roles than the ones watched.</summary>
    private bool RolesChanged() =>
        _teamNames.Intersect(_teams.Names()).Any(team =>
            !_teams.Roles(team).SequenceEqual(_watches.Where(watch => watch.Team == team).Select(watch => watch.Role)));

    /// <summary>Takes removed teams out of Work and Overseer.</summary>
    internal void Forget(IReadOnlyList<string> teams)
    {
        if (!teams.Any(_teamNames.Contains))
            return;
        _watches.RemoveAll(watch => teams.Contains(watch.Team));
        _teamNames.RemoveAll(teams.Contains);
        _work.Forget(teams);
        _overseer.Forget(teams);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>The vocabulary the cards, the logs and the roles draw their icons from, together, with Auto resolved
    /// here so no view has to know what this terminal answered.</summary>
    private void ShowIcons(IconStyle style)
    {
        var drawn = Icons.Resolve(style, _auto);
        _drawn = drawn;
        _work.ShowIcons(drawn);
        _overseer.ShowIcons(drawn);
        _card.ShowIcons(drawn);
    }

    private void Collapse()
    {
        if (_dispatcherOpen)
            CloseDispatcher();
        else
            CloseSession();
    }

    /// <summary>View › Dispatcher log: the dispatcher's whole log full-screen over whatever area is showing.</summary>
    private void OpenDispatcher()
    {
        _dispatcherOpen = true;
        ShowWholeLog();
        ShowDispatcher(_clock.GetUtcNow());
        Arrange();
        _dispatchFrame.SetFocus();
        ShowMessage();
    }

    private void CloseDispatcher()
    {
        _dispatcherOpen = false;
        Arrange();
        FocusArea();
        ShowMessage();
    }

    /// <summary>Scrolls the session full-screen, or the dispatcher's log while that's open.</summary>
    private void Scroll(Action<CardPanel> session, Action<LogView> log)
    {
        if (_dispatcherOpen)
            log(_dispatchAll);
        else if (_sessionFull)
            session(_card);
    }

    /// <summary>The session whose lines can be selected and copied: the card's or the role's, full-screen.</summary>
    private CardPanel? Reader() => _area == Area.Overseer && _sessionFull && !_dispatcherOpen ? _card : null;

    private static string Version() =>
        typeof(DashboardWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "";

    private static string? ReadText(string path)
    {
        try { return File.ReadAllText(path).Trim(); }
        catch (IOException) { return null; }
    }

    private static IReadOnlyList<LogLine> ReadTail(string path, int count)
    {
        try
        {
            return [.. File.ReadLines(path).TakeLast(count).Select(DispatchLine.Read)];
        }
        catch (IOException) { return [new LogLine("(the dispatcher hasn't run yet)", LogLineKind.Prose)]; }
    }
}
