using System.Drawing;
using System.Text;
using MentalDesk.Tui.Dialogs;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>Gives the secret a task asks for: the value goes to GitHub as you, and is kept nowhere.</summary>
public sealed class GiveSecretDialog : DashboardDialog
{
    private const string SaveId = "secret.save";
    private const int Wide = 66;
    private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(100);

    private readonly SecretAsk _ask;
    private readonly Task<bool> _alreadySet;
    private readonly Func<string, Task<string?>> _save;
    private readonly Label _request;
    private readonly Label _valueLabel;
    private readonly TextField _value;
    private readonly Label _note;
    private Task<string?>? _saving;
    private bool _replaces;
    private int _wrappedTo;

    public GiveSecretDialog(WaitingItem item, Task<bool> alreadySet, Func<string, Task<string?>> save)
        : base($"Give a secret · {item.Team} #{item.Number}")
    {
        _ask = item.Secret ?? throw new ArgumentException($"#{item.Number} asks for no secret", nameof(item));
        _alreadySet = alreadySet;
        _save = save;
        Width = Dim.Func(_ => Math.Min(Wide, SuperView?.Viewport.Width ?? App?.Screen.Width ?? Wide), this);
        _request = new Label { X = 1, Y = 0, Width = Dim.Fill(1), Height = Dim.Func(_ => Rows(_request!), this), HotKeySpecifier = (Rune)0xffff };
        _valueLabel = new Label { Text = "Value:", X = 1, Y = Pos.Bottom(_request) + 1 };
        _value = new TextField { X = 8, Y = Pos.Top(_valueLabel), Width = Dim.Fill(1), Secret = true };
        _note = new Label { X = 1, Y = Pos.Bottom(_valueLabel) + 1, Width = Dim.Fill(1), Height = Dim.Func(_ => Rows(_note!), this), HotKeySpecifier = (Rune)0xffff };
        Height = Dim.Func(_ => Rows(_request) + 3 + Rows(_note) + 1 + Foot.Rows + Alert.Lines + GetAdornmentsThickness().Vertical, this);
        Bind(SaveId, Key.Enter, "Save", Save);
        Register(CancelId, "Cancel", () =>
        {
            if (_saving is null)
                RequestStop();
        });
        AddButton(ButtonKind.Primary, SaveId);
        AddButton(ButtonKind.Secondary, CancelId);
        Foot.Y = Pos.Bottom(_note) + 1;
        Alert.Y = Pos.Bottom(Foot);
        Add(_request, _valueLabel, _value, _note, Foot);
        Check(_value, _valueLabel, () => string.IsNullOrWhiteSpace(_value.Text) ? "Value is needed" : null);
        SubViewsLaidOut += (_, _) =>
        {
            Wrap();
            PlaceCursor();
        };
        Wrap();
        TakeInAlreadySet();
        _value.SetFocus();
    }

    /// <summary>Whether GitHub took the value.</summary>
    internal bool Saved { get; private set; }

    internal TextField Value => _value;

    internal string Request => _request.Text;

    internal string Note => _note.Text;

    /// <summary>Opens the dialog, and says whether the secret was saved.</summary>
    public static bool Show(IApplication app, WaitingItem item, Task<bool> alreadySet, Func<string, Task<string?>> save)
    {
        using var dialog = new GiveSecretDialog(item, alreadySet, save);
        var open = true;
        app.AddTimeout(PollEvery, () => open && !dialog.TakeInAlreadySet());
        DialogHost.Run(app, dialog);
        open = false;
        return dialog.Saved;
    }

    /// <summary>True once the read of whether it's already set is over, whatever it found.</summary>
    internal bool TakeInAlreadySet()
    {
        if (!_alreadySet.IsCompleted)
            return false;
        if (_alreadySet.IsCompletedSuccessfully && _alreadySet.Result && !_replaces)
        {
            _replaces = true;
            _wrappedTo = 0;
            Wrap();
            SetNeedsLayout();
        }
        return true;
    }

    // TextField only places its cursor as it takes focus or resizes, so it's left behind where the dialog then moves.
    private void PlaceCursor()
    {
        if (_value.HasFocus && !_value.ReadOnly)
            _value.Cursor = _value.Cursor with { Position = _value.ViewportToScreen(new Point(_value.InsertionPoint - _value.ScrollOffset, 0)) };
    }

    private void Wrap()
    {
        var width = Math.Max(1, (Frame.Width > 0 ? Frame.Width : Wide) - GetAdornmentsThickness().Horizontal - 2);
        if (width == _wrappedTo)
            return;
        _wrappedTo = width;
        string[] request = [$"{Capital(_ask.Asker)} asks for {_ask.Name}, {_ask.Place}.", "", $"Why: {_ask.Why}"];
        string[] note =
        [
            .. _replaces ? new[] { $"{_ask.Name} is already set there. Saving will replace it." } : [],
            "GitHub stores it encrypted. No agent ever sees it.",
        ];
        _request.Text = Wrapped(request, width);
        _note.Text = Wrapped(note, width);
        SetNeedsLayout();
    }

    private static string Wrapped(IEnumerable<string> paragraphs, int width) =>
        string.Join('\n', paragraphs.SelectMany(text => text.Split('\n')).SelectMany(line => AskFixDialog.Lines(line, width)));

    private static int Rows(Label label) => label.Text.Split('\n').Length;

    private static string Capital(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private void Save()
    {
        if (_saving is not null || !Checked())
            return;
        Alert.Clear();
        _value.ReadOnly = true;
        var saving = _saving = _save(_value.Text);
        if (saving.IsCompleted)
        {
            Settle(saving);
            return;
        }
        // Polled rather than Invoked: the reader may have opened this inside a timer, and Invoke waits on that timer's lock.
        App?.AddTimeout(PollEvery, () =>
        {
            if (!saving.IsCompleted)
                return true;
            Settle(saving);
            return false;
        });
    }

    private void Settle(Task<string?> saving)
    {
        _saving = null;
        _value.ReadOnly = false;
        var refused = saving.IsCompletedSuccessfully ? saving.Result : "couldn't reach GitHub";
        if (refused is null)
        {
            Saved = true;
            RequestStop();
            return;
        }
        Refuse(_value, refused);
    }
}
