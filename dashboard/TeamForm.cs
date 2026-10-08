using System.Collections.ObjectModel;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>Whether a team picks up work, as the form's Status row offers it.</summary>
public enum TeamStatus
{
    Working,
    Paused,
}

/// <summary>One team's settings, to change and save without a text editor.</summary>
public sealed class TeamForm : Dialog
{
    internal const string NameCaption = "The team's name, which names its file.";
    internal const string RepoCaption = "The GitHub repo the team works on, as owner/repo.";
    internal const string StakeholdersCaption = "Whose comments the team acts on. You, unless you add others.";
    internal const string ProjectCaption = "The GitHub Project whose board the team moves its work across.";
    internal const string NewProject = "A new project, named after the team";
    internal const string OwnerCaption = "Who owns the Project: the user or organisation in its URL.";
    internal const string NumberCaption = "The Project's number, the last part of its URL.";
    internal const string VisionCaption = "The Lead's yardstick, in the repo. Missing? It drafts one for you to approve.";
    internal const string WorkdirCaption = "Where the agents work. They can't write outside it.";
    internal const string SkillsCaption = "Skills the agents load. Must be installed on this machine.";
    internal const string TryCaption = "What a-team try runs to let you try a change.";
    internal const string CheckoutCaption = "Where the Dev looks for merged work to clean up. Change it in the file.";
    internal const string StatusCaption = "Whether the team picks up work. Paused lets a run in flight finish.";
    internal const string ReleaseCaption = "When a-team runs the repo's release.yml. Never suits a repo that deploys itself on merge.";
    internal const string WorktreesCaption = "How many tasks can be in flight, PRs included.";
    internal const string DevsCaption = "How many Dev runs build at once.";
    internal const string PitchedCaption = "How many pitches wait on you at once.";
    internal const string ExploringCaption = "How many drafted pitches wait for room in Pitched.";
    internal const string IdeasCaption = "How many of the Lead's ideas wait for you to prioritise them.";
    internal const string ReadyFloorCaption = "Below this many Ready tasks, the Lead warns the Dev is running out of work.";
    private const string ProblemsHeading = "Problems";
    private const int BandPadding = 1;
    private const int ProblemsTop = BandPadding + 2;
    private const string NoName = "the new team";
    private const int Inset = 1;
    private const int FieldX = 14;
    private const int LimitWidth = 6;
    private const string PickText = "Enter ▸";
    private const int ButtonGap = 2;
    private static readonly Key RepairKey = Key.F12;
    private const int PickWidth = 8;

    private readonly TeamSettings _before;
    private readonly string? _team;
    private readonly Func<string, TeamSettings, string?> _save;
    private readonly IReadOnlyCollection<string> _taken;
    private readonly string _teamsDirectory;
    private readonly TextField? _name;
    private readonly TextField _repo;
    private readonly PickRow _stakeholders;
    private readonly PickRow _skills;
    private readonly DropDownList _project;
    private readonly TextField _owner;
    private readonly TextField _number;
    private readonly View _ownedBy;
    private readonly TextField _vision;
    private readonly TextField _workdir;
    private readonly TextField _try;
    private readonly OptionSelector<TeamStatus>? _status;
    private readonly OptionSelector<TeamRelease> _release;
    private readonly NumericUpDown<int> _worktrees;
    private readonly NumericUpDown<int> _devs;
    private readonly NumericUpDown<int> _pitched;
    private readonly NumericUpDown<int> _exploring;
    private readonly NumericUpDown<int> _ideas;
    private readonly NumericUpDown<int> _readyFloor;
    private readonly StatusBar _hints = new();
    private readonly View _buttons = new() { X = Pos.Center(), Width = Dim.Auto(), Height = 1, CanFocus = true };
    private readonly Button _repair = Button($"Repair ({RepairKey})");
    private readonly MessageBar _message = new();
    private readonly View _problemsBand = new() { SchemeName = LogSchemes.Warning, CanFocus = true, Visible = false };
    private readonly Label _problemsLabel = new() { Text = ProblemsHeading, X = Inset, Y = BandPadding };
    private readonly ListView _problems = new();
    private TeamHealth? _health;
    private Func<int, IReadOnlyList<string>> _problemRows = _ => [];
    private int _wrappedTo;
    private List<ProjectChoice> _projects = [];
    private IReadOnlyList<string> _stakeholderNames;
    private IReadOnlyList<string> _skillNames;
    private Func<string, Task<Reading>>? _listProjects;
    private string? _projectsOwner;

