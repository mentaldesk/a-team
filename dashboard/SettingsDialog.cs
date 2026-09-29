using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard;

/// <summary>The dashboard's settings, a page at a time: the pages on the left, the one picked on the right.</summary>
public sealed class SettingsDialog : Dialog
{
    internal const string TeamsPage = "Teams";
    private static readonly Key Apply = Key.Enter.WithCtrl;
    private const string Prompt = "Press a key…";
    private const string RebindHint = "Enter rebind";
    private const string RemoveHint = "Delete remove";
    private const string FilterHint = "Type to filter";
    private const string KeepHint = "Ctrl+Enter keep";
    private const string CancelHint = "Esc cancel";
    private const string PauseHint = "p pause";
    private const string ResumeHint = "p resume";
    private const string EditHint = "Enter edit";
    private const string Working = "working";
    private const string Paused = "paused";
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
    private const int KeysRow = 2;
    private const int PageHintsAbove = 2;
    private const double StripeTint = 0.1;
    private const int IconsHeadingRow = 2;
    private const int IconStylesRow = IconsHeadingRow + 1;

    private static readonly (IconStyle Style, string Name)[] IconChoices =
        [(IconStyle.Auto, "Automatic"), (IconStyle.NerdFont, "Nerd Font"), (IconStyle.Unicode, "Unicode")];

    private static readonly int IconLegendRow = IconStylesRow + IconChoices.Length + 1;

    private readonly CommandRegistry _commands;
    private readonly List<(string Id, string Label, Key Key)> _bindings;
    private readonly List<(string Id, Key Key)> _changed = [];
    private readonly List<int> _shown = [];
    private readonly int _labelWidth;
    private readonly List<Page> _pages;
    private readonly List<View> _hints = [];
    private readonly ListView _picker = new();
    private readonly CheckBox _toolCalls;
    private readonly OptionSelector _iconStyles;
    private readonly TextField _filter = new();
    private readonly KeyList _keys;
    private readonly TeamConfigs _teams;
    private readonly List<TeamRow> _teamRows;
    private readonly TeamList _teamList = new();
    private readonly MessageBar _message = new();
    private bool _capturing;

    public SettingsDialog(
        ThemeSetting theme,
        IconSetting icons,
        bool expandToolCalls,
        CommandRegistry commands,
        TeamConfigs teams,
        Action redraw,
        IconStyle auto,
        string? page = null)
    {
        _commands = commands;
        _teams = teams;
        _teamRows = [.. teams.Names().Select(teams.Row)];
        _bindings = [.. commands.Registered.Select(command => (command.Id, command.Label, command.Key))];
        _labelWidth = _bindings.Count == 0 ? 0 : _bindings.Max(binding => binding.Label.Length);

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
        _keys = new KeyList();
        _keys.VerticalScrollBar.VisibilityMode = ScrollBarVisibilityMode.Auto;
        _keys.Captured = OnKeysKey;
        _keys.RowRender += (_, e) => e.RowAttribute = Stripe(e.Row, _keys.Value, _keys.GetAttributeForRole(VisualRole.Normal));
        _filter.TextChanged += (_, _) =>
        {
            _keys.Value = null;
            ShowKeys();
        };
        _filter.KeyDown += (_, key) =>
        {
            if (key == Key.CursorDown)
                key.Handled = _keys.SetFocus();
        };

        _teamList.Pause = TogglePause;
        EditTeam = (team, settings, save) =>
            App is { } app ? TeamForm.Show(app, team, settings, save, ListProjects) : null;
        _teamList.ValueChanged += (_, _) => ShowTeam();

        _pages =
        [
            new Page("Theme", [new Placed(themes)], () => []),
            new Page("Keyboard Shortcuts", [new Placed(_filter), new Placed(_keys, 0, KeysRow)], () => [RebindHint, RemoveHint, FilterHint]),
            new Page("Dashboard", DashboardRows(), () => []),
            new Page(TeamsPage, [new Placed(_teamList)], () => [SelectedTeam() is { Paused: false } ? PauseHint : ResumeHint, EditHint]),
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
        _filter.Width = Dim.Fill(Inset);
        _keys.Width = Dim.Fill(Inset);
        _keys.Height = Dim.Func(_ => Math.Max(1, Viewport.Height - 1 - _message.Lines - PageHintsAbove - KeysRow), this);
        _teamList.Width = Dim.Fill(Inset);
        _teamList.Height = Dim.Func(_ => Math.Max(1, Viewport.Height - 1 - _message.Lines - PageHintsAbove), this);
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);

        Add(_picker, rule);
        foreach (var placed in _pages.SelectMany(page => page.Rows))
            Add(placed.View);
        Add(_message);
        ShowKeys();
        ShowTeams();
        ShowPage();
        if (page == TeamsPage)
            _teamList.SetFocus();
    }

