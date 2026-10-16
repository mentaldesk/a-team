using System.Collections.ObjectModel;
using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Fields;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>Adds an idea to a team's board without leaving the app: the team, a title, a few lines and a rank.</summary>
public sealed class NewIdeaDialog : DashboardDialog
{
    internal const string RankCaption = "None leaves it in Triage to rank later.";
    internal const string Name = "New idea";
    private const string AddId = "idea.add";
    private const string AttachId = "idea.attach";
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
    private readonly MessageBar _message = new();
    private readonly Attaching? _attaching;
    private readonly ImageAttach? _attach;
    private Task<string?>? _adding;

    /// <param name="description">What the Description starts with, which Esc asks before throwing away.</param>
    public NewIdeaDialog(
        IReadOnlyList<string> teams, string team, IdeaFiler filer, Func<bool>? confirmDiscard = null, string description = "",
        Attaching? attaching = null)
        : base(Name)
    {
        _filer = filer;
        _confirmDiscard = confirmDiscard ?? (() => true);
        Width = Dim.Func(_ => Math.Min(Wide, SuperView?.Viewport.Width ?? Wide), this);
        Height = Dim.Func(_ => DescriptionLines + 9 + Foot.Rows + _message.Lines + GetAdornmentsThickness().Vertical, this);

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
        _description = new MultiLineField { X = Inset, Y = 5, Width = Dim.Fill(Inset), Height = DescriptionLines, Text = description };
        var rankY = 5 + DescriptionLines + 1;
        Add(new Label { Text = "Priority", X = Inset, Y = rankY });
        _rank = new OptionSelector<Rank>
        {
            X = FieldX,
            Y = rankY,
            Orientation = Orientation.Horizontal,
            Value = Rank.None,
        }.SelectOnArrows();
        Add(new Label { Text = RankCaption, X = FieldX, Y = rankY + 1 });
        _attaching = attaching;
        Bind(AddId, Key.Enter.WithCtrl, "Add", () => AddIdea());
        if (attaching is not null)
        {
            _attach = new ImageAttach(_description, file => attaching.Upload(_team.Text, file), attaching.Clipboard, Say);
            _description.KeyDown += (_, key) => key.Handled = key.Handled || _attach.Pressed(key);
            Bind(AttachId, attaching.Key, "Attach file", () => AttachFile());
        }
        Also("Ctrl+V", "Paste");
        Register(CancelId, "Cancel", () => Dismiss());
        Also("←/→", "Priority");
        Also("Tab/Shift+Tab", "Next / previous");
        AddButton(ButtonKind.Primary, AddId);
        if (attaching is not null)
            AddButton(ButtonKind.Secondary, AttachId);
        AddButton(ButtonKind.Secondary, CancelId);
        Foot.Y = rankY + 3;
        _message.Y = Pos.Bottom(Foot);
        Add(_team, _title, _description, _rank, Foot, _message);
        _title.TextChanged += (_, _) => StyleCancel();
        _description.ContentChanged += (_, _) => StyleCancel();
        StyleCancel();
        _title.SetFocus();
    }

    /// <summary>The idea once it's on the board, or null where the dialog was cancelled.</summary>
    internal (string Team, int Number)? Added { get; private set; }

    internal DropDownList Team => _team;

    internal TextField IdeaTitle => _title;

    internal MultiLineField Description => _description;

    internal OptionSelector<Rank> Ranks => _rank;

    internal MessageBar Message => _message;

    /// <summary>The idea as the dialog holds it now.</summary>
    internal IdeaDraft Draft => new(_team.Text, _title.Text, _description.Text, _rank.Value ?? Rank.None);

    /// <summary>Opens the dialog, and returns the idea it added, or null where it was cancelled.</summary>
    public static (string Team, int Number)? Show(
        IApplication app, IReadOnlyList<string> teams, string team, IdeaFiler filer, string description = "",
        Attaching? attaching = null)
    {
        using var dialog = new NewIdeaDialog(
            teams, team, filer, () => DiscardDialog.Show(app, DiscardDialog.Idea), description, attaching);
        DialogHost.Run(app, dialog);
        return dialog.Added;
    }

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
    private bool Dismiss()
    {
        if (_adding is not null)
            return true;
        if (Written && !_confirmDiscard())
            return true;
        RequestStop();
        return true;
    }

    private bool Written => !string.IsNullOrWhiteSpace(_title.Text) || !string.IsNullOrWhiteSpace(_description.Text);

    private void StyleCancel() => Restyle(CancelId, Written ? ButtonKind.Danger : ButtonKind.Secondary);

    private void Say(string text, Schemes scheme)
    {
        _message.Show(text, scheme);
        SetNeedsLayout();
        SetNeedsDraw();
    }
}
