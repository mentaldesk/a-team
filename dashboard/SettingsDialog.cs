using System.Collections.ObjectModel;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
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
    private const string NerdFont = "Nerd Font icons on the cards";
    private const int Inset = 1;
    private const int Gap = 1;
    private const int GlyphAndSpace = 2;

    private readonly CommandRegistry _commands;
    private readonly List<(string Id, string Label, Key Key)> _bindings;
    private readonly List<(string Id, Key Key)> _changed = [];
    private readonly int _labelWidth;
    private readonly List<Page> _pages;
    private readonly List<View> _hints = [];
    private readonly ListView _picker = new();
    private readonly CheckBox _toolCalls;
    private readonly CheckBox _nerdFont;
    private readonly KeyList _keys;
    private readonly MessageBar _message = new();
    private bool _capturing;

    public SettingsDialog(
        ThemeSetting theme, bool expandToolCalls, bool nerdFont, CommandRegistry commands, Action redraw)
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
        _nerdFont = new CheckBox
        {
            Text = NerdFont,
            Value = nerdFont ? CheckState.Checked : CheckState.UnChecked,
        };
        _keys = new KeyList();
        _keys.Captured = key => _capturing && Capture(key);

        _pages =
        [
            new Page("Theme", [themes], () => BundledThemes.Names.Max(name => name.Length) + GlyphAndSpace,
                BundledThemes.Names.Count, [KeepHint, CancelHint]),
            new Page("Keyboard Shortcuts", [_keys], KeysWide, Math.Max(1, _bindings.Count),
                [RebindHint, KeepHint, CancelHint]),
            new Page("Dashboard", [_toolCalls, _nerdFont],
                () => Math.Max(ToolCalls.Length, NerdFont.Length) + GlyphAndSpace, 2, [KeepHint, CancelHint]),
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
        foreach (var page in _pages)
            for (var row = 0; row < page.Views.Count; row++)
            {
                page.Views[row].X = Pos.Right(rule) + Gap;
                page.Views[row].Y = row;
            }
        _keys.Width = Dim.Fill(Inset);
        _keys.Height = content;
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);

        Add(_picker, rule);
        foreach (var view in _pages.SelectMany(page => page.Views))
            Add(view);
        Add(_message);
        ShowKeys();
        ShowPage();
    }

    internal bool Confirmed { get; private set; }

    internal bool ExpandToolCalls => _toolCalls.Value == CheckState.Checked;

    internal bool NerdFontIcons => _nerdFont.Value == CheckState.Checked;

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
    public static void Show(IApplication app, DashboardSettings settings, CommandRegistry commands)
    {
        var theme = ThemeSetting.Live(settings);
        using var dialog = new SettingsDialog(
            theme, settings.ReadExpandToolCalls(), settings.ReadNerdFont(), commands, () => app.LayoutAndDraw(true));
        app.Run(dialog);
        dialog.Store(theme, settings);
    }

    /// <summary>Keeps what the dialog was left holding, or puts back what was in effect before it opened.</summary>
    internal void Store(ThemeSetting theme, DashboardSettings settings)
    {
        if (!Confirmed)
        {
            theme.Cancel();
            return;
        }
        theme.Keep();
        settings.WriteExpandToolCalls(ExpandToolCalls);
        settings.WriteNerdFont(NerdFontIcons);
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
            foreach (var view in _pages[index].Views)
                view.Visible = index == selected;
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

    private sealed record Page(string Name, IReadOnlyList<View> Views, Func<int> Width, int Height, string[] Hints);

    /// <summary>A list that can take a key literally. ListView's own type-ahead answers a letter before any
    /// handler the dialog could attach, so the letter being bound would never reach the capture.</summary>
    private sealed class KeyList : ListView
    {
        public Func<Key, bool>? Captured { get; set; }

        protected override bool OnKeyDown(Key key) => Captured?.Invoke(key) == true || base.OnKeyDown(key);
    }
}
