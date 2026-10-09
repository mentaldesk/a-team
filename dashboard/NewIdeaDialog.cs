using System.Collections.ObjectModel;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

// Terminal.Gui 2.5 obsoletes TextView in favour of Terminal.Gui.Editor; moving to it is a change of its own.
#pragma warning disable CS0618

namespace ATeam.Dashboard;

/// <summary>Adds an idea to a team's board without leaving the app: the team, a title, a few lines and a rank.</summary>
public sealed class NewIdeaDialog : Dialog
{
    internal const string RankCaption = "None leaves it in Triage to rank later.";
    private const string AddHint = "add";
    private const string CancelHint = "cancel";
    private const int Inset = 1;
    private const int FieldX = 11;
    private const int DescriptionLines = 6;
    private const int Wide = 76;
    private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(100);

    private readonly IdeaFiler _filer;
    private readonly Func<bool> _confirmDiscard;
    private readonly DropDownList _team;
    private readonly TextField _title;
    private readonly TextView _description;
    private readonly OptionSelector<Rank> _rank;
    private readonly StatusBar _hints = new();
    private readonly MessageBar _message = new();
    private Task<string?>? _adding;

    public NewIdeaDialog(IReadOnlyList<string> teams, string team, IdeaFiler filer, Func<bool>? confirmDiscard = null)
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
        _description = new TextView
        {
            X = Inset,
            Y = 5,
            Width = Dim.Fill(Inset),
            Height = DescriptionLines,
            WordWrap = true,
            TabKeyAddsTab = false,
            BorderStyle = LineStyle.Rounded,
        };
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
        _hints.Show("", [new HintedCommand(AddHint, "Ctrl+Enter add"), new HintedCommand(CancelHint, "Esc cancel")], Run);
        _title.SetFocus();
    }

    /// <summary>The idea once it's on the board, or null where the dialog was cancelled.</summary>
    internal (string Team, int Number)? Added { get; private set; }

    internal DropDownList Team => _team;

    internal TextField IdeaTitle => _title;

    internal TextView Description => _description;

    internal OptionSelector<Rank> Ranks => _rank;

    internal StatusBar Hints => _hints;

    internal MessageBar Message => _message;

    /// <summary>The idea as the dialog holds it now.</summary>
    internal IdeaDraft Draft => new(_team.Text, _title.Text, _description.Text, _rank.Value ?? Rank.None);

    /// <summary>Opens the dialog, and returns the idea it added, or null where it was cancelled.</summary>
    public static (string Team, int Number)? Show(IApplication app, IReadOnlyList<string> teams, string team, IdeaFiler filer)
    {
        using var dialog = new NewIdeaDialog(teams, team, filer, () => DiscardDialog.Show(app, DiscardDialog.Idea));
        app.Run(dialog);
        return dialog.Added;
    }

    /// <summary>Enter picks a team or a rank, and never adds: that's Ctrl+Enter, so a stray Enter can't.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => true;

    protected override bool OnKeyDown(Key key) => Pressed(key) || base.OnKeyDown(key);

    private bool Pressed(Key key) =>
        key == Key.Enter.WithCtrl ? AddIdea()
        : key == Key.Esc && Cancel();

    private bool Step(int by)
    {
        var count = Enum.GetValues<Rank>().Length;
        _rank.Value = (Rank)(((int)(_rank.Value ?? Rank.None) + by + count) % count);
        _rank.FocusedItem = (int)_rank.Value;
        return true;
    }

    private bool Run(string hint) => hint == AddHint ? AddIdea() : Cancel();

    internal bool AddIdea()
    {
        if (_adding is not null)
            return true;
        if (string.IsNullOrWhiteSpace(_title.Text))
        {
            Say("Nothing to add: the title is needed", Schemes.Error);
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
