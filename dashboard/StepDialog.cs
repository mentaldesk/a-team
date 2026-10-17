using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Fields;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>Asks one <see cref="Step"/>, and runs its work in place on Ctrl+Enter, showing what the work prints.</summary>
public sealed class StepDialog : DashboardDialog
{
    private const string YesId = "step.yes";
    private const string BackId = "step.back";
    private const int Inset = 1;
    private const int OutputLines = 8;

    private readonly Step _step;
    private readonly Label _body;
    private readonly Label _output;
    private readonly List<string> _printed = [];
    private readonly MessageBar _message = new();
    private readonly OptionSelector? _choice;
    private bool _ready;
    private bool _running;
    private bool? _offered;

    public StepDialog(Step step)
        : base(step.Title)
    {
        _step = step;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _body = new Label
        {
            X = Inset,
            Y = 0,
            Width = Dim.Fill(Inset),
            TextFormatter = { WordWrap = true, MultiLine = true },
        };
        _output = new Label
        {
            X = Inset,
            Y = Pos.Bottom(_body) + 1,
            Width = Dim.Fill(Inset),
            Height = Dim.Func(_ => _printed.Count == 0 ? 0 : Math.Min(OutputLines, _printed.Count), this),
            TextFormatter = { WordWrap = false, MultiLine = true },
        };
        Bind(YesId, Key.Enter.WithCtrl, step.Choose ? "Choose" : Capital(step.Yes), () => Yes(),
            () => Done ? _step.ShowOutput : _ready && !_running && _offered == true);
        Bind(BackId, Key.Backspace, "Back", () => Back(), () => !Done && !_running && _offered is not null);
        Register(CancelId, step.Choose ? "Cancel new team" : Capital(step.No), () => No(), () => !Done && !_running && _offered is not null);
        AddButton(ButtonKind.Primary, YesId);
        AddButton(ButtonKind.Secondary, BackId);
        AddButton(ButtonKind.Secondary, CancelId);
        Foot.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - Foot.Rows - _message.Lines), this);
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);
        Add(_body, _output, Foot, _message);
        if (step.Choose)
        {
            _choice = new OptionSelector
            {
                X = Inset,
                Y = Pos.Bottom(_body) + 1,
                Orientation = Orientation.Vertical,
                Labels = [step.Yes, step.No],
                Value = 0,
            }.SelectOnArrows();
            Add(_choice);
            // SelectOnArrows misses the arrows when the options are the dialog's first focus.
            Bind("step.previous", Key.CursorUp, "Previous option", () => Choose(0), () => _choice.HasFocus);
            Bind("step.next", Key.CursorDown, "Next option", () => Choose(1), () => _choice.HasFocus);
            _choice.SetFocus();
        }
        ShowLines(step.Lines);

        if (step.Load is { } load)
        {
            Offer(false);
            load().ContinueWith(read =>
            {
                var (lines, failure) = read.Status == TaskStatus.RanToCompletion ? read.Result : (null, read.Exception?.InnerException?.Message);
                OnUi(() => Loaded(lines, failure));
            }, TaskContinuationOptions.ExecuteSynchronously);
        }
        else
        {
            _ready = true;
            Offer(true);
        }
    }

    /// <summary>Whether it was answered yes and anything that meant running went through.</summary>
    internal bool Done { get; private set; }

    /// <summary>Whether a <see cref="Step.Choose"/> step was left with Esc, cancelling the new team.</summary>
    internal bool Cancelled { get; private set; }

    internal OptionSelector? Choice => _choice;

    internal Label Body => _body;

    internal IReadOnlyList<string> Printed => _printed;

    internal MessageBar Message => _message;

    /// <summary>Whether Backspace left it, for the step before.</summary>
    internal bool WentBack { get; private set; }

    /// <summary>Asks the step, and returns how it was left.</summary>
    public static Answer Show(IApplication app, Step step)
    {
        using var dialog = new StepDialog(step);
        DialogHost.Run(app, dialog);
        return dialog.WentBack ? Answer.Back : dialog.Cancelled ? Answer.Cancelled : dialog.Done ? Answer.Done : Answer.Skipped;
    }

    internal bool Yes()
    {
        if (Done)
            return Close();
        if (!_ready || _running)
            return true;
        if (_choice is not null && _choice.Value != 0)
            return Close();
        if (_step.Work is not { } work)
        {
            Done = true;
            return Close();
        }
        _running = true;
        _printed.Clear();
        ShowOutput();
        Offer(null);
        Say("Working…", Schemes.Base);
        work(line => OnUi(() => Print(line))).ContinueWith(ran =>
        {
            var failure = ran.Status == TaskStatus.RanToCompletion ? ran.Result : ran.Exception?.InnerException?.Message;
            OnUi(() => Finished(failure));
        }, TaskContinuationOptions.ExecuteSynchronously);
        return true;
    }

    internal bool No()
    {
        if (_running)
            return true;
        Cancelled = _choice is not null;
        return Close();
    }

    internal bool Back()
    {
        if (_running)
            return true;
        WentBack = true;
        return Close();
    }

    private void Loaded(IReadOnlyList<string>? lines, string? failure)
    {
        if (lines is not null)
            ShowLines(lines);
        if (failure is not null)
        {
            ShowLines([]);
            Say(failure, Schemes.Error);
        }
        else
        {
            _message.Clear();
        }
        _ready = failure is null;
        Offer(_ready);
    }

    private void Print(string line)
    {
        _printed.Add(line.TrimEnd());
        ShowOutput();
    }

    private void Finished(string? failure)
    {
        _running = false;
        if (failure is not null)
        {
            Say(failure, Schemes.Error);
            Offer(true);
            return;
        }
        Done = true;
        if (!_step.ShowOutput)
        {
            Close();
            return;
        }
        _message.Clear();
        Rename(YesId, "Continue");
        ShowButtons();
        Refresh();
    }

    private void ShowLines(IReadOnlyList<string> lines)
    {
        _body.Text = string.Join('\n', lines);
        _body.Height = Math.Max(1, lines.Count);
        Refresh();
    }

    private void ShowOutput()
    {
        _output.Text = string.Join('\n', _printed.TakeLast(OutputLines));
        Refresh();
    }

    /// <summary>Yes and no where <paramref name="yes"/> is true, only no where it's false, and nothing while working.</summary>
    private void Offer(bool? yes)
    {
        _offered = _choice is not null ? true : yes;
        ShowButtons();
        Refresh();
    }

    private void Choose(int option)
    {
        _choice!.Value = option;
        _choice.SubViews.OfType<CheckBox>().ElementAt(option).SetFocus();
    }

    private static string Capital(string label) => label.Length == 0 ? label : char.ToUpperInvariant(label[0]) + label[1..];

    private void OnUi(Action action)
    {
        if (App is { } app)
            app.Invoke(action);
        else
            action();
    }

    private void Say(string message, Schemes scheme)
    {
        _message.Show(message, scheme);
        Refresh();
    }

    private void Refresh()
    {
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private bool Close()
    {
        RequestStop();
        return true;
    }
}
