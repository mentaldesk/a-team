using System.Globalization;
using MentalDesk.Tui;

namespace ATeam.Dashboard;

/// <summary>A stretch of your time: on one card, or in Work between cards where <paramref name="Item"/> is null, spent on
/// <paramref name="Doing"/>.</summary>
public sealed record Visit(string Team, int? Item, string Gate, DateTimeOffset At, TimeSpan Spent, IReadOnlyList<string> Did, string Doing = "")
{
    /// <summary>What you did on the card, as its History line says it after the time, or what the time between cards went on.</summary>
    public string What => Item is null ? Doing : Did.Count == 0 ? "read" : string.Join(", ", Did.Distinct());

    public string[] Arguments =>
    [
        "board", Team, "spent", "you", Item?.ToString(CultureInfo.InvariantCulture) ?? "-",
        At.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        ((int)Spent.TotalSeconds).ToString(CultureInfo.InvariantCulture), Gate, What,
    ];

    public static string Duration(TimeSpan spent) => Times.Took(spent);
}

/// <summary>Times what you spend in Work: each card from opening its reader to closing it, or for the whole of a try
/// started from it, and the time between cards as <see cref="Other"/> for the selected lane's team, split by what it went
/// on. With no key for <see cref="IdleOnCard"/> on a card or in a report, or <see cref="IdleBetweenCards"/> elsewhere between
/// cards, or once you've gone to GitHub, time stops counting until the next one.</summary>
public sealed class Attention(TimeProvider clock, Action<Visit> record)
{
    public const string Other = "Other";

    public const string Finding = "Finding a card";

    public const string Reports = "Reports";

    public const string Elsewhere = "Settings & other";

    public static readonly string[] Doings = [Finding, Reports, Elsewhere];

    public static readonly TimeSpan IdleOnCard = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan IdleBetweenCards = TimeSpan.FromSeconds(15);

    /// <summary>The dashboard can't see keys inside a try, so a try counts in full up to this.</summary>
    public static readonly TimeSpan LongestTry = TimeSpan.FromMinutes(30);

    private readonly Lock _lock = new();
    private Stretch? _stretch;
    private DateTimeOffset _last = clock.GetUtcNow();
    private DateTimeOffset? _handed;
    private bool _away;

    public void Key()
    {
        lock (_lock)
        {
            Accrue(clock.GetUtcNow());
            _away = false;
        }
    }

    /// <summary>You've gone to GitHub: nothing counts until your next key.</summary>
    public void Away()
    {
        lock (_lock)
        {
            Accrue(clock.GetUtcNow());
            _away = true;
        }
    }

    /// <summary>Work with no reader open, on <paramref name="team"/>'s lane, spent on <paramref name="doing"/>; null where
    /// time doesn't count.</summary>
    public void Browsing(string? team, string doing = Finding)
    {
        lock (_lock)
        {
            if (_stretch is { Item: not null } || _stretch?.Team == team && _stretch?.Doing == doing)
                return;
            Switch(team is null ? null : new Stretch(team, null, Other, clock.GetUtcNow(), doing));
        }
    }

    /// <summary>The reader is open on <paramref name="item"/>; opening it again, back from a try, carries on the visit.</summary>
    public void Open(WaitingItem item, string gate)
    {
        lock (_lock)
        {
            if (_stretch is { } open && open.Item == item.Number && open.Team == item.Team)
                return;
            Switch(new Stretch(item.Team, item.Number, gate, clock.GetUtcNow()));
        }
    }

    public void Did(string what)
    {
        lock (_lock)
            if (_stretch is { Item: not null } visit)
                visit.Did.Add(what);
    }

    public void Close()
    {
        lock (_lock)
            if (_stretch is { Item: not null })
                Switch(null);
    }

    /// <summary>The terminal goes to a try; the visit waits for it.</summary>
    public void HandOver()
    {
        lock (_lock)
        {
            Accrue(clock.GetUtcNow());
            _handed = _last;
        }
    }

    public void TakeBack()
    {
        lock (_lock)
        {
            if (_handed is not { } handed)
                return;
            _handed = null;
            var now = clock.GetUtcNow();
            if (_stretch is { } stretch)
                stretch.Spent += Min(now - handed, LongestTry);
            _last = now;
        }
    }

    public void Stop()
    {
        lock (_lock)
            Switch(null);
    }

    private void Accrue(DateTimeOffset now)
    {
        if (_stretch is { } stretch && !_away)
            stretch.Spent += Min(now - _last, stretch.Item is null && stretch.Doing != Reports ? IdleBetweenCards : IdleOnCard);
        _last = now;
    }

    private void Switch(Stretch? next)
    {
        Accrue(clock.GetUtcNow());
        if (_stretch is { } ended && ended.Spent >= TimeSpan.FromSeconds(1))
            record(new Visit(ended.Team, ended.Item, ended.Gate, ended.At, ended.Spent, [.. ended.Did], ended.Doing));
        _stretch = next;
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private sealed class Stretch(string team, int? item, string gate, DateTimeOffset at, string doing = "")
    {
        public string Doing => doing;
        public string Team => team;
        public int? Item => item;
        public string Gate => gate;
        public DateTimeOffset At => at;
        public TimeSpan Spent { get; set; }
        public List<string> Did { get; } = [];
    }
}
