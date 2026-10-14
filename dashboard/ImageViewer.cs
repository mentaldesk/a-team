using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>An attached image on its own, filling the window, until Esc.</summary>
public sealed class ImageViewer : Dialog
{
    private const string CloseHint = "close";

    private readonly StatusBar _hints = new();

    public ImageViewer(Picture picture, string alt)
    {
        Title = alt;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        var image = new ImageView { Width = Dim.Fill(), Height = Dim.Fill(1), CanFocus = false, Image = picture.Over(GetAttributeForRole(VisualRole.Normal).Background) };
        image.MouseBindings.Clear();
        _hints.Y = Pos.AnchorEnd(1);
        _hints.Show([new HintedCommand(CloseHint, "Esc close")], _ => Close());
        Add(image, _hints);
    }

    public static void Show(IApplication app, Picture picture, string alt)
    {
        using var viewer = new ImageViewer(picture, alt);
        app.Run(viewer);
    }

    protected override bool OnKeyDown(Key key) => key == Key.Esc ? Close() : base.OnKeyDown(key);

    private bool Close()
    {
        RequestStop();
        return true;
    }
}
