using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>The dashboard's settings, one row per setting under its label. Today there is only the theme.</summary>
public sealed class SettingsDialog : Dialog
{
    private static readonly Key Apply = Key.Enter.WithCtrl;

    public SettingsDialog(ThemeSetting theme, Action redraw)
    {
        Title = "Settings";
        Width = 36;
        Height = 13;

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
        var keys = new Label { X = 1, Y = Pos.Bottom(themes) + 1, Text = $"{Apply}: OK   {Key.Esc}: Cancel" };
        Add(label, themes, keys);

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

    /// <summary>Runs the dialog, keeping the theme picked in it only if it was accepted.</summary>
    public static void Show(IApplication app, ThemeSetting theme)
    {
        using var dialog = new SettingsDialog(theme, () => app.LayoutAndDraw(true));
        app.Run(dialog);
        if (!dialog.Confirmed)
            theme.Cancel();
    }
}
