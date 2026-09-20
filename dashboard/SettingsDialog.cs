using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>The dashboard's settings, one row per setting under its label.</summary>
public sealed class SettingsDialog : Dialog
{
    private static readonly Key Apply = Key.Enter.WithCtrl;
    private readonly CheckBox _toolCalls;

    public SettingsDialog(ThemeSetting theme, bool expandToolCalls, Action redraw)
    {
        Title = "Settings";
        Width = 36;
        Height = 16;

        var label = new Label { X = 1, Y = 0, Text = "Theme:" };
        var themes = new OptionSelector
        {
            X = 3,
            Y = Pos.Bottom(label),
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
        var toolCallsLabel = new Label { X = 1, Y = Pos.Bottom(themes) + 1, Text = "Tool calls:" };
        _toolCalls = new CheckBox
        {
            X = 3,
            Y = Pos.Bottom(toolCallsLabel),
            Text = "Show tool calls in full",
            Value = expandToolCalls ? CheckState.Checked : CheckState.UnChecked,
        };
        var keys = new Label { X = 1, Y = Pos.Bottom(_toolCalls) + 1, Text = $"{Apply}: OK   {Key.Esc}: Cancel" };
        Add(label, themes, toolCallsLabel, _toolCalls, keys);

        var ok = new Button { Text = "OK" };
        ok.Accepting += (_, _) => Close(confirmed: true);
        var cancel = new Button { Text = "Cancel" };
        cancel.Accepting += (_, _) => Close(confirmed: false);
        AddButton(ok);
        AddButton(cancel);
        // AddButton makes the last button added the default; neither is, so neither answers Enter.
        ok.IsDefault = false;
        cancel.IsDefault = false;
        DefaultAcceptView = null;
    }

    internal bool Confirmed { get; private set; }

    internal bool ExpandToolCalls => _toolCalls.Value == CheckState.Checked;

    /// <summary>Terminal.Gui closes a Dialog when a subview's Accept reaches it unhandled, and Enter raises Accept
    /// on the theme list. The buttons close the dialog from their own Accepting, so nothing here needs it.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => true;

    protected override bool OnKeyDown(Key key)
    {
        if (key == Apply)
            return Close(confirmed: true);
        if (key == Key.Esc)
            return Close(confirmed: false);
        return base.OnKeyDown(key);
    }

    private bool Close(bool confirmed)
    {
        Confirmed = confirmed;
        RequestStop();
        return true;
    }

    /// <summary>Runs the dialog, keeping what was picked in it only if it was accepted.</summary>
    public static void Show(IApplication app, DashboardSettings settings)
    {
        var theme = ThemeSetting.Live(settings);
        using var dialog = new SettingsDialog(theme, settings.ReadExpandToolCalls(), () => app.LayoutAndDraw(true));
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
    }
}
