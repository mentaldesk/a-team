using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>The image file to attach, for one you'd rather type the path of than drop.</summary>
public sealed class AttachFileDialog : Dialog
{
    internal const string Name = "Attach image file";
    private const string AttachHint = "attach";
    private const string CancelHint = "cancel";
    private const int Wide = 60;

    private readonly TextField _file;
    private readonly StatusBar _hints = new();

    public AttachFileDialog()
    {
        Title = Name;
        Width = Dim.Func(_ => Math.Min(Wide, SuperView?.Viewport.Width ?? Wide), this);
        Height = 3 + GetAdornmentsThickness().Vertical;
        Add(new Label { Text = "File", X = 1, Y = 0 });
        _file = new TextField { X = 7, Y = 0, Width = Dim.Fill(1) };
        _hints.Y = 2;
        Add(_file, _hints);
        _file.KeyDown += (_, key) => key.Handled = key.Handled || Pressed(key);
        _hints.Show([new HintedCommand(AttachHint, "Ctrl+Enter attach"), new HintedCommand(CancelHint, "Esc cancel")],
            hint => hint == AttachHint ? Done(_file.Text) : Done(null));
        _file.SetFocus();
    }

    /// <summary>The path typed, or null where the dialog was cancelled.</summary>
    internal string? Chosen { get; private set; }

    internal TextField File => _file;

    /// <summary>Opens the dialog, and returns the path typed, or null where it was cancelled or left empty.</summary>
    public static string? Show(IApplication app)
    {
        using var dialog = new AttachFileDialog();
        app.Run(dialog);
        return dialog.Chosen;
    }

    /// <summary>Enter doesn't attach: that's Ctrl+Enter, as in every dialog.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => true;

    protected override bool OnKeyDown(Key key) => Pressed(key) || base.OnKeyDown(key);

    private bool Pressed(Key key) =>
        key == Key.Enter.WithCtrl ? Done(_file.Text)
        : key == Key.Esc && Done(null);

    private bool Done(string? path)
    {
        Chosen = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        RequestStop();
        return true;
    }
}
