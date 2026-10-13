using System.Collections.ObjectModel;
using MentalDesk.Tui.Fields;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>Adds an idea to a team's board without leaving the app: the team, a title, a few lines and a rank.</summary>
public sealed class NewIdeaDialog : Dialog
{
    internal const string RankCaption = "None leaves it in Triage to rank later.";
    private const string AddHint = "add";
    private const string CancelHint = "cancel";
    private const string PasteHint = "paste";
    private const string AttachHint = "attach";
    private const int Inset = 1;
    private const int FieldX = 11;
    private const int DescriptionLines = 10;
    private const int Wide = 76;
    private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(100);

    private readonly IdeaFiler _filer;
    private readonly Func<bool> _confirmDiscard;
    private readonly DropDownList _team;
    private readonly TextField _title;
    private readonly MultiLineField _description;
    private readonly OptionSelector<Rank> _rank;
    private readonly StatusBar _hints = new();
    private readonly MessageBar _message = new();
    private readonly Attaching? _attaching;
    private readonly ImageAttach? _attach;
    private Task<string?>? _adding;

    public NewIdeaDialog(
        IReadOnlyList<string> teams, string team, IdeaFiler filer, Func<bool>? confirmDiscard = null, Attaching? attaching = null)
    {
        _filer = filer;
        _confirmDiscard = confirmDiscard ?? (() => true);
        Title = "New idea";
        Width = Dim.Func(_ => Math.Min(Wide, SuperView?.Viewport.Width ?? Wide), this);
        Height = Dim.Func(_ => DescriptionLines + 10 + _message.Lines + GetAdornmentsThickness().Vertical, this);

        Add(new Label { Text = "Team", X = Inset, Y = 1 });
        _team = new DropDownList
        {
            X = FieldX,
            Y = 1,
            Width = 24,
            ReadOnly = true,
            Source = new ListWrapper<string>(new ObservableCollection<string>(teams)),
            Text = team,
        };
        Add(new Label { Text = "Title", X = Inset, Y = 2 });
        _title = new TextField { X = FieldX, Y = 2, Width = Dim.Fill(Inset) };
        Add(new Label { Text = "Description", X = Inset, Y = 4 });
        _description = new MultiLineField { X = Inset, Y = 5, Width = Dim.Fill(Inset), Height = DescriptionLines };
        var rankY = 5 + DescriptionLines + 1;
        Add(new Label { Text = "Priority", X = Inset, Y = rankY });
        _rank = new OptionSelector<Rank>
        {
            X = FieldX,
            Y = rankY,
            Orientation = Orientation.Horizontal,
            // NoStop is what makes the arrows move between the ranks; with the default the options are Tab stops.
            TabBehavior = TabBehavior.NoStop,
            Value = Rank.None,
        };
        Add(new Label { Text = RankCaption, X = FieldX, Y = rankY + 1 });
        _hints.Y = rankY + 3;
        _message.Y = rankY + 4;
        Add(_team, _title, _description, _rank, _hints, _message);
        _rank.KeyDown += (_, key) =>
            key.Handled = key == Key.CursorRight ? Step(+1) : key == Key.CursorLeft && Step(-1);
        // The fields bind Enter and Esc themselves, so the dialog's keys are caught before they see them.
        foreach (var field in new View[] { _team, _title, _description, _rank })
            field.KeyDown += (_, key) => key.Handled = key.Handled || Pressed(key);
        _attaching = attaching;
        if (attaching is not null)
        {
            _attach = new ImageAttach(_description, file => attaching.Upload(_team.Text, file), attaching.Clipboard, Say);
            _description.KeyDown += (_, key) => key.Handled = key.Handled || _attach.Pressed(key);
        }
        _hints.Show([
            new HintedCommand(AddHint, "Ctrl+Enter add"),
            .. attaching is null ? Array.Empty<HintedCommand>()
                : [new HintedCommand(PasteHint, "Ctrl+V paste"), new HintedCommand(AttachHint, $"{KeyNames.Short(attaching.Key)} attach file")],
            new HintedCommand(CancelHint, "Esc cancel"),
        ], Run);
        _title.SetFocus();
    }

    /// <summary>The idea once it's on the board, or null where the dialog was cancelled.</summary>
    internal (string Team, int Number)? Added { get; private set; }

    internal DropDownList Team => _team;

    internal TextField IdeaTitle => _title;

    internal MultiLineField Description => _description;

    internal OptionSelector<Rank> Ranks => _rank;

    internal StatusBar Hints => _hints;

