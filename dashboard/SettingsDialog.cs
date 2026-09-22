using System.Collections.ObjectModel;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>The dashboard's settings, one row per setting under its label, keys last.</summary>
public sealed class SettingsDialog : Dialog
{
    private static readonly Key Apply = Key.Enter.WithCtrl;
    private const string Prompt = "Press a key…";
    private static readonly string[] HintTexts = ["Enter rebind", "Ctrl+Enter keep", "Esc cancel"];
    private const string Separator = " · ";
    private const int Inset = 1;

    private readonly CommandRegistry _commands;
    private readonly List<(string Id, string Label, Key Key)> _bindings;
    private readonly List<(string Id, Key Key)> _changed = [];
    private readonly int _labelWidth;
    private readonly int _listTop;
    private readonly CheckBox _toolCalls;
    private readonly KeyList _keys;
    private readonly MessageBar _message = new();
    private bool _capturing;

    public SettingsDialog(ThemeSetting theme, bool expandToolCalls, CommandRegistry commands, Action redraw)
    {
        _commands = commands;
        _bindings = [.. commands.Registered.Select(command => (command.Id, command.Label, command.Key))];
        _labelWidth = _bindings.Count == 0 ? 0 : _bindings.Max(binding => binding.Label.Length);

        const int themesTop = 1;
        var toolCallsTop = themesTop + BundledThemes.Names.Count + 1;
        var keysTop = toolCallsTop + 3;
        _listTop = keysTop + 1;

        Title = "Settings";
        Width = Dim.Func(_ => Fits(Wide() + GetAdornmentsThickness().Horizontal, SuperView?.Viewport.Width), this);
        Height = Dim.Func(_ => Fits(Tall() + GetAdornmentsThickness().Vertical, SuperView?.Viewport.Height), this);

        var label = new Label { X = Inset, Y = 0, Text = "Theme:" };
        var themes = new OptionSelector
        {
            X = Inset + 2,
            Y = themesTop,
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
        var toolCallsLabel = new Label { X = Inset, Y = toolCallsTop, Text = "Tool calls:" };
        _toolCalls = new CheckBox
        {
            X = Inset + 2,
            Y = toolCallsTop + 1,
            Text = "Show tool calls in full",
            Value = expandToolCalls ? CheckState.Checked : CheckState.UnChecked,
        };
        var keysLabel = new Label { X = Inset, Y = keysTop, Text = "Keys:" };
        _keys = new KeyList
        {
            X = Inset,
            Y = _listTop,
            Width = Dim.Fill(Inset),
            Height = Dim.Func(_ => Math.Max(1, Viewport.Height - _listTop - 1 - _message.Lines), this),
        };
        _keys.Captured = key => _capturing && Capture(key);
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);

        Add(label, themes, toolCallsLabel, _toolCalls, keysLabel, _keys);
        Add(Hints());
        Add(_message);
        ShowKeys();
    }

    internal bool Confirmed { get; private set; }

    internal bool ExpandToolCalls => _toolCalls.Value == CheckState.Checked;

    internal ListView Keys => _keys;

    internal MessageBar Message => _message;

    internal IReadOnlyList<string> Rows => [.. _bindings.Select(Row)];

    internal IReadOnlyList<(string Id, Key Key)> Changed => _changed;

    /// <summary>Enter reaches a Dialog as Accept, from the keys list or the theme list alike, and never as a key.
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
            theme, settings.ReadExpandToolCalls(), commands, () => app.LayoutAndDraw(true));
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

    /// <summary>The hint row, each hint clickable and the separators between them not.</summary>
    private View[] Hints()
    {
        Func<bool>[] runs =
        [
            () => { _keys.SetFocus(); return Rebind(); },
            () => Close(confirmed: true),
            () => Close(confirmed: false),
        ];
        var y = Pos.Func(_ => Math.Max(0, Viewport.Height - 1 - _message.Lines), this);
        List<View> row = [];
        Pos x = Inset;
        foreach (var (text, run) in HintTexts.Zip(runs))
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
            hint.Accepting += (_, args) => args.Handled = run();
            row.Add(hint);
            x = Pos.Right(hint);
        }
        return [.. row];
    }

    private static int HintWidth =>
        HintTexts.Sum(text => text.Length) + (Separator.Length * (HintTexts.Length - 1));

    private int Wide() =>
        Math.Max(_bindings.Count == 0 ? 0 : Rows.Max(row => row.Length), HintWidth) + (Inset * 2);

    private int Tall() => _listTop + Math.Max(1, _bindings.Count) + 1 + _message.Lines;

    private static int Fits(int wanted, int? available) => available is { } room ? Math.Min(wanted, room) : wanted;

    /// <summary>A list that can take a key literally. ListView's own type-ahead answers a letter before any
    /// handler the dialog could attach, so the letter being bound would never reach the capture.</summary>
    private sealed class KeyList : ListView
    {
        public Func<Key, bool>? Captured { get; set; }

        protected override bool OnKeyDown(Key key) => Captured?.Invoke(key) == true || base.OnKeyDown(key);
    }
}
