using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Keys;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>The dashboard's settings, a page at a time: the pages on the left, the one picked on the right.</summary>
public sealed class SettingsDialog : Dialog
{
    internal const string TeamsPage = "Teams";
    internal const string OverseerPage = "Overseer";
    private const string LimitsHeading = "Highlight a card that stays in a column longer than";
    private const string LimitsHint = "e.g. 30m, 2h, 3d or 2w; blank for no limit";
    private const int LimitsRow = 2;
    private const int LimitWidth = 10;
    private static readonly Key Apply = Key.Enter.WithCtrl;
    private static readonly Key GuideKey = Key.G.WithCtrl;
    private static readonly Key ResetKey = Key.R.WithCtrl;
    internal const string KeysPageName = "Keyboard Shortcuts";
    private const string KeepHint = "Ctrl+Enter keep";
    private const string CancelHint = "Esc cancel";
    private const string GuideHint = "Ctrl+G guide";
    private const string PauseHint = "p pause";
    private const string ResumeHint = "p resume";
    private const string EditHint = "Enter edit";
    private const string NewHint = "n new";
    private const string RemoveTeamHint = "x remove";
    private const string Working = "working";
    private const string Paused = "paused";
    private const string TodayHeading = "Today";
    private const string Unreadable = "can't read this file";
    private const string Separator = " · ";
    private const string ToolCalls = "Show tool calls in full";
    private const string IconsHeading = "Icons:";
    private const string IconLegend = "run  idle  paused  ok  error  here  tool";
    private const string ThisTerminal = "this terminal: ";
    private const int Inset = 1;
    private const int Gap = 1;
    private const int StatusLines = 1;
    private const int Indent = 2;
    private const int TeamListRow = 1;
    private const int PageHintsAbove = 2;
    private const int IconsHeadingRow = 2;
    private const int IconStylesRow = IconsHeadingRow + 1;

    private static readonly (IconStyle Style, string Name)[] IconChoices =
        [(IconStyle.Auto, "Automatic"), (IconStyle.NerdFont, "Nerd Font"), (IconStyle.Unicode, "Unicode")];

    private static readonly int IconLegendRow = IconStylesRow + IconChoices.Length + 1;

    private readonly InputScopes _scopes = new();
    private readonly List<Page> _pages;
    private readonly List<View> _hints = [];
    private readonly ListView _picker = new();
    private readonly CheckBox _toolCalls;
    private readonly OptionSelector _iconStyles;
    private readonly TeamConfigs _teams;
    private readonly TeamStart? _start;
    private readonly IconSetting _icons;
    private readonly IconStyle _auto;
    private readonly List<TeamRow> _teamRows;
    private readonly TeamList _teamList = new();
    private readonly Label _teamHeader = new();
    private readonly MessageBar _message = new();
    private readonly List<string> _removed = [];
    private readonly TeamChecks? _checks;
    private readonly Func<string, Task<Reading>>? _readToday;
    private readonly Dictionary<string, TeamToday?> _today = [];
    private readonly Action<string> _showGuide;
    private readonly List<(string Column, TextField Field)> _limits;
    private bool _refreshingTeams;