    internal bool Confirmed { get; private set; }

    internal bool ExpandToolCalls => _toolCalls.Value == CheckState.Checked;

    internal ListView Pages => _picker;

    internal ListView Keys => _keys;

    internal ListView Teams => _teamList;

    internal MessageBar Message => _message;

    internal TextField Filter => _filter;

    internal IReadOnlyList<string> Rows => [.. _shown.Select(index => Row(_bindings[index]))];

    internal IReadOnlyList<(string Id, Key Key)> Changed => _changed;

    /// <summary>Opens the team form and returns what it saved, or null where it was cancelled.</summary>
    internal Func<string, TeamSettings, Func<TeamSettings, string?>, TeamSettings?> EditTeam { get; set; }

    /// <summary>Enter reaches a Dialog as Accept, from any of its lists alike, and never as a key.
    /// The hints close the dialog from their own Accepting, so nothing here does.</summary>
    protected override bool OnAccepting(CommandEventArgs args) =>
        _teamList.HasFocus ? OpenTeam() : !_keys.HasFocus || Rebind();

    protected override bool OnKeyDown(Key key)
    {
        if (key == Apply)
            return Close(confirmed: true);
        if (key == Key.Esc)
            return Close(confirmed: false);
        return base.OnKeyDown(key);
    }

    /// <summary>Runs the dialog, keeping what was picked in it only if it was accepted.</summary>
    public static void Show(
        IApplication app,
        DashboardSettings settings,
        CommandRegistry commands,
        Action<IconStyle> showIcons,
        IconStyle auto,
        TeamConfigs teams,
        string? page = null)
    {
        var theme = ThemeSetting.Live(settings);
        var icons = new IconSetting(settings.ReadIcons(), showIcons, settings.WriteIcons);
        using var dialog = new SettingsDialog(
            theme, icons, settings.ReadExpandToolCalls(), commands, teams, () => app.LayoutAndDraw(true), auto, page);
        app.Run(dialog);
        dialog.Store(theme, icons, settings);
    }

    /// <summary>Keeps what the dialog was left holding, or puts back what was in effect before it opened.</summary>
    internal void Store(ThemeSetting theme, IconSetting icons, DashboardSettings settings)
    {
        if (!Confirmed)
        {
            theme.Cancel();
            icons.Cancel();
            return;
        }
        theme.Keep();
        icons.Keep();
        settings.WriteExpandToolCalls(ExpandToolCalls);
        if (_changed.Count == 0)
            return;
        settings.WriteKeys(_changed);
        _commands.Apply(_changed);
    }

    /// <summary>Starts waiting for the key the selected command is to run on.</summary>
    internal bool Rebind()
    {
        if (Selected() is null)
            return true;
        _capturing = true;
        Say("");
        ShowKeys();
        return true;
    }

    internal bool Unbind()
    {
        if (Selected() is { } index)
            Bind(index, Key.Empty);
        return true;
    }

    /// <summary>Typing on the keys list edits the filter, so it narrows the list without leaving it.</summary>
    private bool OnKeysKey(Key key)
    {
        if (_capturing)
            return Capture(key);
        if (key == Key.Delete)
            return Unbind();
        if (key == Key.Backspace && _filter.Text.Length > 0)
        {
            _filter.Text = _filter.Text[..^1];
            return true;
        }
        if (key.IsCtrl || key.IsAlt || !key.TryGetPrintableRune(out var typed))
            return false;
        _filter.Text += typed.ToString();
        _filter.MoveEnd();
        return true;
    }

    private bool Capture(Key key)
    {
        _capturing = false;
        if (key == Key.Esc || Selected() is not { } index)
        {
            ShowKeys();
            return true;
        }

        var taken = _bindings.FindIndex(binding => binding.Key == key && binding.Id != _bindings[index].Id);
        if (taken >= 0)
        {
            Say($"{KeyNames.Short(key)} already runs {_bindings[taken].Label}.");
            ShowKeys();
            return true;
        }

        Bind(index, key);
        return true;
    }

    private void Bind(int index, Key key)
    {
        var (id, label, _) = _bindings[index];
        _bindings[index] = (id, label, key);
        var change = _changed.FindIndex(binding => binding.Id == id);
        if (change < 0)
            _changed.Add((id, key));
        else
            _changed[change] = (id, key);
        ShowKeys();
    }

