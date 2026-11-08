using MentalDesk.Tui.Settings;

namespace ATeam.Dashboard;

/// <summary>Settings' Dashboard page: whether sessions and the Assistant show every tool call, and which icons the app draws.</summary>
public sealed class DashboardPage : SettingsPage
{
    internal const string Name = "Dashboard";
    internal const string ToolCallsText = "Show tool calls in full";
    internal const string LegendText = "run  idle  paused  ok  error  here  tool";
    private const string ThisTerminal = "this terminal: ";
    private const int Indent = 2;
    private const int IconsHeadingRow = 2;
    private const int IconStylesRow = IconsHeadingRow + 1;

    private static readonly (IconStyle Style, string Name)[] IconChoices =
        [(IconStyle.Auto, "Automatic"), (IconStyle.NerdFont, "Nerd Font"), (IconStyle.Unicode, "Unicode")];

    private readonly DashboardSettings _settings;
    private readonly IconSetting _icons;
    private readonly Label _toolCallsDefault;
    private readonly Label _iconsDefault;
    private bool _keptToolCalls;
    private IconStyle _keptIcons;

    public DashboardPage(DashboardSettings settings, IconSetting icons, IconStyle auto, Action redraw) : base(Name)
    {
        _settings = settings;
        _icons = icons;
        _keptToolCalls = settings.ReadExpandToolCalls();
        _keptIcons = icons.Current;
        ToolCalls = new CheckBox
        {
            Text = ToolCallsText,
            Value = _keptToolCalls ? CheckState.Checked : CheckState.UnChecked,
        };
        ToolCalls.ValueChanged += (_, _) =>
        {
            Show();
            OnChanged();
        };
        _toolCallsDefault = Dimmed(new Label { X = Pos.Right(ToolCalls) + 2 });
        IconStyles = new OptionSelector
        {
            X = Indent,
            Y = IconStylesRow,
            Orientation = Orientation.Vertical,
            Labels = [.. IconChoices.Select(choice => IconRow(choice, auto))],
            Value = Index(icons.Current),
        };
        IconStyles.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } index && index >= 0 && index < IconChoices.Length)
            {
                icons.Preview(IconChoices[index].Style);
                redraw();
            }
            Show();
            OnChanged();
        };
        _iconsDefault = Dimmed(new Label { X = Pos.Right(IconStyles) + 1 });
        Legend = new Label { X = Indent, Y = IconStylesRow + IconChoices.Length + 1, Text = LegendText };
        Add(ToolCalls, _toolCallsDefault, new Label { Y = IconsHeadingRow, Text = "Icons:" }, IconStyles, _iconsDefault, Legend);
        Show();
    }

    public CheckBox ToolCalls { get; }

    public OptionSelector IconStyles { get; }

    public Label Legend { get; }

    internal bool ExpandToolCalls => ToolCalls.Value == CheckState.Checked;

    internal string ToolCallsDefault => _toolCallsDefault.Text;

    internal string IconsDefault => _iconsDefault.Text;

    public override bool IsChanged => ExpandToolCalls != _keptToolCalls || _icons.Current != _keptIcons;

    public override void Keep()
    {
        _settings.WriteExpandToolCalls(ExpandToolCalls);
        _icons.Keep();
        _keptToolCalls = ExpandToolCalls;
        _keptIcons = _icons.Current;
    }

    public override void Revert()
    {
        ToolCalls.Value = _keptToolCalls ? CheckState.Checked : CheckState.UnChecked;
        IconStyles.Value = Index(_keptIcons);
    }

    private static int Index(IconStyle style) => Math.Max(0, Array.FindIndex(IconChoices, choice => choice.Style == style));

    private void Show()
    {
        _toolCallsDefault.Text = ExpandToolCalls ? DefaultNote("off") : "";
        _iconsDefault.Text = _icons.Current == IconStyle.Auto ? "" : DefaultNote(IconChoices[0].Name);
        _iconsDefault.Y = IconStylesRow + Index(_icons.Current);
    }

    /// <summary>A style's row: its own glyphs after its name, so you pick the row that isn't boxes, and for
    /// Automatic what it decided for the terminal you're in, so the row answers rather than promises.</summary>
    private static string IconRow((IconStyle Style, string Name) choice, IconStyle auto)
    {
        var name = choice.Name.PadRight(IconChoices.Max(other => other.Name.Length));
        return choice.Style == IconStyle.Auto
            ? $"{name}  {ThisTerminal}{IconChoices.First(other => other.Style == auto).Name}"
            : $"{name}  {Icons.Sample(choice.Style)}";
    }
}
