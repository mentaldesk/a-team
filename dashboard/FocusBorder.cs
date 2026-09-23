using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>Draws a view's border in its scheme's focus colour while it holds the focused region. Terminal.Gui
/// draws border lines in <see cref="VisualRole.Normal"/> whatever has focus, so swap the role as it's resolved
/// rather than overriding the scheme, which would have to be reapplied on every theme change.</summary>
internal sealed class FocusBorder
{
    private readonly View _pane;
    private bool _focused;

    internal FocusBorder(View pane)
    {
        _pane = pane;
        if (pane.Border?.GetOrCreateView() is not { } border)
            return;
        border.GettingAttributeForRole += (_, e) =>
        {
            if (!_focused)
                return;
            var role = e.Role switch
            {
                VisualRole.Normal => VisualRole.Focus,
                VisualRole.HotNormal => VisualRole.HotFocus,
                _ => e.Role,
            };
            if (role == e.Role)
                return;
            e.Result = _pane.GetAttributeForRole(role);
            e.Handled = true;
        };
    }

    internal bool Focused => _focused;

    internal void Show(bool focused)
    {
        if (focused == _focused)
            return;
        _focused = focused;
        _pane.SetNeedsDraw();
    }
}