    public SettingsDialog(
        ThemeSetting theme,
        IconSetting icons,
        bool expandToolCalls,
        CommandRegistry commands,
        TeamConfigs teams,
        Action redraw,
        IconStyle auto,
        string? page = null,
        TeamStart? start = null,
        Func<string, Task<TeamHealth>>? check = null,
        Action<string>? showGuide = null,
        TeamChecks? checks = null,
        IReadOnlyDictionary<string, string>? limits = null,
        Func<string, Task<Reading>>? today = null,
        ITerminalKeys? terminalKeys = null,
        KeyOverrides? keys = null)
    {
        _showGuide = showGuide ?? (_ => { });
        ShowHelp = sheet =>
        {
            if (App is { } app)
                KeysSheet.Show(app, sheet);
        };
        _limits = [.. OverseerBoard.Columns.Select(column => (column,
            new TextField { Width = LimitWidth, Text = limits is not null && limits.TryGetValue(column, out var limit) ? limit : "" }))];
        _teams = teams;
        _start = start;
        _icons = icons;
        _auto = auto;
        check ??= start is null ? null : start.Check;
        _checks = checks ?? (check is null ? null : new TeamChecks(check, teams.Stamp));
        _readToday = today ?? (start is null ? null : start.Today);
        _teamRows = [.. teams.Names().Select(teams.Row)];
        Confirm = confirm =>
        {
            if (App is { } app)
                app.Run(confirm);
        };
        keys ??= new KeyOverrides(commands.Library, commands.Keys, () => null, _ => { });
        KeysPage = new KeysPage(commands.Library, _scopes, keys, scope => scope.Name, confirm => Confirm(confirm));

        Title = "Settings";
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill(StatusLines);

        var themes = new OptionSelector
        {
            Orientation = Orientation.Vertical,
            Labels = [.. BundledThemes.Names],
            Value = BundledThemes.Names.ToList().IndexOf(theme.Current),
        };
        themes.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } index && index >= 0 && index < BundledThemes.Names.Count)
            {
                theme.Preview(BundledThemes.Names[index]);
                redraw();
            }
        };
        _toolCalls = new CheckBox
        {
            Text = ToolCalls,
            Value = expandToolCalls ? CheckState.Checked : CheckState.UnChecked,
        };
        _iconStyles = new OptionSelector
        {
            Orientation = Orientation.Vertical,
            Labels = [.. IconChoices.Select(choice => IconRow(choice, auto))],
            Value = Math.Max(0, Array.FindIndex(IconChoices, choice => choice.Style == icons.Current)),
        };
        _iconStyles.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } index && index >= 0 && index < IconChoices.Length)
            {
                icons.Preview(IconChoices[index].Style);
                redraw();
            }
        };
        _teamList.Pause = TogglePause;
        _teamList.New = () => NewTeam();
        _teamList.Drop = RemoveTeam;
        RepairTeam = team => App is { } app && _start is { } start &&
            StepDialog.Show(app, start.Repair(team, _teams.Settings(team))) == Answer.Done;
        EditTeam = (team, settings, save) =>
            App is { } app
                ? TeamForm.Show(app, team, settings, save, ListProjects, _checks?.Latest(team),
                    () => Repaired(team), Icons.Resolve(_icons.Current, _auto), _start is { } start ? start.Repos : null)
                : null;
        CreateTeam = (again, create) =>
        {
            if (App is not { } app || _start is null)
                return null;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var directory = _teams.TeamsDirectory.StartsWith(home, StringComparison.Ordinal)
                ? $"~{_teams.TeamsDirectory[home.Length..]}"
                : _teams.TeamsDirectory;
            return again is null
                ? TeamForm.ShowNew(app, _start.Defaults(), _teams.Names(), directory, create, _start.Projects, _start.Me(),
                    repos: _start.Repos)
                : TeamForm.ShowNew(app, _teams.Settings(again), [.. _teams.Names().Where(name => name != again)], directory, create,
                    _start.Projects, Task.FromResult(""), again, _start.Repos);
        };
        FollowTeam = team => App is { } app && _start is not null
            ? _start.Follow(_teams, team, step => StepDialog.Show(app, step), () => VisionFor = team)
            : (string.Empty, Schemes.Base);
        ConfirmRemove = (team, repo, kept) => App is { } app && RemoveTeamDialog.Show(app, team, repo, kept);
        _teamList.ValueChanged += (_, _) =>
        {
            if (!_refreshingTeams)
                ShowTeam();
        };
        _teamHeader.GettingAttributeForRole += (_, e) =>
        {
            var attribute = e.Result ?? GetAttributeForRole(e.Role);
            e.Result = attribute with { Foreground = attribute.Background, Background = attribute.Foreground };
            e.Handled = true;
        };

        _pages =
        [
            new Page("Theme", [new Placed(themes)], () => [], () => []),
            new Page(KeysPageName, [new Placed(KeysPage)], () => [],
                () => [new("Enter", "Add a key"), new("Del", "Remove the key"), new(KeyNames.Short(ResetKey), "Reset to defaults…"), new("Type", "Filter")]),
            new Page("Dashboard", DashboardRows(), () => [], () => []),
            .. terminalKeys is null ? [] : new[] { new Page(TerminalPage.Name, [new Placed(Terminal = new TerminalPage(terminalKeys, Say))], () => [], () => []) },
            new Page(TeamsPage, [new Placed(_teamHeader), new Placed(_teamList, 0, TeamListRow)], () => SelectedTeam() is { } team
                ? [team.Paused ? ResumeHint : PauseHint, EditHint, RemoveTeamHint, NewHint]
                : [NewHint],
                () => SelectedTeam() is { } team
                    ? [new("p", team.Paused ? "Resume" : "Pause"), new("Enter", "Edit"), new("x", "Remove"), new("n", "New team")]
                    : [new("n", "New team")]),
            new Page(OverseerPage, LimitRows(), () => [LimitsHint], () => []),
        ];

        var content = Dim.Func(_ => Math.Max(1, Viewport.Height - 1 - _message.Lines), this);
        _picker.X = Inset;
        _picker.Y = 0;
        _picker.Width = _pages.Max(page => page.Name.Length);
        _picker.Height = content;
        _picker.SetSource(new ObservableCollection<string>(_pages.Select(page => page.Name)));
        _picker.Value = Math.Max(0, _pages.FindIndex(shown => shown.Name == page));
        _picker.ValueChanged += (_, _) => ShowPage();

        var rule = new Line { X = Pos.Right(_picker) + Gap, Y = 0, Orientation = Orientation.Vertical, Height = content };
        foreach (var placed in _pages.SelectMany(page => page.Rows))
        {
            placed.View.X = Pos.Right(rule) + Gap + placed.X;
            placed.View.Y = placed.Y;
        }
        KeysPage.Width = Dim.Fill(Inset);
        KeysPage.Height = Dim.Func(_ => Math.Max(1, Viewport.Height - 1 - _message.Lines - PageHintsAbove), this);
        if (Terminal is not null)
        {
            Terminal.Width = Dim.Fill(Inset);
            Terminal.Height = content;
        }
        _teamHeader.Width = Dim.Fill(Inset);
        _teamList.Width = Dim.Fill(Inset);
        _teamList.Height = Dim.Func(_ => Math.Max(1, Viewport.Height - 1 - _message.Lines - PageHintsAbove - TeamListRow), this);
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);

        Add(_picker, rule);
        foreach (var placed in _pages.SelectMany(page => page.Rows))
            Add(placed.View);
        Add(_message);
        ShowTeams();
        ShowPage();
        if (page == TeamsPage)
            _teamList.SetFocus();
    }

    internal bool Confirmed { get; private set; }

    internal bool ExpandToolCalls => _toolCalls.Value == CheckState.Checked;

    internal ListView Pages => _picker;

    internal KeysPage KeysPage { get; }

    internal Action<ConfirmDialog> Confirm { get; set; }

    internal ListView Teams => _teamList;

    internal TerminalPage? Terminal { get; }

    /// <summary>Shows Help over the dialog.</summary>
    internal Action<KeySheet> ShowHelp { get; set; }

    /// <summary>What F1 shows: the page's own keys, then the dialog's.</summary>
    internal KeySheet Sheet
    {
        get
        {
            var page = _pages[_picker.Value ?? 0];
            return DialogHost.Sheet(page.Name,
            [
                .. page.Keys(),
                new(KeyNames.Short(Apply), "Keep"),
                new(KeyNames.Short(GuideKey), "Guide"),
                new("Tab/Shift+Tab", "Next / previous"),
                new("Esc", "Cancel"),
            ]);
        }
    }

    internal string TeamHeader => _teamHeader.Text;

    internal MessageBar Message => _message;

    internal IReadOnlyList<(string Column, TextField Field)> LimitFields => _limits;

    /// <summary>Each column's limit as entered, blank for none.</summary>
    internal IReadOnlyDictionary<string, string> Limits =>
        _limits.ToDictionary(limit => limit.Column, limit => limit.Field.Text.Trim());

    /// <summary>Opens the team form and returns what it saved, or null where it was cancelled.</summary>
    internal Func<string, TeamSettings, Func<TeamSettings, string?>, TeamSettings?> EditTeam { get; set; }

    /// <summary>Opens the new team form over <c>create</c>, filled in from the team named where Back returned to it, and
    /// returns the name it created, or null where it was cancelled.</summary>
    internal Func<string?, Func<string, TeamSettings, string?>, string?> CreateTeam { get; set; }

    /// <summary>Takes a team just created through the steps to its first run, and returns the line to leave, or null
    /// where Back went past the first step.</summary>
    internal Func<string, (string Message, Schemes Scheme)?> FollowTeam { get; set; }

    /// <summary>Asks whether to remove a team, given its name, its repo and the name its file would be kept as.</summary>
    internal Func<string, string, string, bool> ConfirmRemove { get; set; }

    /// <summary>Sets a team's board up again, once you've seen what that does, and says whether it did.</summary>
    internal Func<string, bool> RepairTeam { get; set; }

    /// <summary>The new team whose vision you chose to write now, which closes the dialog for the interview.</summary>
    internal string? VisionFor { get; private set; }

    /// <summary>The teams removed while the dialog was open.</summary>
    internal IReadOnlyList<string> RemovedTeams => _removed;

    /// <summary>Enter reaches a Dialog as Accept, from any of its lists alike, and never as a key.
    /// The hints close the dialog from their own Accepting, so nothing here does.</summary>
    protected override bool OnAccepting(CommandEventArgs args) =>
        !_teamList.HasFocus || OpenTeam();

    /// <summary>The app hands each key here before any view, so the Keys page records it, as AppShell does.</summary>
    internal bool Route(Key key) => _scopes.Top is not null && _scopes.Handle(key) != KeyResult.Pass;

    protected override bool OnKeyDown(Key key)
    {
        if (key == ResetKey && KeysPage.Visible)
            return KeysPage.ResetButton.InvokeCommand(Command.Accept) is not false;
        if (key == Apply)
            return Close(confirmed: true);
        if (key == Key.Esc)
            return Close(confirmed: false);
        if (key == GuideKey)
            return Guide();
        if (key == Key.F1)
            return DialogHost.ShowOver(this, Sheet, ShowHelp);
        return base.OnKeyDown(key);
    }

    /// <summary>Runs the dialog, keeping what was picked in it only if it was accepted, and returns the teams
    /// removed in it, which are gone either way, and the new team whose vision to write now.</summary>
    public static (IReadOnlyList<string> Removed, string? Vision) Show(
        IApplication app,
        DashboardSettings settings,
        CommandRegistry commands,
        KeyOverrides keys,
        Action<IconStyle> showIcons,
        IconStyle auto,
        TeamConfigs teams,
        string? page = null,
        TeamStart? start = null,
        bool newTeam = false,
        Action<string>? showGuide = null,
        TeamChecks? checks = null,
        MacKeys? macKeys = null)
    {
        var theme = ThemeSetting.Live(settings);
        var icons = new IconSetting(settings.ReadIcons(), showIcons, settings.WriteIcons);
        using var dialog = new SettingsDialog(
            theme, icons, settings.ReadExpandToolCalls(), commands, teams, () => app.LayoutAndDraw(true), auto, page, start, showGuide: showGuide, checks: checks,
            limits: settings.ReadLimits(), terminalKeys: macKeys is null ? null : new TerminalKeys(macKeys), keys: keys);
        if (newTeam)
            app.Invoke(() => dialog.NewTeam());
        EventHandler<Key> route = (_, key) => key.Handled = key.Handled || dialog.Route(key);
        app.Keyboard.KeyDown += route;
        try
        {
            app.Run(dialog);
        }
        finally
        {
            app.Keyboard.KeyDown -= route;
        }
        dialog.Store(theme, icons, settings);
        return (dialog.RemovedTeams, dialog.VisionFor);
    }

    /// <summary>Keeps what the dialog was left holding, or puts back what was in effect before it opened.</summary>
    internal void Store(ThemeSetting theme, IconSetting icons, DashboardSettings settings)
    {
        if (!Confirmed)
        {
            theme.Cancel();
            icons.Cancel();
            KeysPage.Revert();
            return;
        }
        theme.Keep();
        icons.Keep();
        settings.WriteExpandToolCalls(ExpandToolCalls);
        settings.WriteLimits(Limits);
        if (KeysPage.IsChanged)
            KeysPage.Keep();
    }

    internal bool Guide()
    {
        _showGuide(GuideDialog.Teams);
        return true;
    }

    /// <summary>Starts the selected team if it's paused and pauses it if it's working, straight away: it's the
    /// team's own file, not a setting the dialog keeps or cancels.</summary>
    private void TogglePause()
    {
        if (_teamList.Value is not { } index || SelectedTeam() is not { } team)
            return;
        if (team.Problem is not null)
        {
            Say($"Fix {team.Name}.json before starting or pausing {team.Name}: a-team can't read it.");
            return;
        }
        try
        {
            _teams.SetWorking(team.Name, team.Paused);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Say($"Couldn't {(team.Paused ? "start" : "pause")} {team.Name}: {e.Message}");
            return;
        }
        _teamRows[index] = _teams.Row(team.Name);
        ShowTeams();
        ShowTeam();
    }

    /// <summary>Moves the selected team's file aside once you've said yes, straight away like pausing it.</summary>
    private void RemoveTeam()
    {
        if (_teamList.Value is not { } index || SelectedTeam() is not { } team)
            return;
        var kept = _teams.RemovedName(team.Name);
        if (!ConfirmRemove(team.Name, team.Repo, kept))
            return;
        try
        {
            kept = _teams.Remove(team.Name);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Say($"Couldn't remove {team.Name}: {e.Message}");
            return;
        }
        _removed.Add(team.Name);
        _teamRows.RemoveAt(index);
        ShowTeams();
        ShowTeam();
        Say($"Removed {team.Name}. Rename {kept} back to {team.Name}.json to bring it back.", Schemes.Accent);
    }

    /// <summary>Opens the selected team's form, and saves what it's left holding straight into the team's file.</summary>
    internal bool OpenTeam()
    {
        if (_teamList.Value is not { } index || SelectedTeam() is not { } team)
            return true;
        if (team.Problem is { } problem)
        {
            Say($"{team.Name}.json, {problem}");
            return true;
        }
        TeamSettings before;
        try
        {
            before = _teams.Settings(team.Name);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _teamRows[index] = _teams.Row(team.Name);
            ShowTeams();
            Say($"Couldn't open {team.Name}: {e.Message}");
            return true;
        }
        if (EditTeam(team.Name, before, after => SaveTeam(team.Name, before, after)) is not { } saved)
            return true;
        _teamRows[index] = _teams.Row(team.Name);
        Check(team.Name);
        ReadToday(team.Name);
        ShowTeams();
        ShowTeam();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (saved.Warning(home) is { } warning)
            Say(warning, Schemes.Accent);
        else if (saved.Notice(home) is { } notice)
            Say(notice, Schemes.Base);
        return true;
    }

    /// <summary>Opens the form for a new team, then offers what it needs to get to work, and leaves it selected.</summary>
    internal bool NewTeam()
    {
        if (CreateTeam(null, CreateTeamFile) is not { } team)
            return true;
        while (true)
        {
            ReadTeams(team);
            if (FollowTeam(team) is { } said)
            {
                ReadTeams(team);
                Say(said.Message, said.Scheme);
                if (VisionFor == team)
                    Close(confirmed: true);
                return true;
            }
            var was = team;
            if (CreateTeam(was, (name, settings) => RedoTeamFile(was, name, settings)) is not { } redone)
            {
                _teams.Delete(was);
                ReadTeams(was);
                Say($"{was} is cancelled. Its clone, App and project are still there.", Schemes.Base);
                return true;
            }
            team = redone;
        }
    }

    private string? RedoTeamFile(string team, string name, TeamSettings settings) =>
        _start is { } start ? start.Redo(_teams, team, name, settings) : "There's no a-team install here to start a team from.";

    private string? CreateTeamFile(string team, TeamSettings settings) =>
        _start is { } start ? start.Create(_teams, team, settings) : "There's no a-team install here to start a team from.";

    private void ReadTeams(string selected)
    {
        _teamRows.Clear();
        _teamRows.AddRange(_teams.Names().Select(_teams.Row));
        _teamList.Value = null;
        CheckTeams();
        ShowTeams();
        if (_teamRows.Count > 0)
            _teamList.Value = Math.Max(0, _teamRows.FindIndex(row => row.Name == selected));
        _teamList.SetFocus();
        ShowTeam();
    }

    private string? SaveTeam(string team, TeamSettings before, TeamSettings after)
    {
        try
        {
            _teams.Save(team, before, after);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return $"Couldn't save {team}: {e.Message}";
        }
    }

    private static Task<Reading> ListProjects(string owner) =>
        new TeamCommand("gh").Read("project", "list", "--owner", owner, "--format", "json", "--limit", "100");

    private TeamRow? SelectedTeam() =>
        _teamList.Value is { } index && index >= 0 && index < _teamRows.Count ? _teamRows[index] : null;

    /// <summary>What's wrong with the selected team's file, if anything, and the pause hint that fits it.</summary>
    private void ShowTeam()
    {
        if (_pages[_picker.Value ?? 0].Name != TeamsPage)
            return;
        if (SelectedTeam() is { Problem: { } problem } team)
            Say($"{team.Name}.json, {problem}");
        else
            _message.Clear();
        ShowHints(_pages[_picker.Value ?? 0].Hints());
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void ShowTeams()
    {
        var selected = _teamList.Value;
        var (header, rows) = TeamColumns();
        _teamHeader.Text = header;
        _refreshingTeams = true;
        _teamList.SetSource(new ObservableCollection<string>(rows));
        _teamList.Value = _teamRows.Count == 0 ? null : Math.Min(selected ?? 0, _teamRows.Count - 1);
        _refreshingTeams = false;
        if (_teamList.Value != selected)
            ShowTeam();
    }

    private (string Header, IReadOnlyList<string> Rows) TeamColumns()
    {
        List<string> headings = ["Team", "Repo", "Status"];
        if (_readToday is not null)
            headings.Add(TodayHeading);
        if (_checks is not null)
            headings.Add("Health");
        var cells = _teamRows.Select(team => (string[])[team.Name, team.Repo, Status(team),
            .. _readToday is null ? Array.Empty<string>() : [Today(team)], Health(team)]).ToList();
        var widths = Enumerable.Range(0, headings.Count)
            .Select(column => cells.Select(row => row[column].Length).Append(headings[column].Length).Max())
            .ToArray();
        string Cell(string cell, int column) =>
            headings[column] == TodayHeading ? cell.PadLeft(widths[column])
            : column < headings.Count - 1 ? cell.PadRight(widths[column])
            : cell;
        string Line(IReadOnlyList<string> row) =>
            string.Join("  ", row.Take(headings.Count).Select(Cell)).TrimEnd();
        return (Line(headings), [.. cells.Select(Line)]);
    }

    /// <summary>The Today column: nothing until the team's cost has been read, or where it can't be.</summary>
    private string Today(TeamRow team) => _today.GetValueOrDefault(team.Name)?.Column ?? "";

    /// <summary>Reads what each readable team has spent today, each filling its row in when it answers.</summary>
    private void ReadToday()
    {
        foreach (var team in _teamRows.Where(team => team.Problem is null))
            ReadToday(team.Name);
    }

    private void ReadToday(string team) =>
        _readToday?.Invoke(team).ContinueWith(read => OnUi(() =>
        {
            _today[team] = read.Status == TaskStatus.RanToCompletion ? TeamToday.Of(read.Result) : null;
            ShowTeams();
        }), TaskContinuationOptions.ExecuteSynchronously);

    /// <summary>The health column: nothing where there's no way to check, and <c>checking…</c> until the check answers.</summary>
    private string Health(TeamRow team) =>
        _checks is null ? ""
        : team.Problem is { } problem ? TeamHealth.Unreadable(problem).Column
        : _checks.Latest(team.Name) is not { IsCompleted: true } health ? TeamHealth.Checking
        : health.Status == TaskStatus.RanToCompletion ? health.Result.Column
        : "check failed";

    /// <summary>Repairs the team's board and checks it again, so its row and the open form follow; null where nothing
    /// was repaired.</summary>
    internal Task<TeamHealth>? Repaired(string team) => RepairTeam(team) ? Check(team) : null;

    /// <summary>Starts checking every readable team not checked yet, each filling its row in when it answers.</summary>
    private void CheckTeams()
    {
        if (_checks is null)
            return;
        foreach (var team in _teamRows.Where(team => team.Problem is null))
            Follow(_checks.For(team.Name));
    }

    private Task<TeamHealth>? Check(string team) => _checks is null ? null : Follow(_checks.Check(team));

    private Task<TeamHealth> Follow(Task<TeamHealth> health)
    {
        health.ContinueWith(_ => OnUi(ShowTeams), TaskContinuationOptions.ExecuteSynchronously);
        return health;
    }

    private void OnUi(Action action)
    {
        if (App is { } app)
            app.Invoke(action);
        else
            action();
    }

    private string Status(TeamRow team) =>
        team.Problem is not null ? Unreadable
        : team.Paused ? Paused
        : _today.GetValueOrDefault(team.Name) is { Reached: true } ? TeamToday.BudgetReached
        : Working;

    /// <summary>Shows the page the list is on, and only that one, with the hints that page answers to.</summary>
    private void ShowPage()
    {
        var selected = _picker.Value ?? 0;
        if (_pages[selected].Name == TeamsPage)
        {
            CheckTeams();
            ReadToday();
        }
        for (var index = 0; index < _pages.Count; index++)
            foreach (var placed in _pages[index].Rows)
                placed.View.Visible = index == selected;
        ShowHints(_pages[selected].Hints());
        ShowTeam();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void Say(string message, Schemes scheme = Schemes.Error)
    {
        _message.Show(message, scheme);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private bool Close(bool confirmed)
    {
        if (confirmed && UnreadableLimit() is { } column)
        {
            _picker.Value = _pages.FindIndex(page => page.Name == OverseerPage);
            _limits.First(limit => limit.Column == column).Field.SetFocus();
            Say($"Overseer: can't read the limit for {column}. Use a number and m, h, d or w, like 3d, or leave it blank.");
            return true;
        }
        Confirmed = confirmed;
        RequestStop();
        return true;
    }

    /// <summary>The page's own hints at the foot of the page, and the dialog's beneath them at its left edge.</summary>
    private void ShowHints(IReadOnlyList<string> page)
    {
        foreach (var hint in _hints)
        {
            Remove(hint);
            hint.Dispose();
        }
        _hints.Clear();
        var dialogRow = Pos.Func(_ => Math.Max(0, Viewport.Height - 1 - _message.Lines), this);
        _hints.AddRange(Hints(page, KeysPage.X, dialogRow - PageHintsAbove));
        _hints.AddRange(Hints([KeepHint, CancelHint, GuideHint], Inset, dialogRow));
        foreach (var hint in _hints)
            Add(hint);
    }

    /// <summary>A hint row, each hint clickable and the separators between them not.</summary>
    private View[] Hints(IReadOnlyList<string> texts, Pos x, Pos y)
    {
        List<View> row = [];
        foreach (var text in texts)
        {
            if (row.Count > 0)
            {
                var separator = new Label { Text = Separator, X = x, Y = y, CanFocus = false };
                row.Add(separator);
                x = Pos.Right(separator);
            }
            var hint = new Button
            {
                Text = text,
                X = x,
                Y = y,
                NoDecorations = true,
                NoPadding = true,
                ShadowStyle = ShadowStyles.None,
                HotKeySpecifier = (Rune)0xffff,
                CanFocus = false,
            };
            hint.Accepting += (_, args) => args.Handled = Run(text);
            row.Add(hint);
            x = Pos.Right(hint);
        }
        return [.. row];
    }

    private bool Run(string hint)
    {
        if (hint is PauseHint or ResumeHint)
        {
            TogglePause();
            return true;
        }
        if (hint == NewHint)
        {
            _teamList.SetFocus();
            return NewTeam();
        }
        if (hint == EditHint)
        {
            _teamList.SetFocus();
            return OpenTeam();
        }
        if (hint == RemoveTeamHint)
        {
            _teamList.SetFocus();
            RemoveTeam();
            return true;
        }
        return hint == GuideHint ? Guide() : Close(confirmed: hint == KeepHint);
    }

    /// <summary>The first column whose limit isn't one Overseer can read, or null when they all are.</summary>
    internal string? UnreadableLimit() =>
        _limits.FirstOrDefault(limit => !ColumnLimits.TryParse(limit.Field.Text, out _)).Column;

    private IReadOnlyList<Placed> LimitRows()
    {
        var label = OverseerBoard.Columns.Max(column => column.Length) + Gap;
        return
        [
            new Placed(new Label { Text = LimitsHeading }),
            .. _limits.SelectMany((limit, row) => new[]
            {
                new Placed(new Label { Text = limit.Column }, Indent, LimitsRow + row),
                new Placed(limit.Field, Indent + label, LimitsRow + row),
            }),
        ];
    }

    private IReadOnlyList<Placed> DashboardRows() =>
    [
        new Placed(_toolCalls),
        new Placed(new Label { Text = IconsHeading }, 0, IconsHeadingRow),
        new Placed(_iconStyles, Indent, IconStylesRow),
        new Placed(new Label { Text = IconLegend }, Indent, IconLegendRow),
    ];

    /// <summary>A style's row: its own glyphs after its name, so you pick the row that isn't boxes, and for
    /// Automatic what it decided for the terminal you're in, so the row answers rather than promises.</summary>
    private static string IconRow((IconStyle Style, string Name) choice, IconStyle auto)
    {
        var name = choice.Name.PadRight(IconChoices.Max(other => other.Name.Length));
        return choice.Style == IconStyle.Auto
            ? $"{name}  {ThisTerminal}{IconChoices.First(other => other.Style == auto).Name}"
            : $"{name}  {Icons.Sample(choice.Style)}";
    }

    /// <summary>A view on a page, at the row and indent the page wants it.</summary>
    private sealed record Placed(View View, int X = 0, int Y = 0);

    private sealed record Page(string Name, IReadOnlyList<Placed> Rows, Func<string[]> Hints, Func<KeyRow[]> Keys);

    /// <summary>A list that answers <c>p</c>, <c>x</c> and <c>n</c> itself: ListView's own type-ahead answers a letter
    /// before any handler the dialog could attach.</summary>
    private sealed class TeamList : ListView
    {
        public Action? Pause { get; set; }

        public Action? Drop { get; set; }

        public Action? New { get; set; }

        protected override bool OnKeyDown(Key key)
        {
            var action = key == new Key('p') ? Pause : key == new Key('x') ? Drop : key == new Key('n') ? New : null;
            if (action is null)
                return base.OnKeyDown(key);
            action();
            return true;
        }
    }
}
