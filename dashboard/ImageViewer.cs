using MentalDesk.Tui.Dialogs;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>An attached image on its own, filling the window, until Esc.</summary>
public sealed class ImageViewer : DashboardDialog
{
    public ImageViewer(Picture picture, string alt)
        : base(alt)
    {
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        var image = new ImageView { Width = Dim.Fill(), Height = Dim.Fill(1), CanFocus = false, Image = picture.Over(GetAttributeForRole(VisualRole.Normal).Background) };
        image.MouseBindings.Clear();
        Register(CancelId, "Close", () => RequestStop());
        AddButton(ButtonKind.Secondary, CancelId);
        Foot.Y = Pos.AnchorEnd(1);
        Add(image, Foot);
    }

    public static void Show(IApplication app, Picture picture, string alt)
    {
        using var viewer = new ImageViewer(picture, alt);
        DialogHost.Run(app, viewer);
    }
}
