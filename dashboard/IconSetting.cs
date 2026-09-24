namespace ATeam.Dashboard;

/// <summary>The icon style while the Settings dialog is open: previewed on the real screen, kept on OK,
/// put back on cancel.</summary>
public sealed class IconSetting
{
    private readonly Action<IconStyle> _apply;
    private readonly Action<IconStyle> _keep;
    private readonly IconStyle _opened;

    public IconSetting(IconStyle current, Action<IconStyle> apply, Action<IconStyle> keep)
    {
        _opened = current;
        Current = current;
        _apply = apply;
        _keep = keep;
    }

    public IconStyle Current { get; private set; }

    public void Preview(IconStyle style)
    {
        if (style == Current)
            return;
        Current = style;
        _apply(style);
    }

    public void Keep() => _keep(Current);

    public void Cancel() => Preview(_opened);
}
