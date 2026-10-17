using Terminal.Gui.Drawing;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public class ImageViewerTests
{
    [Fact]
    public void It_shows_the_picture_and_one_Esc_Close_button()
    {
        using var viewer = new ImageViewer(new Picture(new Color[4, 4]), "A screenshot");

        Assert.Single(viewer.SubViews.OfType<ImageView>());
        Assert.Equal(["Esc Close"], viewer.SubViews.OfType<ButtonBar>().Single().Shown.Select(button => button.Text.Trim()));
        Assert.Empty(viewer.SubViews.OfType<StatusBar>());
    }

    [Fact]
    public void F1_lists_Esc_Close_under_the_picture_s_alt_text()
    {
        using var viewer = new ImageViewer(new Picture(new Color[4, 4]), "A screenshot");

        var sheet = DialogTests.F1(viewer);

        Assert.Equal("A screenshot", sheet.Here!.Heading);
        Assert.Equal([("Esc", "Close")], DialogTests.Rows(sheet));
    }
}
