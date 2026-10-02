using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>Asks before something that can't be taken back, saying what it does, with Enter to go ahead and Esc to
/// leave it.</summary>
public class ConfirmDialog : Dialog
{
    private const string CancelHint = "Esc cancel";
    private const string Separator = " · ";
    private const int Inset = 1;
    private const int Wide = 78;

    private readonly Label _text;

    public ConfirmDialog(string title, string text, string verb)
    {
        Title = title;
        Width = Dim.Func(_ => OuterWidth(), this);
        Height = Dim.Func(_ => Lines(text).Count + 2 + GetAdornmentsThickness().Vertical, this);

        _text = new Label
        {
            X = Inset,
            Y = 0,
            Width = Dim.Fill(Inset),
            Height = Dim.Func(_ => Lines(text).Count, this),
            Text = text,
            TextFormatter = { WordWrap = false, MultiLine = true },
        };
        SubViewLayout += (_, _) =>
        {
            var wrapped = string.Join('\n', Lines(text));
            if (_text.Text != wrapped)
                _text.Text = wrapped;
        };
        var confirm = Hint($"Enter {verb}", Inset, Pos.Bottom(_text) + 1, confirmed: true);
        var separator = new Label { Text = Separator, X = Pos.Right(confirm), Y = confirm.Y, CanFocus = false };
        var cancel = Hint(CancelHint, Pos.Right(separator), confirm.Y, confirmed: false);
        Add(_text, confirm, separator, cancel);
    }

    internal Label Body => _text;

    internal bool Confirmed { get; private set; }

    internal IReadOnlyList<Button> Hints => [.. SubViews.OfType<Button>()];

    protected override bool OnAccepting(CommandEventArgs args) => Close(confirmed: true);

    protected override bool OnKeyDown(Key key) => key == Key.Esc ? Close(confirmed: false) : base.OnKeyDown(key);

    protected static bool Ask(IApplication app, ConfirmDialog dialog)
    {
        app.Run(dialog);
        return dialog.Confirmed;
    }

    /// <summary>The text broken at spaces into lines no wider than <paramref name="width"/>.</summary>
    internal static List<string> Wrap(string text, int width)
    {
        List<string> lines = [];
        var line = "";
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                lines.Add(line);
                line = word;
            }
            else
                line = line.Length == 0 ? word : $"{line} {word}";
        }
        lines.Add(line);
        return lines;
    }

    private Button Hint(string text, Pos x, Pos y, bool confirmed)
    {
        var hint = new Button
        {
            Text = text,
            X = x,
            Y = y,
            NoDecorations = true,
            NoPadding = true,
            ShadowStyle = ShadowStyles.None,
            HotKeySpecifier = (Rune)0xffff,
            CanFocus = false,
        };
        hint.Accepting += (_, args) => args.Handled = Close(confirmed);
        return hint;
    }

    private int OuterWidth() => Fits(Wide + GetAdornmentsThickness().Horizontal, SuperView?.Viewport.Width);

    /// <summary>Each line of the text wrapped on its own, so a blank line between paragraphs stays.</summary>
    private List<string> Lines(string text)
    {
        var width = Math.Max(1, OuterWidth() - GetAdornmentsThickness().Horizontal - 2 * Inset);
        return [.. text.Split('\n').SelectMany(paragraph => Wrap(paragraph, width))];
    }

    private static int Fits(int wanted, int? available) => available is { } room ? Math.Min(wanted, room) : wanted;

    private bool Close(bool confirmed)
    {
        Confirmed = confirmed;
        RequestStop();
        return true;
    }
}
