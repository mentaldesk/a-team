using MentalDesk.Tui.Chrome;
using MentalDesk.Tui.Keys;
using MentalDesk.Tui.Shell;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>What the window can be given beyond what it can't run without. Keep it in alphabetical order, so two features adding one each don't meet on a line.</summary>
public sealed record DashboardOptions
{
    public Func<WaitingItem, IReadOnlyList<string>, string?>? AskFix { get; init; }
    public Func<WaitingItem, string?>? AskHold { get; init; }
    public AssistantSetup? Assistant { get; init; }
    public Func<Key, Attaching>? Attaching { get; init; }
    public Attention? Attention { get; init; }
    public TeamChecks? Checks { get; init; }
    public IClipboard? Clipboard { get; init; }
    public TimeProvider? Clock { get; init; }
    public Func<WaitingItem, bool>? ConfirmAccept { get; init; }
    public Func<WaitingItem, bool>? ConfirmApprove { get; init; }
    public Func<Write?, IReadOnlyList<Write>, bool>? ConfirmQuit { get; init; }
    public Func<RunTask, bool>? ConfirmStop { get; init; }
    public Action<Handover>? HandOver { get; init; }
    public MacKeys? MacKeys { get; init; }
    public Func<IReadOnlyList<string>, string, string, Attaching?, (string Team, int Number)?>? NewIdea { get; init; }
    public PaneWidths? PaneWidths { get; init; }
    public DispatchPass? Pass { get; init; }
    public Func<string, Task<Reading>>? ReadBoard { get; init; }
    public Func<WaitingItem, Task<Reading>>? ReadConversation { get; init; }
    public Func<WaitingItem, Task<Reading>>? ReadCriteria { get; init; }
    public Func<WaitingItem, Task<Reading>>? ReadDocument { get; init; }
    public Func<WaitingItem, Task<Reading>>? ReadHistory { get; init; }
    public Func<string, DateTimeOffset, Task<Reading>>? ReadMoves { get; init; }
    public Func<string, Task<Reading>>? ReadTrend { get; init; }
    public Handover? Resume { get; init; }
    public Action<string>? ShowDecisions { get; init; }
    public Action<string>? ShowGuide { get; init; }
    public Action<IReadOnlyList<string>, Action<string, SpendingItem>>? ShowSpending { get; init; }
    public Action<IReadOnlyList<(string Team, int? Waiting)>>? ShowTrends { get; init; }
    public TeamStart? Start { get; init; }
    public Func<string, Task<Reading>>? SyncBoard { get; init; }
    public TerminalTitle? TerminalTitle { get; init; }
    public WhatsNew? WhatsNew { get; init; }
    public WriteQueue? Writes { get; init; }
}
