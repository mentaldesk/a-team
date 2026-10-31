using System.Collections.ObjectModel;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>Overseer's panel for a selected lane heading: a line per role saying whether it's running or idle, when it
/// last ran and when the dispatcher next checks, with what's wrong where the team can't run.</summary>
public sealed class TeamPanel : FrameView
{
    private const int NameWidth = 14;
    private static readonly string BaseScheme = SchemeManager.SchemesToSchemeName(Schemes.Base)!;
    private static readonly string ErrorScheme = SchemeManager.SchemesToSchemeName(Schemes.Error)!;

    private readonly Label _problem = new() { X = 0, Y = 0, Width = Dim.Fill(), Height = 0, CanFocus = false, SchemeName = ErrorScheme };
    private readonly ListView _roles = new() { X = 0, Width = Dim.Fill(), Height = Dim.Fill(1), CanFocus = false };
    private readonly Label _hint = new() { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), CanFocus = false, SchemeName = LogSchemes.Dimmed };
    private readonly ObservableCollection<string> _lines = [];
    private IReadOnlyList<string> _order = [];

    public TeamPanel()
    {
        CanFocus = true;
        SchemeName = BaseScheme;
        _roles.Y = Pos.Bottom(_problem);
        _roles.SetSource(_lines);
        Add(_problem, _roles, _hint);
    }

    internal string Team => Title;

    /// <summary>The role the keys act on.</summary>
    internal string? Role => _roles.Value is { } at && at < _order.Count ? _order[at] : null;

    internal IReadOnlyList<string> Lines => _lines;

    internal string Problem => _problem.Text;

    internal string Hint => _hint.Text;

    /// <summary>Rows it needs at the foot of the lanes: its frame, the problem if any, a line per role and the hint.</summary>
    internal int Rows => 2 + (_problem.Text.Length > 0 ? 1 : 0) + _lines.Count + 1;

    /// <summary>Shows <paramref name="team"/>'s roles, each with its icon and what it's doing, keeping the role chosen
    /// while it's the same team.</summary>
    public void Show(string team, IReadOnlyList<(string Role, string Icon, string Line)> roles, string? problem)
    {
        var chosen = Title == team ? Role : null;
        if (Title != team)
            Title = team;
        _order = [.. roles.Select(role => role.Role)];
        var lines = roles.Select(role => $"{role.Icon}{OverseerView.RoleName(role.Role).PadRight(NameWidth)}{role.Line}").ToList();
        if (!_lines.SequenceEqual(lines))
        {
            _lines.Clear();
            foreach (var line in lines)
                _lines.Add(line);
        }
        var at = chosen is null ? 0 : Math.Max(0, _order.ToList().IndexOf(chosen));
        if (_roles.Value != (_order.Count == 0 ? null : at))
            _roles.Value = _order.Count == 0 ? null : at;
        var text = problem ?? "";
        if (_problem.Text != text)
        {
            _problem.Text = text;
            _problem.Height = text.Length > 0 ? 1 : 0;
        }
    }

    /// <summary>The keys that work on the chosen role.</summary>
    public void ShowHint(string hint)
    {
        if (_hint.Text != hint)
            _hint.Text = hint;
    }

    /// <summary>Up and down choose the role the keys act on, stopping at either end.</summary>
    public void MoveRole(int step)
    {
        if (_order.Count == 0)
            return;
        _roles.Value = Math.Clamp((_roles.Value ?? 0) + step, 0, _order.Count - 1);
    }

    /// <summary>Chooses <paramref name="role"/>, where the team has it.</summary>
    public void Choose(string role)
    {
        if (_order.ToList().IndexOf(role) is >= 0 and var at)
            _roles.Value = at;
    }
}
