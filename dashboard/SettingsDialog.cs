using System.Collections.ObjectModel;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>The dashboard's settings, a page at a time: the pages on the left, the one picked on the right.</summary>
public sealed class SettingsDialog : Dialog
{
    private static readonly Key Apply = Key.Enter.WithCtrl;
    private const string Prompt = "Press a key…";
    private const string RebindHint = "Enter rebind";
    private const string KeepHint = "Ctrl+Enter keep";
    private const string CancelHint = "Esc cancel";
    private const string Separator = " · ";
    private const string ToolCalls = "Show tool calls in full";
    private const string IconsHeading = "Icons:";
    private const string IconLegend = "run  idle  paused  ok  error  here  tool";
    private const int Inset = 1;
    private const int Gap = 1;
    private const int GlyphAndSpace = 2;
    private const int Indent = 2;
    private const int IconsHeadingRow = 2;
    private const int IconStylesRow = IconsHeadingRow + 1;

    private static readonly (IconStyle Style, string Name)[] IconChoices =
        [(IconStyle.Auto, "Automatic"), (IconStyle.NerdFont, "Nerd Font"), (IconStyle.Unicode, "Unicode")];

    private static readonly int IconLegendRow = IconStylesRow + IconChoices.Length + 1;
    private static readonly int DashboardTall = IconLegendRow + 1;

    private readonly CommandRegistry _commands;
    private readonly List<(string Id, string Label, Key Key)> _bindings;
    private readonly List<(string Id, Key Key)> _changed = [];
    private readonly int _labelWidth;
    private readonly List<Page> _pages;
    private readonly List<View> _hints = [];
    private readonly ListView _picker = new();
    private readonly CheckBox _toolCalls;
    private readonly OptionSelector _iconStyles;
    private readonly KeyList _keys;
    private readonly MessageBar _message = new();
    private bool _capturing;

    public SettingsDialog(
        ThemeSetting theme, IconSetting icons, bool expandToolCalls, CommandRegistry commands, Action redraw)
    {
        _commands = commands;
        _bindings = [.. commands.Registered.Select(command => (command.Id, command.Label, command.Key))];
        _labelWidth = _bindings.Count == 0 ? 0 : _bindings.Max(binding => binding.Label.Length);

        Title = "Settings";
        Width = Dim.Func(_ => Fits(Wide() + GetAdornmentsThickness().Horizontal, SuperView?.Viewport.Width), this);
        Height = Dim.Func(_ => Fits(Tall() + GetAdornmentsThickness().Vertical, SuperView?.Viewport.Height), this);

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
            Labels = [.. IconChoices.Select(IconRow)],
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
        _keys.Captured = key => _capturing && Capture(key);

        _pages =
        [
            new Page("Theme", [new Placed(themes)], () => BundledThemes.Names.Max(name => name.Length) + GlyphAndSpace,
                BundledThemes.Names.Count, [KeepHint, CancelHint]),
            new Page("Keyboard Shortcuts", [new Placed(_keys)], KeysWide, Math.Max(1, _bindings.Count),
                [RebindHint, KeepHint, CancelHint]),
            new Page("Dashboard", DashboardRows(), DashboardWide, DashboardTall, [KeepHint, CancelHint]),
        ];

        var content = Dim.Func(_ => Math.Max(1, Viewport.Height - 1 - _message.Lines), this);
        _picker.X = Inset;
        _picker.Y = 0;
        _picker.Width = _pages.Max(page => page.Name.Length);
        _picker.Height = content;
        _picker.SetSource(new ObservableCollection<string>(_pages.Select(page => page.Name)));
        _picker.Value = 0;
        _picker.ValueChanged += (_, _) => ShowPage();

        var rule = new Line { X = Pos.Right(_picker) + Gap, Y = 0, Orientation = Orientation.Vertical, Height = content };
        foreach (var placed in _pages.SelectMany(page => page.Rows))
        {
            placed.View.X = Pos.Right(rule) + Gap + placed.X;
            placed.View.Y = placed.Y;
        }
        _keys.Width = Dim.Fill(Inset);
        _keys.Height = content;
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);

