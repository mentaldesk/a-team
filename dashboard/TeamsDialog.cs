using System.Collections.ObjectModel;
using System.Text.Json;
using MentalDesk.Tui.Dialogs;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>Your teams, a row each, and what you can do to them: each action happens straight away and says so.</summary>
public sealed class TeamsDialog : DashboardDialog
{
    internal const string PauseId = "teams.pause";
    internal const string EditId = "teams.edit";
    internal const string RemoveId = "teams.remove";
    internal const string NewId = "teams.new";
    internal const string GuideId = "teams.guide";
    private const string Working = "working";
    private const string Paused = "paused";
    private const string TodayHeading = "Today";
    private const string Unreadable = "can't read this file";
    private const int Inset = 1;
    private const int ListRow = 1;

    private readonly TeamConfigs _teams;
    private readonly TeamStart? _start;
    private readonly IconStyle _icons;
    private readonly List<TeamRow> _rows;
    private readonly TeamList _list = new();
    private readonly Label _header = new() { X = Inset, Y = 0 };
    private readonly MessageBar _message = new();
    private readonly List<string> _removed = [];
    private readonly TeamChecks? _checks;
    private readonly Func<string, Task<Reading>>? _readToday;
    private readonly Dictionary<string, TeamToday?> _today = [];
    private readonly Action<string> _showGuide;
    private bool _refreshing;

    public TeamsDialog(
        TeamConfigs teams,
        TeamStart? start = null,
        Func<string, Task<TeamHealth>>? check = null,
        Action<string>? showGuide = null,
        TeamChecks? checks = null,
        Func<string, Task<Reading>>? today = null,
        IconStyle icons = IconStyle.Unicode)
        : base("Teams")
    {
        _teams = teams;
        _start = start;
        _icons = icons;
        _showGuide = showGuide ?? (_ => { });
        check ??= start is null ? null : start.Check;
        _checks = checks ?? (check is null ? null : new TeamChecks(check, teams.Stamp));
        _readToday = today ?? (start is null ? null : start.Today);
        _rows = [.. teams.Names().Select(teams.Row)];
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill(1);

        RepairTeam = team => App is { } app && _start is { } begun &&
            StepDialog.Show(app, begun.Repair(team, _teams.Settings(team))) == Answer.Done;
        MakeMirror = team => App is { } app && _start is { } begun &&
            StepDialog.Show(app, begun.NewMirror(_teams, team)) == Answer.Done &&
            StepDialog.Show(app, begun.Repair(team, _teams.Settings(team))) == Answer.Done;
        EditTeam = (team, settings, save) =>
            App is { } app
                ? TeamForm.Show(app, team, settings, save, ListProjects, _checks?.Latest(team),
                    () => Repaired(team), _icons, _start is { } begun ? begun.Repos : null)
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

        int FootRow() => Math.Max(0, Viewport.Height - Foot.Rows - _message.Lines);
        _header.Width = Dim.Fill(Inset);
        _header.GettingAttributeForRole += (_, e) =>
        {
            var attribute = e.Result ?? GetAttributeForRole(e.Role);
            e.Result = attribute with { Foreground = attribute.Background, Background = attribute.Foreground };
            e.Handled = true;
        };
        _list.X = Inset;
        _list.Y = ListRow;
        _list.Width = Dim.Fill(Inset);
        _list.Height = Dim.Func(_ => Math.Max(1, FootRow() - ListRow - 1), this);
        _list.Pause = () => Commands.Execute(PauseId);
        _list.Drop = () => Commands.Execute(RemoveId);
        _list.New = () => Commands.Execute(NewId);
        _list.ValueChanged += (_, _) =>
        {
            if (!_refreshing)
                ShowTeam();
        };
        Foot.Y = Pos.Func(_ => FootRow(), this);
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);
        Add(_header, _list, Foot, _message);

        Bind(PauseId, Key.P, "Pause", TogglePause, () => SelectedTeam() is not null);
        Bind(EditId, Key.Enter, "Edit", () => OpenTeam(), () => SelectedTeam() is not null);
        Bind(RemoveId, Key.X, "Remove", RemoveTeam, () => SelectedTeam() is not null);
        Bind(NewId, Key.N, "New team", () => NewTeam());
        Bind(GuideId, Key.G.WithCtrl, "Guide", () => _showGuide(GuideDialog.Teams));
        Also("Up/Down", "Select a team");
        Register(CancelId, "Close", Close);
        AddButton(ButtonKind.Secondary, PauseId);
        AddButton(ButtonKind.Primary, EditId);
        AddButton(ButtonKind.Danger, RemoveId);
        AddButton(ButtonKind.Secondary, NewId);
        AddButton(ButtonKind.Secondary, CancelId);

