using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>The dashboard's settings, one row per setting under its label. Today there is only the theme.</summary>
public sealed class SettingsDialog : Dialog
{
    private bool _accepted;

    public SettingsDialog(ThemeSetting theme, Action redraw)
    {
        Title = "Settings";
        Width = 36;
        Height = 11;

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
        Add(label, themes);

        var ok = new Button { Text = "OK", IsDefault = true };
        ok.Accepting += (_, _) =>
        {
            _accepted = true;
            RequestStop();
        };
        var cancel = new Button { Text = "Cancel" };
        cancel.Accepting += (_, _) => RequestStop();
        AddButton(ok);
        AddButton(cancel);
    }

    /// <summary>Runs the dialog, keeping the theme picked in it only if it was accepted.</summary>
    public static void Show(IApplication app, ThemeSetting theme)
    {
        using var dialog = new SettingsDialog(theme, () => app.LayoutAndDraw(true));
        app.Run(dialog);
        if (!dialog._accepted)
            theme.Cancel();
    }
}
