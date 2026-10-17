using MentalDesk.Tui.Dialogs;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>The image file to attach, for one you'd rather type the path of than drop.</summary>
public sealed class AttachFileDialog : DashboardDialog
{
    internal const string Name = "Attach image file";
    private const string AttachId = "attach.attach";
    private const int Wide = 60;

    private readonly TextField _file;

    public AttachFileDialog()
        : base(Name)
    {
        Width = Dim.Func(_ => Math.Min(Wide, SuperView?.Viewport.Width ?? Wide), this);
        Height = Dim.Func(_ => 2 + Foot.Rows + GetAdornmentsThickness().Vertical, this);
        Add(new Label { Text = "File", X = 1, Y = 0 });
        _file = new TextField { X = 7, Y = 0, Width = Dim.Fill(1) };
        Bind(AttachId, Key.Enter.WithCtrl, "Attach", () => Done(_file.Text));
        Register(CancelId, "Cancel", () => Done(null));
        AddButton(ButtonKind.Primary, AttachId);
        AddButton(ButtonKind.Secondary, CancelId);
        Foot.Y = 2;
        Add(_file, Foot);
        _file.SetFocus();
    }

    /// <summary>The path typed, or null where the dialog was cancelled.</summary>
    internal string? Chosen { get; private set; }

    internal TextField File => _file;

    /// <summary>Opens the dialog, and returns the path typed, or null where it was cancelled or left empty.</summary>
    public static string? Show(IApplication app)
    {
        using var dialog = new AttachFileDialog();
        DialogHost.Run(app, dialog);
        return dialog.Chosen;
    }

    private void Done(string? path)
    {
        Chosen = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        RequestStop();
    }
}
