using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard;

/// <summary>A menu item that can't run here, drawn dimmed and doing nothing, rather than disabled: the arrows skip a
/// disabled item. A copy of MentalDesk.Tui's own, which the library keeps internal, keeping its letter underlined.</summary>
internal sealed class DimmedMenuItem : MenuItem
{
    /// <summary>Terminal.Gui colours the key from the menu rather than the item, so it's dimmed here.</summary>
    public DimmedMenuItem() =>
        KeyView.GettingAttributeForRole += (_, args) =>
        {
            if (!Dimmed || args.Role != VisualRole.Normal)
                return;
            args.Result = GetAttributeForRole(VisualRole.Normal);
            args.Handled = true;
        };

    public bool Dimmed
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            SetNeedsDraw();
        }
    }

    protected override bool OnGettingAttributeForRole(in VisualRole role, ref Attribute currentAttribute)
    {
        if (!Dimmed)
            return base.OnGettingAttributeForRole(role, ref currentAttribute);
        var scheme = GetScheme();
        var disabled = scheme.GetAttributeForRole(VisualRole.Disabled, App?.Driver?.DefaultAttribute);
        if (role is VisualRole.HotNormal or VisualRole.HotFocus)
            disabled = disabled with { Style = disabled.Style | TextStyle.Underline };
        currentAttribute = HasFocus
            ? disabled with { Background = scheme.GetAttributeForRole(VisualRole.Focus, App?.Driver?.DefaultAttribute).Background }
            : disabled;
        return true;
    }

    protected override bool OnActivating(CommandEventArgs args) => Dimmed || base.OnActivating(args);

    protected override bool OnAccepting(CommandEventArgs args) => Dimmed || base.OnAccepting(args);
}
