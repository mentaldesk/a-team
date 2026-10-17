using Terminal.Gui.Text;

namespace ATeam.Dashboard;

/// <summary>A dialog's buttons at its foot, centred, wrapping onto another row rather than leaving any out. Only
/// the visible ones take a place, in the order they were added.</summary>
public sealed class ButtonBar : View
{
    private const int Gap = 3;
    private const int Inset = 1;

    public ButtonBar()
    {
        X = Inset;
        Width = Dim.Fill(Inset);
        Height = Dim.Func(_ => Rows, this);
        CanFocus = false;
    }

    /// <summary>How many rows the buttons take across the dialog as it's laid out now.</summary>
    public int Rows => RowsOf(SuperView is { } dialog ? dialog.Viewport.Width - (Inset * 2) : 0).Count;

    internal IReadOnlyList<Button> Shown => [.. SubViews.OfType<Button>().Where(button => button.Visible)];

    /// <summary>The shown buttons, a row at a time, filled from the first so the last row ends with the last button.</summary>
    internal IReadOnlyList<IReadOnlyList<Button>> RowsOf(int width)
    {
        List<IReadOnlyList<Button>> rows = [];
        List<Button> row = [];
        var used = 0;
        foreach (var button in Shown)
        {
            var wide = Wide(button);
            if (row.Count > 0 && used + Gap + wide > width)
            {
                rows.Add(row);
                row = [];
                used = 0;
            }
            used += (row.Count > 0 ? Gap : 0) + wide;
            row.Add(button);
        }
        if (row.Count > 0)
            rows.Add(row);
        return rows;
    }

    /// <summary>Call once a button's shown or hidden, or its label changes.</summary>
    public void Refresh()
    {
        SetNeedsLayout();
        SuperView?.SetNeedsLayout();
        SetNeedsDraw();
    }

    protected override void OnSubViewLayout(LayoutEventArgs args)
    {
        var rows = RowsOf(Viewport.Width);
        for (var y = 0; y < rows.Count; y++)
        {
            var x = Math.Max(0, (Viewport.Width - RowWidth(rows[y])) / 2);
            foreach (var button in rows[y])
            {
                button.Width = Wide(button);
                button.X = x;
                button.Y = y;
                x += Wide(button) + Gap;
            }
        }
        base.OnSubViewLayout(args);
    }

    private static int RowWidth(IReadOnlyList<Button> row) => row.Sum(Wide) + (Gap * (row.Count - 1));

    private static int Wide(Button button) => button.Text.GetColumns();
}