    internal MessageBar Message => _message;

    /// <summary>The idea as the dialog holds it now.</summary>
    internal IdeaDraft Draft => new(_team.Text, _title.Text, _description.Text, _rank.Value ?? Rank.None);

    /// <summary>Opens the dialog, and returns the idea it added, or null where it was cancelled.</summary>
    public static (string Team, int Number)? Show(
        IApplication app, IReadOnlyList<string> teams, string team, IdeaFiler filer, Attaching? attaching = null)
    {
        using var dialog = new NewIdeaDialog(teams, team, filer, () => DiscardDialog.Show(app, DiscardDialog.Idea), attaching);
        app.Run(dialog);
        return dialog.Added;
    }

    /// <summary>Enter picks a team or a rank, and never adds: that's Ctrl+Enter, so a stray Enter can't.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => true;

    protected override bool OnKeyDown(Key key) => Pressed(key) || base.OnKeyDown(key);

    /// <summary>A paste reaches the app before any view, so a dropped image or a pasted screenshot is caught here.</summary>
    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        if (App is not { } app)
            return;
        if (newIsRunning)
            app.Paste += OnAppPaste;
        else
            app.Paste -= OnAppPaste;
    }

    internal void OnAppPaste(object? sender, PasteEventArgs paste) =>
        paste.Handled = paste.Handled || (_description.HasFocus && _attach?.Paste(paste.Text) == true);

    private bool Pressed(Key key) =>
        key == Key.Enter.WithCtrl ? AddIdea()
        : _attaching is not null && key == _attaching.Key ? AttachFile()
        : key == Key.Esc && Cancel();

    /// <summary>What Ctrl+V does in the description, for a click on its hint.</summary>
    private bool Paste()
    {
        _description.SetFocus();
        if (!_attach!.Pressed(Key.V.WithCtrl))
            _description.InvokeCommand(Command.Paste);
        return true;
    }

    /// <summary>The image goes in at the description's cursor, wherever the keyboard was.</summary>
    internal bool AttachFile()
    {
        if (_attaching?.PickFile() is { } path)
        {
            _description.SetFocus();
            _attach!.Attach(path);
        }
        return true;
    }

    private bool Step(int by)
    {
        var count = Enum.GetValues<Rank>().Length;
        _rank.Value = (Rank)(((int)(_rank.Value ?? Rank.None) + by + count) % count);
        _rank.FocusedItem = (int)_rank.Value;
        return true;
    }

    private bool Run(string hint) => hint switch
    {
        AddHint => AddIdea(),
        AttachHint => AttachFile(),
        PasteHint => Paste(),
        _ => Cancel(),
    };

    internal bool AddIdea()
    {
        if (_adding is not null)
            return true;
        if (string.IsNullOrWhiteSpace(_title.Text))
        {
            Say("Nothing to add: the title is needed", Schemes.Error);
            return true;
        }
        if (_attach?.Uploading == true)
        {
            Say("Wait for the image to finish uploading", Schemes.Error);
            return true;
        }
        Say("Adding…", Schemes.Accent);
        var adding = _adding = _filer.Add(Draft);
        if (adding.IsCompleted)
        {
            Settle(adding);
            return true;
        }
        // Polled rather than Invoked, as the reader's comment is: Invoke can wait on a timer's lock.
        App?.AddTimeout(PollEvery, () =>
        {
            if (!adding.IsCompleted)
                return true;
            Settle(adding);
            return false;
        });
        return true;
    }

    /// <summary>An idea that didn't go on keeps what you wrote, and its team once its issue is open.</summary>
    private void Settle(Task<string?> done)
    {
        _adding = null;
        var failure = done.Status == TaskStatus.RanToCompletion
            ? done.Result
            : done.Exception?.GetBaseException().Message ?? "the idea wasn't added";
        _team.Enabled = _filer.Opened is null;
        if (failure is { Length: > 0 })
        {
            Say(failure, Schemes.Error);
            return;
        }
        Added = _filer.Opened;
        RequestStop();
    }

    /// <summary>Waits for an idea being added, and asks before throwing away one you've written.</summary>
    private bool Cancel()
    {
        if (_adding is not null)
            return true;
        if ((!string.IsNullOrWhiteSpace(_title.Text) || !string.IsNullOrWhiteSpace(_description.Text)) && !_confirmDiscard())
            return true;
        RequestStop();
        return true;
    }

    private void Say(string text, Schemes scheme)
    {
        _message.Show(text, scheme);
        SetNeedsLayout();
        SetNeedsDraw();
    }
}
