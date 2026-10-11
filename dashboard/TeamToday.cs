using System.Globalization;
using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>What a team's runs have cost since local midnight, against its daily budget, as
/// <c>a-team board &lt;team&gt; today</c> reports it.</summary>
public sealed record TeamToday(decimal Cost, decimal Budget, bool Reached)
{
    public const string BudgetReached = "budget reached";

    public string Column => $"${Cost.ToString("0.00", CultureInfo.InvariantCulture)}";

    public static TeamToday? Of(Reading reading)
    {
        if (reading.Failure is { Length: > 0 })
            return null;
        try
        {
            using var document = JsonDocument.Parse(reading.Output);
            var root = document.RootElement;
            return new TeamToday(root.GetProperty("cost").GetDecimal(), root.GetProperty("budget").GetDecimal(),
                root.GetProperty("reached").GetBoolean());
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