        Add(_picker, rule);
        foreach (var placed in _pages.SelectMany(page => page.Rows))
            Add(placed.View);
        Add(_message);
        ShowKeys();
        ShowPage();
    }

    internal bool Confirmed { get; private set; }

    internal bool ExpandToolCalls => _toolCalls.Value == CheckState.Checked;

    internal ListView Pages => _picker;

    internal ListView Keys => _keys;

    internal MessageBar Message => _message;

    internal IReadOnlyList<string> Rows => [.. _bindings.Select(Row)];

    internal IReadOnlyList<(string Id, Key Key)> Changed => _changed;

    /// <summary>Enter reaches a Dialog as Accept, from the keys list or the pages list alike, and never as a key.
    /// The hints close the dialog from their own Accepting, so nothing here does.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => !_keys.HasFocus || Rebind();

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
        IApplication app, DashboardSettings settings, CommandRegistry commands, Action<IconStyle> showIcons)
    {
        var theme = ThemeSetting.Live(settings);
        var icons = new IconSetting(settings.ReadIcons(), showIcons, settings.WriteIcons);
        using var dialog = new SettingsDialog(
            theme, icons, settings.ReadExpandToolCalls(), commands, () => app.LayoutAndDraw(true));
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

        var (id, label, _) = _bindings[index];
        _bindings[index] = (id, label, key);
        var change = _changed.FindIndex(binding => binding.Id == id);
        if (change < 0)
            _changed.Add((id, key));
        else
            _changed[change] = (id, key);
        ShowKeys();
        return true;
    }

    private int? Selected() =>
        _keys.Value is { } index && index >= 0 && index < _bindings.Count ? index : null;

    /// <summary>Shows the page the list is on, and only that one, with the hints that page answers to.</summary>
    private void ShowPage()
    {
        var selected = _picker.Value ?? 0;
        for (var index = 0; index < _pages.Count; index++)
            foreach (var placed in _pages[index].Rows)
                placed.View.Visible = index == selected;
        ShowHints(_pages[selected].Hints);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void ShowKeys()
    {
        var selected = _keys.Value;
        var rows = Rows.ToList();
        if (_capturing && selected is { } index && index >= 0 && index < rows.Count)
            rows[index] = $"{_bindings[index].Label.PadRight(_labelWidth)}  {Prompt}";
        _keys.SetSource(new ObservableCollection<string>(rows));
        _keys.Value = selected ?? (_bindings.Count == 0 ? null : 0);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private string Row((string Id, string Label, Key Key) binding) =>
        $"{binding.Label.PadRight(_labelWidth)}  {KeyNames.Short(binding.Key)}";

    private void Say(string message)
    {
        _message.Show(message, Schemes.Error);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private bool Close(bool confirmed)
    {
        Confirmed = confirmed;
        RequestStop();
        return true;
    }

    private void ShowHints(IReadOnlyList<string> texts)
    {
        foreach (var hint in _hints)
        {
            Remove(hint);
            hint.Dispose();
        }
        _hints.Clear();
        _hints.AddRange(Hints(texts));
        foreach (var hint in _hints)
            Add(hint);
    }

    /// <summary>The hint row, each hint clickable and the separators between them not.</summary>
    private View[] Hints(IReadOnlyList<string> texts)
    {
        var y = Pos.Func(_ => Math.Max(0, Viewport.Height - 1 - _message.Lines), this);
        List<View> row = [];
        Pos x = Inset;
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
        if (hint != RebindHint)
            return Close(confirmed: hint == KeepHint);
        _keys.SetFocus();
        return Rebind();
    }

    private IReadOnlyList<Placed> DashboardRows() =>
    [
        new Placed(_toolCalls),
        new Placed(new Label { Text = IconsHeading }, 0, IconsHeadingRow),
        new Placed(_iconStyles, Indent, IconStylesRow),
        new Placed(new Label { Text = IconLegend }, Indent, IconLegendRow),
    ];

    /// <summary>A style's row, its own glyphs after its name, so you pick the row that isn't boxes.</summary>
    private static string IconRow((IconStyle Style, string Name) choice) => choice.Style == IconStyle.Auto
        ? choice.Name
        : $"{choice.Name.PadRight(IconChoices.Max(other => other.Name.Length))}  {Icons.Sample(choice.Style)}";

    private static int DashboardWide() => Math.Max(
        Math.Max(ToolCalls.Length + GlyphAndSpace, IconsHeading.Length),
        Indent + Math.Max(
            IconChoices.Max(choice => IconRow(choice).GetColumns()) + GlyphAndSpace, IconLegend.Length));

    private int KeysWide() => Math.Max(
        _bindings.Count == 0 ? 0 : Rows.Max(row => row.Length),
        _labelWidth + 2 + Prompt.Length);

    private static int HintWidth(IReadOnlyList<string> texts) =>
        texts.Sum(text => text.Length) + (Separator.Length * (texts.Count - 1));

    private int Wide() => Math.Max(
        _pages.Max(page => page.Name.Length) + Gap + 1 + Gap + _pages.Max(page => page.Width()),
        _pages.Max(page => HintWidth(page.Hints))) + (Inset * 2);

    private int Tall() =>
        Math.Max(_pages.Count, _pages.Max(page => page.Height)) + 1 + _message.Lines;

    private static int Fits(int wanted, int? available) => available is { } room ? Math.Min(wanted, room) : wanted;

    /// <summary>A view on a page, at the row and indent the page wants it.</summary>
    private sealed record Placed(View View, int X = 0, int Y = 0);

    private sealed record Page(string Name, IReadOnlyList<Placed> Rows, Func<int> Width, int Height, string[] Hints);

    /// <summary>A list that can take a key literally. ListView's own type-ahead answers a letter before any
    /// handler the dialog could attach, so the letter being bound would never reach the capture.</summary>
    private sealed class KeyList : ListView
    {
        public Func<Key, bool>? Captured { get; set; }

        protected override bool OnKeyDown(Key key) => Captured?.Invoke(key) == true || base.OnKeyDown(key);
    }
}
