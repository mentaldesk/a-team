using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Fields;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>Why you're holding a Ready task, which the board posts on it so its card in Blocked says why.</summary>
public sealed class HoldDialog : DashboardDialog
{
    private const string HoldId = "hold.hold";
    private const int Wide = 60;

    private readonly PlaceholderField _why;

    public HoldDialog(int number)
        : base($"Hold #{number}")
    {
        Width = Dim.Func(_ => Math.Min(Wide, SuperView?.Viewport.Width ?? Wide), this);
        Height = Dim.Func(_ => 2 + Foot.Rows + Alert.Lines + GetAdornmentsThickness().Vertical, this);
        var label = new Label { Text = "Why:", X = 1, Y = 0 };
        _why = new PlaceholderField { X = 6, Y = 0, Width = Dim.Fill(1) };
        Bind(HoldId, Key.Enter, "Hold", Hold);
        Register(CancelId, "Cancel", RequestStop);
        AddButton(ButtonKind.Primary, HoldId);
        AddButton(ButtonKind.Secondary, CancelId);
        Foot.Y = 2;
        Alert.Y = Pos.Bottom(Foot);
        Add(label, _why, Foot);
        Check(_why, label, () => string.IsNullOrWhiteSpace(_why.Text) ? "Why is needed" : null);
        _why.SetFocus();
    }

    /// <summary>The reason given, or null where the dialog was cancelled.</summary>
    internal string? Chosen { get; private set; }

    internal PlaceholderField Why => _why;

    /// <summary>Opens the dialog, and returns the reason given, or null where it was cancelled.</summary>
    public static string? Show(IApplication app, WaitingItem item)
    {
        using var dialog = new HoldDialog(item.Number);
        DialogHost.Run(app, dialog);
        return dialog.Chosen;
    }

    private void Hold()
    {
        if (!Checked())
            return;
        Chosen = _why.Text.Trim();
        RequestStop();
    }
}
