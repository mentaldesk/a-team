namespace ATeam.Dashboard;

/// <summary>The theme while the Settings dialog is open: previewed on the real screen, kept on OK, put back on cancel.</summary>
public sealed class ThemeSetting
{
    private readonly Action<string> _apply;
    private readonly Action<string> _keep;
    private readonly string _opened;

    public ThemeSetting(string current, Action<string> apply, Action<string> keep)
    {
        _opened = current;
        Current = current;
        _apply = apply;
        _keep = keep;
    }

    public static ThemeSetting Live(DashboardSettings settings) =>
        new(BundledThemes.Current, BundledThemes.Apply, settings.WriteTheme);

    public string Current { get; private set; }

    public void Preview(string theme)
    {
        if (theme == Current)
            return;
        Current = theme;
        _apply(theme);
    }

    public void Keep() => _keep(Current);

    public void Cancel() => Preview(_opened);
}
