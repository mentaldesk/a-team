using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>Asks one <see cref="Step"/>, and runs its work in place on Enter, showing what the work prints.</summary>
public sealed class StepDialog : Dialog
{
    private const string YesHint = "yes";
    private const string NoHint = "no";
    private const string ContinueHint = "continue";
    private const string ChooseHint = "choose";
    private const int Inset = 1;
    private const int OutputLines = 8;

    private readonly Step _step;
    private readonly Label _body;
    private readonly Label _output;
    private readonly List<string> _printed = [];
    private readonly StatusBar _hints = new();
    private readonly MessageBar _message = new();
    private readonly OptionSelector? _choice;
    private bool _ready;
    private bool _running;

    public StepDialog(Step step)
    {
        _step = step;
        Title = step.Title;
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
        _hints.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - 1 - _message.Lines), this);
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);
        Add(_body, _output, _hints, _message);
        if (step.Choose)
        {
            _choice = new OptionSelector
            {
                X = Inset,
                Y = Pos.Bottom(_body) + 1,
                Orientation = Orientation.Vertical,
                TabBehavior = TabBehavior.NoStop,
                Labels = [step.Yes, step.No],
                Value = 0,
            };
            Add(_choice);
            _choice.SetFocus();
        }
        ShowLines(step.Lines);

        if (step.Load is { } load)
        {
            ShowHints(false);
            load().ContinueWith(read =>
            {
                var (lines, failure) = read.Status == TaskStatus.RanToCompletion ? read.Result : (null, read.Exception?.InnerException?.Message);
                OnUi(() => Loaded(lines, failure));
            }, TaskContinuationOptions.ExecuteSynchronously);
        }
        else
        {
            _ready = true;
            ShowHints(true);
        }
    }

    /// <summary>Whether it was answered yes and anything that meant running went through.</summary>
    internal bool Done { get; private set; }

    /// <summary>Whether a <see cref="Step.Choose"/> step was left with Esc, cancelling the new team.</summary>
    internal bool Cancelled { get; private set; }

    internal OptionSelector? Choice => _choice;

    internal Label Body => _body;

    internal IReadOnlyList<string> Printed => _printed;

    internal StatusBar Hints => _hints;

    internal MessageBar Message => _message;

    /// <summary>Asks the step, and returns whether it was done, or null where it was cancelled.</summary>
    public static bool? Show(IApplication app, Step step)
    {
        using var dialog = new StepDialog(step);
        app.Run(dialog);
        return dialog.Cancelled ? null : dialog.Done;
    }

    /// <summary>Enter reaches a Dialog as Accept, and never as a key.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => Yes();

    protected override bool OnKeyDown(Key key) => key == Key.Esc ? No() : base.OnKeyDown(key);

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
        ShowHints(null);
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
        ShowHints(_ready);
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
            ShowHints(true);
            return;
        }
        Done = true;
        if (!_step.ShowOutput)
        {
            Close();
            return;
        }
        _message.Clear();
        _hints.Show("", [new HintedCommand(ContinueHint, "Enter continue")], _ => Close());
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
    private void ShowHints(bool? yes)
    {
        List<HintedCommand> hints = [];
        if (_choice is not null)
        {
            _hints.Show("", [new HintedCommand(ChooseHint, "Enter choose"), new HintedCommand(NoHint, "Esc cancel new team")],
                hint => hint == ChooseHint ? Yes() : No());
            Refresh();
            return;
        }
        if (yes == true)
            hints.Add(new HintedCommand(YesHint, $"Enter {_step.Yes}"));
        if (yes is not null)
            hints.Add(new HintedCommand(NoHint, $"Esc {_step.No}"));
        _hints.Show("", hints, hint => hint == YesHint ? Yes() : No());
        Refresh();
    }

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