    private int? Selected() =>
        _keys.Value is { } row && row >= 0 && row < _shown.Count ? _shown[row] : null;

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
        ShowTeams();
        ShowTeam();
        if (saved.Warning(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) is { } warning)
            Say(warning, Schemes.Accent);
        return true;
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
        _teamList.SetSource(new ObservableCollection<string>(TeamRows()));
        _teamList.Value = selected ?? (_teamRows.Count == 0 ? null : 0);
    }

    private IReadOnlyList<string> TeamRows()
    {
        var name = _teamRows.Count == 0 ? 0 : _teamRows.Max(team => team.Name.Length);
        var repo = _teamRows.Count == 0 ? 0 : _teamRows.Max(team => team.Repo.Length);
        return [.. _teamRows.Select(team => $"{team.Name.PadRight(name)}  {team.Repo.PadRight(repo)}  {Status(team)}")];
    }

    private static string Status(TeamRow team) =>
        team.Problem is not null ? Unreadable : team.Paused ? Paused : Working;

    /// <summary>Shows the page the list is on, and only that one, with the hints that page answers to.</summary>
    private void ShowPage()
    {
        var selected = _picker.Value ?? 0;
        for (var index = 0; index < _pages.Count; index++)
            foreach (var placed in _pages[index].Rows)
                placed.View.Visible = index == selected;
        ShowHints(_pages[selected].Hints());
        ShowTeam();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void ShowKeys()
    {
        var selected = _keys.Value;
        var filter = _filter.Text.Trim();
        _shown.Clear();
        _shown.AddRange(Enumerable.Range(0, _bindings.Count).Where(index => Matches(_bindings[index], filter)));
        var rows = Rows.ToList();
        if (_capturing && selected is { } row && row >= 0 && row < rows.Count)
            rows[row] = $"{_bindings[_shown[row]].Label.PadRight(_labelWidth)}  {Prompt}";
        _keys.SetSource(new ObservableCollection<string>(rows));
        _keys.Value = rows.Count == 0 ? null : Math.Clamp(selected ?? 0, 0, rows.Count - 1);
        _keys.EnsureSelectedItemVisible();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private static bool Matches((string Id, string Label, Key Key) binding, string filter) =>
        binding.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || KeyNames.Short(binding.Key).Contains(filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>Null leaves the row to the list, so the selected row keeps its highlight.</summary>
    internal static Attribute? Stripe(int row, int? selected, Attribute normal) =>
        row % 2 == 1 && row != selected ? normal with { Background = Blend(normal.Background, normal.Foreground) } : null;

    private static Color Blend(Color from, Color to) =>
        new(Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B));

    private static int Mix(byte from, byte to) => (int)Math.Round(from + (to - from) * StripeTint);

    private string Row((string Id, string Label, Key Key) binding) =>
        $"{binding.Label.PadRight(_labelWidth)}  {KeyNames.Short(binding.Key)}";

    private void Say(string message, Schemes scheme = Schemes.Error)
    {
        _message.Show(message, scheme);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private bool Close(bool confirmed)
    {
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
        _hints.AddRange(Hints(page, _keys.X, dialogRow - PageHintsAbove));
        _hints.AddRange(Hints([KeepHint, CancelHint], Inset, dialogRow));
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
        if (hint == EditHint)
        {
            _teamList.SetFocus();
            return OpenTeam();
        }
        switch (hint)
        {
            case RebindHint:
                _keys.SetFocus();
                return Rebind();
            case RemoveHint:
                _keys.SetFocus();
                return Unbind();
            case FilterHint:
                return _filter.SetFocus();
            default:
                return Close(confirmed: hint == KeepHint);
        }
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

    private sealed record Page(string Name, IReadOnlyList<Placed> Rows, Func<string[]> Hints);

    /// <summary>A list that can take a key literally. ListView's own type-ahead answers a letter before any
    /// handler the dialog could attach, so neither the capture nor the filter would ever see it.</summary>
    private sealed class KeyList : ListView
    {
        public Func<Key, bool>? Captured { get; set; }

        protected override bool OnKeyDown(Key key) => Captured?.Invoke(key) == true || base.OnKeyDown(key);
    }

    /// <summary>A list that answers <c>p</c> itself, for the same reason as <see cref="KeyList"/>.</summary>
    private sealed class TeamList : ListView
    {
        public Action? Pause { get; set; }

        protected override bool OnKeyDown(Key key)
        {
            if (key != new Key('p'))
                return base.OnKeyDown(key);
            Pause?.Invoke();
            return true;
        }
    }
}
