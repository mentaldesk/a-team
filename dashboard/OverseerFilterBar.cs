using MentalDesk.Tui.Fields;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>Overseer's filter: docked above the column headings, laid out as the guide's find bar is, with the count of
/// the cards it finds dimmed at its right.</summary>
public sealed class OverseerFilterBar : View
{
    public const int Rows = 2;
    private const int LabelWidth = 9;
    private const int SideWidth = 14;

    private readonly Label _label = new() { X = 1, Y = 0, Text = "Filter" };
    private readonly Label _count = new() { X = Pos.AnchorEnd(SideWidth), Y = 0, Width = SideWidth - 1, TextAlignment = Alignment.End };

    public OverseerFilterBar()
    {
        Height = Rows;
        BorderStyle = LineStyle.Single;
        Border!.Thickness = new Thickness(0, 0, 0, 1);
        _count.GettingAttributeForRole += (_, e) =>
        {
            if (e.Role != VisualRole.Normal)
                return;
            e.Result = _count.GetAttributeForRole(VisualRole.ReadOnly);
            e.Handled = true;
        };
        Add(_label, Field, _count);
        Field.TextChanged += (_, _) => Changed?.Invoke();
    }

    public PlaceholderField Field { get; } = new() { X = LabelWidth, Y = 0, Width = Dim.Fill(SideWidth), Placeholder = "Type to filter", CanFocus = false };

    public string Query => Field.Text;

    /// <summary>Whether the field can take focus: only while you're typing in it, so focus on the cards stays there.</summary>
    public bool Editing
    {
        get => Field.CanFocus;
        set
        {
            CanFocus = value;
            Field.CanFocus = value;
        }
    }

    /// <summary>The count at its right, empty while the filter is.</summary>
    public string Count
    {
        get => _count.Text;
        set
        {
            if (_count.Text != value)
                _count.Text = value;
        }
    }

    public event Action? Changed;
}
