using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>Asks before a team is removed, saying what goes and what doesn't.</summary>
public sealed class RemoveTeamDialog : Dialog
{
    private const string RemoveHint = "Enter remove";
    private const string CancelHint = "Esc cancel";
    private const string Separator = " · ";
    private const int Inset = 1;
    private const int Wide = 78;

    private readonly Label _text;

    public RemoveTeamDialog(string team, string repo, string kept)
    {
        Title = $"Remove {team}?";
        Width = Dim.Func(_ => OuterWidth(), this);
        var text = Says(team, repo, kept);
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
        var remove = Hint(RemoveHint, Inset, Pos.Bottom(_text) + 1, confirmed: true);
        var separator = new Label { Text = Separator, X = Pos.Right(remove), Y = remove.Y, CanFocus = false };
        var cancel = Hint(CancelHint, Pos.Right(separator), remove.Y, confirmed: false);
        Add(_text, remove, separator, cancel);
    }

    internal Label Body => _text;

    internal bool Confirmed { get; private set; }

    internal static string Says(string team, string repo, string kept)
    {
        var older = kept == $"{team}.json.removed" ? "" : $" An older {team}.json.removed is left as it is.";
        var what = repo.Length > 0 ? repo : "Its repo";
        return $"a-team forgets this team. {what}, its board and everything the team has built are untouched, " +
               $"and the config is kept as {kept} if you want it back.{older}";
    }

    protected override bool OnAccepting(CommandEventArgs args) => Close(confirmed: true);

    protected override bool OnKeyDown(Key key) => key == Key.Esc ? Close(confirmed: false) : base.OnKeyDown(key);

    public static bool Show(IApplication app, string team, string repo, string kept)
    {
        using var dialog = new RemoveTeamDialog(team, repo, kept);
        app.Run(dialog);
        return dialog.Confirmed;
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

    private List<string> Lines(string text) =>
        Wrap(text, Math.Max(1, OuterWidth() - GetAdornmentsThickness().Horizontal - 2 * Inset));

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

    private static int Fits(int wanted, int? available) => available is { } room ? Math.Min(wanted, room) : wanted;

    private bool Close(bool confirmed)
    {
        Confirmed = confirmed;
        RequestStop();
        return true;
    }
}
