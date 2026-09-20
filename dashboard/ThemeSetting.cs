namespace ATeam.Dashboard;

/// <summary>The theme while the Settings dialog is open: previewed on the real screen, put back on cancel.</summary>
public sealed class ThemeSetting
{
    private readonly Action<string> _apply;
    private readonly string _opened;

    public ThemeSetting(string current, Action<string> apply)
    {
        _opened = current;
        Current = current;
        _apply = apply;
    }

    public static ThemeSetting Live() => new(BundledThemes.Current, BundledThemes.Apply);

    public string Current { get; private set; }

    public void Preview(string theme)
    {
        if (theme == Current)
            return;
        Current = theme;
        _apply(theme);
    }

    public void Cancel() => Preview(_opened);
}