    /// <summary><paramref name="save"/> writes what the form holds, and says why it couldn't.</summary>
    public TeamForm(string team, TeamSettings settings, Func<TeamSettings, string?> save)
        : this(team, settings, (_, now) => save(now), [], "")
    {
    }

    /// <summary>A form for a team that isn't there yet: a Name field first, holding <paramref name="name"/>, and no
    /// Status, since it starts paused. <paramref name="create"/> writes the team named, and says why it couldn't.</summary>
    public static TeamForm New(
        TeamSettings settings, IReadOnlyCollection<string> taken, string teamsDirectory, Func<string, TeamSettings, string?> create,
        string name = "") =>
        new(null, settings, create, taken, teamsDirectory, name);

    private TeamForm(
        string? team, TeamSettings settings, Func<string, TeamSettings, string?> save, IReadOnlyCollection<string> taken, string teamsDirectory,
        string name = "")
    {
        _before = settings;
        _save = save;
        _team = team;
        _taken = taken;
        _teamsDirectory = teamsDirectory;
        _stakeholderNames = settings.Stakeholders;
        _skillNames = settings.Skills;
        RunPicker = picker => App is { } app ? Picker.Show(app, picker) : null;
        FindStakeholders = Stakeholders.Read;
        FindSkills = () => SkillsFound.Find(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Current().CheckoutPath);

        Title = team is null ? "New team" : $"Team: {team}";
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        var row = 0;
        if (team is null)
        {
            _name = Field("Name", name, row++, NameCaption);
            _name.HasFocusChanged += (_, _) =>
            {
                if (_name.HasFocus)
                    Hint(Becomes());
            };
            var named = name;
            _name.TextChanged += (_, _) =>
            {
                var name = _name.Text.Trim();
                if (_workdir is { } workdir && (workdir.Text.Length == 0 || workdir.Text == TeamSettings.WorkdirFor(named)))
                    workdir.Text = name.Length == 0 ? "" : TeamSettings.WorkdirFor(name);
                named = name;
                Hint(Becomes());
            };
        }
        _repo = Field("Repo", settings.Repo, row++, RepoCaption);
        _repo.HasFocusChanged += (_, _) =>
        {
            if (!_repo.HasFocus && _listProjects is { } list && Current().RepoOwner != _projectsOwner)
                LoadProjects(list);
        };
        _stakeholders = Picks("Stakeholders", _stakeholderNames, row++, StakeholdersCaption, PickStakeholders);

        Add(new Label { Text = "Project", X = Inset, Y = row });
        _project = new DropDownList { X = FieldX, Y = row, Width = Dim.Fill(Inset), ReadOnly = true };
        _ownedBy = new View { X = FieldX, Y = row, Width = Dim.Fill(Inset), Height = 1, CanFocus = true, Visible = false };
        var ownerLabel = new Label { Text = "Owner", X = 0, Y = 0 };
        _owner = new TextField { Text = settings.ProjectOwner, X = Pos.Right(ownerLabel) + 1, Y = 0, Width = 20 };
        var numberLabel = new Label { Text = "Number", X = Pos.Right(_owner) + 2, Y = 0 };
        _number = new TextField { Text = settings.ProjectNumber?.ToString() ?? "", X = Pos.Right(numberLabel) + 1, Y = 0, Width = 8 };
        _ownedBy.Add(ownerLabel, _owner, numberLabel, _number);
        Add(_project, _ownedBy);
        Caption(_project, ProjectCaption);
        Caption(_owner, OwnerCaption);
        Caption(_number, NumberCaption);
        _project.TextChanged += (_, _) => Pick();
        _project.HasFocusChanged += (_, _) =>
        {
            if (_project.HasFocus && _listProjects is { } list)
                LoadProjects(list);
        };
        ShowProjects([]);
        row++;

        _vision = Field("Vision", settings.Vision, row++, VisionCaption);
        _workdir = Field("Workdir", settings.Workdir, row++, WorkdirCaption);
        _skills = Picks("Skills", _skillNames, row++, SkillsCaption, PickSkills);
        _try = Field("Try", settings.Try, row++, TryCaption);
        if (settings.OtherCheckout is { } checkout)
            Field("Checkout", checkout, row++, CheckoutCaption).ReadOnly = true;

        if (team is not null)
        {
            Add(new Label { Text = "Status", X = Inset, Y = row });
            _status = new OptionSelector<TeamStatus>
            {
                X = FieldX,
                Y = row++,
                Orientation = Orientation.Horizontal,
                // NoStop is what makes the arrows move between the options; with the default they're Tab stops.
                TabBehavior = TabBehavior.NoStop,
                Value = settings.Working ? TeamStatus.Working : TeamStatus.Paused,
            };
            Add(_status);
            Caption(_status, StatusCaption);
        }

        Add(new Label { Text = "Releases", X = Inset, Y = row });
        _release = new OptionSelector<TeamRelease>
        {
            X = FieldX,
            Y = row++,
            Orientation = Orientation.Horizontal,
            TabBehavior = TabBehavior.NoStop,
            Value = settings.Release,
        };
        Add(_release);
        Caption(_release, ReleaseCaption);

        Add(new Label { Text = "Limits", X = Inset, Y = row });
        _worktrees = Limit("Worktrees", settings.Worktrees, 0, row, WorktreesCaption);
        _pitched = Limit("Pitched", settings.Pitched, 1, row, PitchedCaption);
        _exploring = Limit("Exploring", settings.Exploring, 2, row++, ExploringCaption);
        _devs = Limit("Devs", settings.Devs, 0, row, DevsCaption, least: 1);
        _ideas = Limit("Ideas", settings.Ideas, 1, row, IdeasCaption);
        _readyFloor = Limit("Ready floor", settings.ReadyFloor, 2, row, ReadyFloorCaption);
        row += 2;

        _problemsBand.X = 0;
        _problemsBand.Y = row;
        _problemsBand.Width = Dim.Fill();
        _problemsBand.Height = Dim.Func(_ => Math.Max(ProblemsTop + 1 + BandPadding,
            Math.Min((_problems.Source?.Count ?? 0) + ProblemsTop + BandPadding, Viewport.Height - 2 - _message.Lines - row)), this);
        _problems.X = Inset;
        _problems.Y = ProblemsTop;
        _problems.Width = Dim.Fill(Inset);
        _problems.Height = Dim.Fill(BandPadding);
        _problems.ViewportChanged += (_, _) =>
        {
            if (_problems.Viewport.Width != _wrappedTo)
                LayProblems();
        };
        _problemsBand.Add(_problemsLabel, _problems);
        Add(_problemsBand);

        var saveButton = Button(team is null ? "Create" : "Save");
        var cancelButton = Button("Cancel");
        _repair.Visible = false;
        _repair.Accepting += (_, args) => args.Handled = Repair();
        saveButton.Accepting += (_, args) => args.Handled = Save();
        cancelButton.Accepting += (_, args) => args.Handled = Close(null);
        saveButton.X = Pos.Right(_repair) + ButtonGap;
        cancelButton.X = Pos.Right(saveButton) + ButtonGap;
        _buttons.Add(_repair, saveButton, cancelButton);
        _buttons.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - 2 - _message.Lines), this);
        _hints.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - 1 - _message.Lines), this);
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);
        Add(_buttons, _hints, _message);
        if (_name is { } first)
        {
            first.SetFocus();
            Hint(Becomes());
        }
        else
        {
            _repo.SetFocus();
            Hint(RepoCaption);
        }
    }

    /// <summary>What the form was saved with, or null where it was cancelled.</summary>
    internal TeamSettings? Saved { get; private set; }

    /// <summary>The name a new team was created under.</summary>
    internal string? SavedName { get; private set; }

    internal TextField? NameField => _name;

    internal TextField Repo => _repo;

    internal View StakeholdersRow => _stakeholders;

    internal View SkillsRow => _skills;

    /// <summary>Runs a picker over the form and returns what it picked, or null where it was cancelled.</summary>
    internal Func<Picker, IReadOnlyList<string>?> RunPicker { get; set; }

    /// <summary>Who can push to a repo, or null where GitHub can't say.</summary>
    internal Func<string, Task<IReadOnlyList<string>?>> FindStakeholders { get; set; }

    internal Func<SkillsFound> FindSkills { get; set; }

    internal DropDownList Project => _project;

    internal TextField Owner => _owner;

    internal TextField Number => _number;

    internal TextField Vision => _vision;

    internal TextField Workdir => _workdir;

    internal TextField Try => _try;

    internal OptionSelector<TeamStatus>? Status => _status;

    internal OptionSelector<TeamRelease> Releases => _release;

    internal NumericUpDown<int> Worktrees => _worktrees;

    internal NumericUpDown<int> Devs => _devs;

    internal StatusBar Hints => _hints;

    internal MessageBar Message => _message;

    /// <summary>The buttons along the foot of the form, as shown now.</summary>
    internal IReadOnlyList<Button> FootButtons => [.. _buttons.SubViews.OfType<Button>().Where(button => button.Visible)];

    internal ListView Problems => _problems;

    internal string ProblemsTitle => _problemsLabel.Text;

    /// <summary>The icons the Problems heading is drawn in.</summary>
    internal IconStyle IconStyle
    {
        init => _problemsLabel.Text = Icons.Field(Icon.Warning, value) + ProblemsHeading;
    }

    /// <summary>Sets the team's board up again where it can be, and returns the check that follows it, or null where
    /// nothing was done.</summary>
    internal Func<Task<TeamHealth>?>? RepairBoard { get; set; }

    /// <summary>Shows what's wrong with the team once <paramref name="health"/> says, and <c>checking…</c> until then.</summary>
    internal void Watch(Task<TeamHealth> health)
    {
        if (health.IsCompleted)
        {
            ShowHealth(Health(health));
            return;
        }
        ShowProblems(_ => [TeamHealth.Checking]);
        health.ContinueWith(done =>
        {
            if (App is { } app)
                app.Invoke(() => ShowHealth(Health(done)));
            else
                ShowHealth(Health(done));
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>Runs <see cref="RepairBoard"/> where the problems are ones it can put right.</summary>
    internal bool Repair()
    {
        if (_health is not { CanRepair: true } || RepairBoard?.Invoke() is not { } after)
            return true;
        Watch(after);
        return true;
    }

    private static TeamHealth Health(Task<TeamHealth> done) =>
        done.Status == TaskStatus.RanToCompletion
            ? done.Result
            : new TeamHealth([new TeamProblem("check", done.Exception?.InnerException?.Message ?? "it didn't finish")]);

    private void ShowHealth(TeamHealth health)
    {
        _health = health;
        ShowProblems(width => [.. health.Problems.SelectMany(problem => problem.Rows(width))]);
    }

    private void ShowProblems(Func<int, IReadOnlyList<string>> rows)
    {
        _problemRows = rows;
        LayProblems();
        ShowRepair();
    }

    private void LayProblems()
    {
        _wrappedTo = _problems.Viewport.Width;
        var rows = _problemRows(_wrappedTo);
        _problems.SetSource(new ObservableCollection<string>(rows));
        _problemsBand.Visible = _problems.Visible = rows.Count > 0;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void ShowRepair()
    {
        _repair.Visible = _health is { CanRepair: true } && RepairBoard is not null;
        _buttons.SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>The settings as the form holds them now.</summary>
    internal TeamSettings Current() =>
        _before with
        {
            Repo = _repo.Text.Trim(),
            ProjectOwner = _owner.Text.Trim(),
            ProjectNumber = int.TryParse(_number.Text.Trim(), out var number) ? number : null,
            Vision = _vision.Text.Trim(),
            Workdir = _workdir.Text.Trim(),
            Try = _try.Text.Trim(),
            Stakeholders = _stakeholderNames,
            Skills = _skillNames,
            Working = _status is { } status ? status.Value == TeamStatus.Working : _before.Working,
            Release = _release.Value ?? _before.Release,
            Worktrees = _worktrees.Value,
            Devs = _devs.Value,
            Pitched = _pitched.Value,
            Exploring = _exploring.Value,
            Ideas = _ideas.Value,
            ReadyFloor = _readyFloor.Value,
        };

    /// <summary>The owner's projects as a list to pick from, keeping the team's own where the list hasn't got it;
    /// null, when they can't be read, puts the owner and number fields in its place.</summary>
    internal void ShowProjects(IReadOnlyList<ProjectChoice>? projects)
    {
        if (projects is null)
        {
            var focused = _project.HasFocus;
            _project.Visible = false;
            _ownedBy.Visible = true;
            if (focused)
                _owner.SetFocus();
            return;
        }
        _project.Visible = true;
        _ownedBy.Visible = false;
        _projects = [.. projects];
        var owner = _owner.Text.Trim();
        var chosen = int.TryParse(_number.Text.Trim(), out var number) && owner.Length > 0
            ? _projects.Find(project => project.Owner == owner && project.Number == number)
              ?? new ProjectChoice(owner, number, "")
            : null;
        if (chosen is not null && !_projects.Contains(chosen))
            _projects.Insert(0, chosen);
        IEnumerable<string> choices = _projects.Select(project => project.ToString());
        if (_team is null)
            choices = choices.Prepend(NewProject);
        _project.Source = new ListWrapper<string>(new ObservableCollection<string>(choices));
        _project.Text = chosen?.ToString() ?? (_team is null ? NewProject : "");
    }

    /// <summary>Fetches the repo owner's projects and puts them in the list when they arrive, again each time the list
    /// takes focus so a project made meanwhile shows up.</summary>
    internal void LoadProjects(Func<string, Task<Reading>> list)
    {
        _listProjects = list;
        var owner = Current().RepoOwner is { Length: > 0 } repoOwner ? repoOwner : _before.ProjectOwner;
        _projectsOwner = Current().RepoOwner;
        if (owner.Length == 0)
            return;
        list(owner).ContinueWith(read =>
        {
            var projects = read.Status == TaskStatus.RanToCompletion ? ProjectChoice.Parse(read.Result, owner) : null;
            App?.Invoke(() => ShowProjects(projects));
        }, TaskScheduler.Default);
    }

    /// <summary>Enter reaches a Dialog as Accept, from any field alike, and never as a key. The project list's own
    /// Enter picks a project rather than saving.</summary>
    protected override bool OnAccepting(CommandEventArgs args) =>
        args.Context?.Source?.TryGetTarget(out var from) == true && Picking(from) || Save();

    private bool Picking(View from)
    {
        for (var at = from; at is not null; at = at.SuperView)
            if (at == this)
                return false;
        return true;
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
            return Close(null);
        if (key == RepairKey)
            return Repair();
        return base.OnKeyDown(key);
    }

    /// <summary>Opens the form, and returns what it saved, or null where it was cancelled.</summary>
    public static TeamSettings? Show(
        IApplication app, string team, TeamSettings settings, Func<TeamSettings, string?> save, Func<string, Task<Reading>> projects,
        Task<TeamHealth>? health = null, Func<Task<TeamHealth>?>? repair = null, IconStyle icons = IconStyle.Unicode)
    {
        using var form = new TeamForm(team, settings, save) { RepairBoard = repair, IconStyle = icons };
        if (health is not null)
            form.Watch(health);
        return Run(app, form, projects) ? form.Saved : null;
    }

    /// <summary>Opens the form for a new team, and returns the name it was created under, or null where it was
    /// cancelled.</summary>
    public static string? ShowNew(
        IApplication app, TeamSettings settings, IReadOnlyCollection<string> taken, string teamsDirectory,
        Func<string, TeamSettings, string?> create, Func<string, Task<Reading>> projects, Task<string> me, string name = "")
    {
        using var form = New(settings, taken, teamsDirectory, create, name);
        me.ContinueWith(read =>
        {
            if (read.Status == TaskStatus.RanToCompletion)
                form.App?.Invoke(() => form.ShowMe(read.Result));
        }, TaskScheduler.Default);
        return Run(app, form, projects) ? form.SavedName : null;
    }

    private static bool Run(IApplication app, TeamForm form, Func<string, Task<Reading>> projects)
    {
        form.LoadProjects(projects);
        app.Run(form);
        return form.Saved is not null;
    }

    /// <summary>Saves and closes, or says why not and keeps what was typed.</summary>
    internal bool Save()
    {
        var now = Current();
        var name = _team ?? _name?.Text.Trim() ?? "";
        var refusal = _team is null ? TeamSettings.NameRefusal(name, _taken) : null;
        if ((refusal ?? now.Refusal(projectOptional: _team is null) ?? _save(name, now)) is { } failure)
        {
            Say(failure, Schemes.Error);
            return true;
        }
        SavedName = name;
        return Close(now);
    }

    private string Becomes() =>
        _name?.Text.Trim() is { Length: > 0 } name ? $"Becomes {_teamsDirectory}/{name}.json" : NameCaption;

    private string TeamName => _team ?? (_name?.Text.Trim() is { Length: > 0 } name ? name : NoName);

    private bool Close(TeamSettings? saved)
    {
        Saved = saved;
        RequestStop();
        return true;
    }

    /// <summary>Keeps the owner and number fields on the project picked, so they're what's saved either way.</summary>
    private void Pick()
    {
        if (_team is null && _project.Text == NewProject)
        {
            _owner.Text = "";
            _number.Text = "";
            return;
        }
        if (_projects.Find(project => project.ToString() == _project.Text) is not { } picked)
            return;
        _owner.Text = picked.Owner;
        _number.Text = picked.Number.ToString();
    }

    /// <summary>Puts who's signed in as the stakeholder, unless some have been picked already.</summary>
    internal void ShowMe(string me)
    {
        if (me.Length > 0 && _stakeholderNames.Count == 0)
            _stakeholderNames = Show(_stakeholders, [me]);
    }

    /// <summary>Opens the stakeholders picker, filling it once GitHub says who can push to the repo.</summary>
    internal void PickStakeholders()
    {
        var repo = Current().Repo;
        using var picker = new Picker($"Stakeholders for {TeamName}", _stakeholderNames, "", $"Reading who can push to {repo}…");
        FindStakeholders(repo).ContinueWith(read =>
        {
            var found = read.Status == TaskStatus.RanToCompletion ? read.Result : null;
            void Show() => picker.ShowChoices(found ?? [], Stakeholders.Message(found, repo), found is null ? Schemes.Accent : Schemes.Base);
            if (App is { } app)
                app.Invoke(Show);
            else
                Show();
        }, TaskContinuationOptions.ExecuteSynchronously);
        if (RunPicker(picker) is { } picked)
            _stakeholderNames = Show(_stakeholders, picked);
    }

    /// <summary>Opens the skills picker over the skills installed here and the repo's own.</summary>
    internal void PickSkills()
    {
        var found = FindSkills();
        using var picker = new Picker($"Skills for {TeamName}", _skillNames, "(not installed)", found.Message);
        picker.ShowChoices(found.Names, found.Message);
        if (RunPicker(picker) is { } picked)
            _skillNames = Show(_skills, picked);
    }

    private TextField Field(string label, string value, int row, string caption)
    {
        Add(new Label { Text = label, X = Inset, Y = row });
        var field = new TextField { Text = value, X = FieldX, Y = row, Width = Dim.Fill(Inset) };
        Add(field);
        Caption(field, caption);
        return field;
    }

    private PickRow Picks(string label, IReadOnlyList<string> names, int row, string caption, Action pick)
    {
        Add(new Label { Text = label, X = Inset, Y = row });
        var picks = new PickRow(pick) { X = FieldX + 1, Y = row, Width = Dim.Fill(Inset + PickWidth) };
        Add(picks, new Label { Text = PickText, X = Pos.AnchorEnd(Inset + PickWidth), Y = row });
        Show(picks, names);
        Caption(picks, caption);
        return picks;
    }

    private static IReadOnlyList<string> Show(PickRow row, IReadOnlyList<string> names)
    {
        row.Text = names.Count == 0 ? "none" : string.Join(", ", names);
        return names;
    }

    private NumericUpDown<int> Limit(string label, int value, int column, int row, string caption, int least = 0)
    {
        const int columnWidth = 22;
        var name = new Label { Text = label, X = FieldX + column * columnWidth, Y = row };
        var limit = new NumericUpDown<int> { Value = value, X = FieldX + column * columnWidth + 12, Y = row, Width = LimitWidth };
        limit.ValueChanging += (_, e) => e.Handled = e.NewValue < least;
        Add(name, limit);
        Caption(limit, caption);
        return limit;
    }

    /// <summary>What a field changes, said in the status bar as it takes focus.</summary>
    private void Caption(View field, string caption) =>
        field.HasFocusChanged += (_, _) =>
        {
            if (field.HasFocus)
                Hint(caption);
        };

    private void Hint(string caption)
    {
        _hints.Say(caption, Schemes.Base);
        _message.Clear();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private static Button Button(string text) => new() { Text = text, Y = 0, HotKeySpecifier = (Rune)0xffff };

    private void Say(string message, Schemes scheme)
    {
        _message.Show(message, scheme);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>The chosen names of a list field, which opens its picker on Enter.</summary>
    private sealed class PickRow : Label
    {
        private readonly Action _pick;

        public PickRow(Action pick)
        {
            _pick = pick;
            CanFocus = true;
        }

        protected override bool OnKeyDown(Key key)
        {
            if (key != Key.Enter)
                return base.OnKeyDown(key);
            _pick();
            return true;
        }
    }
}
