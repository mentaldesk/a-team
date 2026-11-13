using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Fields;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>The image file to attach, for one you'd rather type the path of than drop.</summary>
public sealed class AttachFileDialog : DashboardDialog
{
    internal const string Name = "Attach image file";
    private const string AttachId = "attach.attach";
    private const int Wide = 60;

    private readonly PlaceholderField _file;

    /// <param name="latest">The screenshot the field starts with, said when it was taken.</param>
    public AttachFileDialog(Screenshot? latest = null, TimeProvider? clock = null)
        : base(Name)
    {
        var rows = latest is null ? 2 : 3;
        Width = Dim.Func(_ => Math.Min(Wide, SuperView?.Viewport.Width ?? Wide), this);
        Height = Dim.Func(_ => rows + Foot.Rows + GetAdornmentsThickness().Vertical, this);
        Add(new Label { Text = "File", X = 1, Y = 0 });
        _file = new PlaceholderField { X = 7, Y = 0, Width = Dim.Fill(1) };
        if (latest is not null)
        {
            _file.Text = latest.Path;
            _file.InsertionPoint = latest.Path.Length;
            Taken = new Label { Text = latest.Said((clock ?? TimeProvider.System).GetUtcNow()), X = 7, Y = 1, CanFocus = false, SchemeName = LogSchemes.Dimmed };
            Add(Taken);
        }
        Bind(AttachId, Key.Enter.WithCtrl, "Attach", () => Done(_file.Text));
        Register(CancelId, "Cancel", () => Done(null));
        AddButton(ButtonKind.Primary, AttachId);
        AddButton(ButtonKind.Secondary, CancelId);
        Foot.Y = rows;
        Add(_file, Foot);
        _file.SetFocus();
    }

    /// <summary>The path typed, or null where the dialog was cancelled.</summary>
    internal string? Chosen { get; private set; }

    internal PlaceholderField File => _file;

    internal Label? Taken { get; }

    /// <summary>Opens the dialog, and returns the path typed, or null where it was cancelled or left empty.</summary>
    public static string? Show(IApplication app, Screenshot? latest = null)
    {
        using var dialog = new AttachFileDialog(latest);
        DialogHost.Run(app, dialog);
        return dialog.Chosen;
    }

    private void Done(string? path)
    {
        Chosen = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        RequestStop();
    }
}
