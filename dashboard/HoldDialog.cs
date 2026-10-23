using MentalDesk.Tui.Dialogs;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>Why you're holding a Ready task, which the board posts on it so its card in Blocked says why.</summary>
public sealed class HoldDialog : DashboardDialog
{
    private const string HoldId = "hold.hold";
    private const int Wide = 60;

    private readonly TextField _why;

    public HoldDialog(int number)
        : base($"Hold #{number}")
    {
        Width = Dim.Func(_ => Math.Min(Wide, SuperView?.Viewport.Width ?? Wide), this);
        Height = Dim.Func(_ => 2 + Foot.Rows + GetAdornmentsThickness().Vertical, this);
        Add(new Label { Text = "Why:", X = 1, Y = 0 });
        _why = new TextField { X = 6, Y = 0, Width = Dim.Fill(1) };
        _why.TextChanged += (_, _) => Grey(HoldId, Given);
        Bind(HoldId, Key.Enter, "Hold", () => Done(_why.Text), () => Given);
        Register(CancelId, "Cancel", () => Done(null));
        AddButton(ButtonKind.Primary, HoldId);
        AddButton(ButtonKind.Secondary, CancelId);
        Grey(HoldId, Given);
        Foot.Y = 2;
        Add(_why, Foot);
        _why.SetFocus();
    }

    /// <summary>The reason given, or null where the dialog was cancelled.</summary>
    internal string? Chosen { get; private set; }

    internal TextField Why => _why;

    private bool Given => !string.IsNullOrWhiteSpace(_why.Text);

    /// <summary>Opens the dialog, and returns the reason given, or null where it was cancelled.</summary>
    public static string? Show(IApplication app, WaitingItem item)
    {
        using var dialog = new HoldDialog(item.Number);
        DialogHost.Run(app, dialog);
        return dialog.Chosen;
    }

    private void Done(string? why)
    {
        if (why is not null && string.IsNullOrWhiteSpace(why))
            return;
        Chosen = why?.Trim();
        RequestStop();
    }
}
