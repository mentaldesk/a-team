using MentalDesk.Tui.Settings;

namespace ATeam.Dashboard;

/// <summary>Settings' Theme page: each theme previewed behind Settings as you pick it.</summary>
public sealed class ThemePage : SettingsPage
{
    internal const string Name = "Theme";

    private readonly ThemeSetting _theme;
    private readonly Label _default;
    private string _kept;

    public ThemePage(ThemeSetting theme, Action redraw) : base(Name)
    {
        _theme = theme;
        _kept = theme.Current;
        Themes = new OptionSelector
        {
            Orientation = Orientation.Vertical,
            Labels = [.. BundledThemes.Names],
            Value = Index(theme.Current),
        };
        _default = Dimmed(new Label { X = Pos.Right(Themes) + 1 });
        Themes.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } index && index >= 0 && index < BundledThemes.Names.Count)
            {
                theme.Preview(BundledThemes.Names[index]);
                redraw();
            }
            Show();
            OnChanged();
        };
        Add(Themes, _default);
        Show();
    }

    public OptionSelector Themes { get; }

    internal string Default => _default.Text;

    public override bool IsChanged => _theme.Current != _kept;

    public override void Keep()
    {
        _theme.Keep();
        _kept = _theme.Current;
    }

    public override void Revert() => Themes.Value = Index(_kept);

    private static int Index(string theme) => BundledThemes.Names.ToList().IndexOf(theme);

    private void Show()
    {
        _default.Text = _theme.Current == BundledThemes.Default ? "" : DefaultNote(BundledThemes.Default);
        _default.Y = Math.Max(0, Index(_theme.Current));
    }
}
