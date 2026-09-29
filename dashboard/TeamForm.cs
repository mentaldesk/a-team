using System.Collections.ObjectModel;
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
    internal const string RepoCaption = "The GitHub repo the team works on, as owner/repo.";
    internal const string StakeholdersCaption = "Whose comments the team acts on. You, unless you add others.";
    internal const string ProjectCaption = "The GitHub Project whose board the team moves its work across.";
    internal const string OwnerCaption = "Who owns the Project: the user or organisation in its URL.";
    internal const string NumberCaption = "The Project's number, the last part of its URL.";
    internal const string VisionCaption = "The Lead's yardstick, in the repo. Missing? It drafts one for you to approve.";
    internal const string WorkdirCaption = "Where the agents work. They can't write outside it.";
    internal const string SkillsCaption = "Skills the agents load. Must be installed on this machine.";
    internal const string TryCaption = "What a-team try runs to let you try a change.";
    internal const string CheckoutCaption = "Where the Dev looks for merged work to clean up. Change it in the file.";
    internal const string StatusCaption = "Whether the team picks up work. Paused lets a run in flight finish.";
    internal const string WorktreesCaption = "How many tasks the Dev builds at once.";
    internal const string PitchedCaption = "How many pitches wait on you at once.";
    internal const string ExploringCaption = "How many drafted pitches wait for room in Pitched.";
    internal const string IdeasCaption = "How many of the Lead's ideas wait for you to prioritise them.";
    internal const string ReadyFloorCaption = "Below this many Ready tasks, the Lead warns the Dev is running out of work.";
    private const string SaveHint = "save";
    private const string CancelHint = "cancel";
    private const int Inset = 1;
    private const int FieldX = 14;
    private const int LimitWidth = 6;
    private const string PickText = "Enter ▸";
    private const int PickWidth = 8;

    private readonly TeamSettings _before;
    private readonly string _team;
    private readonly Func<TeamSettings, string?> _save;
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
    private readonly OptionSelector<TeamStatus> _status;
    private readonly NumericUpDown<int> _worktrees;
    private readonly NumericUpDown<int> _pitched;
    private readonly NumericUpDown<int> _exploring;
    private readonly NumericUpDown<int> _ideas;
    private readonly NumericUpDown<int> _readyFloor;
    private readonly StatusBar _hints = new();
    private readonly MessageBar _message = new();
    private List<ProjectChoice> _projects = [];
    private IReadOnlyList<string> _stakeholderNames;
    private IReadOnlyList<string> _skillNames;

    /// <summary><paramref name="save"/> writes what the form holds, and says why it couldn't.</summary>
    public TeamForm(string team, TeamSettings settings, Func<TeamSettings, string?> save)
    {
        _before = settings;
        _save = save;
        _team = team;
        _stakeholderNames = settings.Stakeholders;
        _skillNames = settings.Skills;
        RunPicker = picker => App is { } app ? Picker.Show(app, picker) : null;
        FindStakeholders = Stakeholders.Read;
        FindSkills = () => SkillsFound.Find(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Current().CheckoutPath);

        Title = $"Team: {team}";
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        var row = 0;
        _repo = Field("Repo", settings.Repo, row++, RepoCaption);
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
        ShowProjects([]);
        row++;

        _vision = Field("Vision", settings.Vision, row++, VisionCaption);
        _workdir = Field("Workdir", settings.Workdir, row++, WorkdirCaption);
        _skills = Picks("Skills", _skillNames, row++, SkillsCaption, PickSkills);
        _try = Field("Try", settings.Try, row++, TryCaption);
        if (settings.OtherCheckout is { } checkout)
            Field("Checkout", checkout, row++, CheckoutCaption).ReadOnly = true;

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

        Add(new Label { Text = "Limits", X = Inset, Y = row });
        _worktrees = Limit("Worktrees", settings.Worktrees, 0, row, WorktreesCaption);
        _pitched = Limit("Pitched", settings.Pitched, 1, row, PitchedCaption);
        _exploring = Limit("Exploring", settings.Exploring, 2, row++, ExploringCaption);
        _ideas = Limit("Ideas", settings.Ideas, 0, row, IdeasCaption);
        _readyFloor = Limit("Ready floor", settings.ReadyFloor, 1, row, ReadyFloorCaption);

        _hints.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - 1 - _message.Lines), this);
        _hints.Show("", [new HintedCommand(SaveHint, "Enter save"), new HintedCommand(CancelHint, "Esc cancel")], Run);
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);
        Add(_hints, _message);
        _repo.SetFocus();
        Say(RepoCaption, Schemes.Base);
    }

    /// <summary>What the form was saved with, or null where it was cancelled.</summary>
    internal TeamSettings? Saved { get; private set; }

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

    internal OptionSelector<TeamStatus> Status => _status;

    internal NumericUpDown<int> Worktrees => _worktrees;

    internal StatusBar Hints => _hints;

    internal MessageBar Message => _message;

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
            Working = _status.Value == TeamStatus.Working,
            Worktrees = _worktrees.Value,
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
        _projects = [.. projects];
        var owner = _owner.Text.Trim();
        var chosen = int.TryParse(_number.Text.Trim(), out var number) && owner.Length > 0
            ? _projects.Find(project => project.Owner == owner && project.Number == number)
              ?? new ProjectChoice(owner, number, "")
            : null;
        if (chosen is not null && !_projects.Contains(chosen))
            _projects.Insert(0, chosen);
        _project.Source = new ListWrapper<string>(new ObservableCollection<string>(_projects.Select(project => project.ToString())));
        _project.Text = chosen?.ToString() ?? "";
    }

    /// <summary>Fetches the repo owner's projects and puts them in the list when they arrive.</summary>
    internal void LoadProjects(Func<string, Task<Reading>> list)
    {
        var owner = Current().RepoOwner is { Length: > 0 } repoOwner ? repoOwner : _before.ProjectOwner;
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
        return base.OnKeyDown(key);
    }

    /// <summary>Opens the form, and returns what it saved, or null where it was cancelled.</summary>
    public static TeamSettings? Show(
        IApplication app, string team, TeamSettings settings, Func<TeamSettings, string?> save, Func<string, Task<Reading>> projects)
    {
        using var form = new TeamForm(team, settings, save);
        form.LoadProjects(projects);
        app.Run(form);
        return form.Saved;
    }

    /// <summary>Saves and closes, or says why not and keeps what was typed.</summary>
    internal bool Save()
    {
        var now = Current();
        if ((now.Refusal() ?? _save(now)) is { } failure)
        {
            Say(failure, Schemes.Error);
            return true;
        }
        return Close(now);
    }

    private bool Close(TeamSettings? saved)
    {
        Saved = saved;
        RequestStop();
        return true;
    }

    /// <summary>Keeps the owner and number fields on the project picked, so they're what's saved either way.</summary>
    private void Pick()
    {
        if (_projects.Find(project => project.ToString() == _project.Text) is not { } picked)
            return;
        _owner.Text = picked.Owner;
        _number.Text = picked.Number.ToString();
    }

    /// <summary>Opens the stakeholders picker, filling it once GitHub says who can push to the repo.</summary>
    internal void PickStakeholders()
    {
        var repo = Current().Repo;
        using var picker = new Picker($"Stakeholders for {_team}", _stakeholderNames, "", $"Reading who can push to {repo}…");
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
        using var picker = new Picker($"Skills for {_team}", _skillNames, "(not installed)", found.Message);
        picker.ShowChoices(found.Names, found.Message);
        if (RunPicker(picker) is { } picked)
            _skillNames = Show(_skills, picked);
    }

    private bool Run(string hint) => hint == SaveHint ? Save() : Close(null);

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

    private NumericUpDown<int> Limit(string label, int value, int column, int row, string caption)
    {
        const int columnWidth = 22;
        var name = new Label { Text = label, X = FieldX + column * columnWidth, Y = row };
        var limit = new NumericUpDown<int> { Value = value, X = FieldX + column * columnWidth + 12, Y = row, Width = LimitWidth };
        limit.ValueChanging += (_, e) => e.Handled = e.NewValue < 0;
        Add(name, limit);
        Caption(limit, caption);
        return limit;
    }

    /// <summary>What a field changes, said as it takes focus.</summary>
    private void Caption(View field, string caption) =>
        field.HasFocusChanged += (_, _) =>
        {
            if (field.HasFocus)
                Say(caption, Schemes.Base);
        };

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