        ShowTeams();
        CheckTeams();
        ReadToday();
        ShowTeam();
        _list.SetFocus();
    }

    internal ListView Teams => _list;

    internal string TeamHeader => _header.Text;

    internal MessageBar Message => _message;

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

    /// <summary>Makes the project a team's settings now ask for, and sets it up; true once both are done.</summary>
    internal Func<string, bool> MakeMirror { get; set; }

    /// <summary>The new team whose vision you chose to write now, which closes the dialog for the interview.</summary>
    internal string? VisionFor { get; private set; }

    internal IReadOnlyList<string> RemovedTeams => _removed;

    /// <summary>Runs the dialog and returns the teams removed in it and the new team whose vision to write now.</summary>
    public static (IReadOnlyList<string> Removed, string? Vision) Show(
        IApplication app,
        TeamConfigs teams,
        TeamStart? start,
        bool newTeam,
        Action<string>? showGuide,
        TeamChecks? checks,
        IconStyle icons)
    {
        using var dialog = new TeamsDialog(teams, start, showGuide: showGuide, checks: checks, icons: icons);
        if (newTeam)
            app.Invoke(() => dialog.NewTeam());
        DialogHost.Run(app, dialog);
        return (dialog.RemovedTeams, dialog.VisionFor);
    }

    private void Close() => RequestStop();

    /// <summary>Starts the selected team if it's paused and pauses it if it's working.</summary>
    private void TogglePause()
    {
        if (_list.Value is not { } index || SelectedTeam() is not { } team)
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
        _rows[index] = _teams.Row(team.Name);
        ShowTeams();
        ShowTeam();
        Say(team.Paused ? $"{team.Name} is working." : $"{team.Name} is paused.", Schemes.Accent);
    }

    /// <summary>Moves the selected team's file aside once you've said yes.</summary>
    private void RemoveTeam()
    {
        if (_list.Value is not { } index || SelectedTeam() is not { } team)
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
        _rows.RemoveAt(index);
        ShowTeams();
        ShowTeam();
        Say($"Removed {team.Name}. Rename {kept} back to {team.Name}.json to bring it back.", Schemes.Accent);
    }

    /// <summary>Opens the selected team's form, and saves what it's left holding straight into the team's file.</summary>
    internal bool OpenTeam()
    {
        if (_list.Value is not { } index || SelectedTeam() is not { } team)
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
            _rows[index] = _teams.Row(team.Name);
            ShowTeams();
            Say($"Couldn't open {team.Name}: {e.Message}");
            return true;
        }
        if (EditTeam(team.Name, before, after => SaveTeam(team.Name, before, after)) is not { } saved)
            return true;
        if (saved.NewMirror && !before.NewMirror)
            MakeMirror(team.Name);
        _rows[index] = _teams.Row(team.Name);
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
                    Close();
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
        _rows.Clear();
        _rows.AddRange(_teams.Names().Select(_teams.Row));
        _list.Value = null;
        CheckTeams();
        ShowTeams();
        if (_rows.Count > 0)
            _list.Value = Math.Max(0, _rows.FindIndex(row => row.Name == selected));
        _list.SetFocus();
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
        _list.Value is { } index && index >= 0 && index < _rows.Count ? _rows[index] : null;

    /// <summary>What's wrong with the selected team's file, if anything, and the buttons that fit it.</summary>
    private void ShowTeam()
    {
        if (SelectedTeam() is { Problem: { } problem } team)
            Say($"{team.Name}.json, {problem}");
        else
            _message.Clear();
        Rename(PauseId, SelectedTeam() is { Paused: true } ? "Resume" : "Pause");
        ShowButtons();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void ShowTeams()
    {
        var selected = _list.Value;
        var (header, rows) = TeamColumns();
        _header.Text = header;
        _refreshing = true;
        _list.SetSource(new ObservableCollection<string>(rows));
        _list.Value = _rows.Count == 0 ? null : Math.Min(selected ?? 0, _rows.Count - 1);
        _refreshing = false;
        if (_list.Value != selected)
            ShowTeam();
    }

    private (string Header, IReadOnlyList<string> Rows) TeamColumns()
    {
        List<string> headings = ["Team", "Repo", "Status"];
        if (_readToday is not null)
            headings.Add(TodayHeading);
        if (_checks is not null)
            headings.Add("Health");
        var cells = _rows.Select(team => (string[])[team.Name, team.Repo, Status(team),
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
        foreach (var team in _rows.Where(team => team.Problem is null))
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
        foreach (var team in _rows.Where(team => team.Problem is null))
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

    private void Say(string message, Schemes scheme = Schemes.Error)
    {
        _message.Show(message, scheme);
        SetNeedsLayout();
        SetNeedsDraw();
    }

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
