using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

public sealed record BudgetCategory(
    string Name,
    IReadOnlyList<BudgetLine> Lines,
    decimal Subtotal,
    decimal ShareOfTotal);

public sealed record BudgetSummary(
    decimal Total,
    int Headcount,
    decimal PerPerson,
    decimal ChargedPerPerson,
    decimal ChargedTotal,
    /// <summary>Charged minus budgeted. Positive means the price covers the plan.</summary>
    decimal Variance,
    decimal Collected,
    decimal Outstanding,
    int PaidCount);

/// <summary>
/// The arithmetic behind the budget page. Pure functions over TripData, so the numbers can be
/// tested without a browser and the page is left to do nothing but render them.
/// </summary>
public static class BudgetService
{
    // ------------------------------------------------------------ calculating

    public static decimal Total(TripData trip) =>
        Money.Round(trip.Budget.Sum(l => l.Unit * l.Qty));

    /// <summary>Lines grouped by category, biggest spend first, with each category's share.</summary>
    public static IReadOnlyList<BudgetCategory> ByCategory(TripData trip)
    {
        var total = Total(trip);

        return trip.Budget
            .GroupBy(l => string.IsNullOrWhiteSpace(l.Category) ? "Uncategorised" : l.Category)
            .Select(g =>
            {
                var subtotal = Money.Round(g.Sum(l => l.Unit * l.Qty));
                var share = total == 0 ? 0 : Money.Round(subtotal / total * 100);
                return new BudgetCategory(g.Key, g.OrderBy(l => l.Item).ToList(), subtotal, share);
            })
            .OrderByDescending(c => c.Subtotal)
            .ToList();
    }

    public static BudgetSummary Summarize(TripData trip)
    {
        var total = Total(trip);
        var headcount = Math.Max(1, trip.Roster.Count);

        var charged = trip.Trip.PricePerPerson;
        var chargedTotal = Money.Round(charged * headcount);

        var collected = Money.Round(trip.Roster.Where(p => p.Paid).Sum(p => p.Amount));
        var paidCount = trip.Roster.Count(p => p.Paid);

        return new BudgetSummary(
            Total: total,
            Headcount: headcount,
            PerPerson: Money.Round(total / headcount),
            ChargedPerPerson: charged,
            ChargedTotal: chargedTotal,
            Variance: Money.Round(chargedTotal - total),
            Collected: collected,
            Outstanding: Money.Round(chargedTotal - collected),
            PaidCount: paidCount);
    }

    /// <summary>Every category currently in use, plus the standard ones, for the dropdown.</summary>
    public static IReadOnlyList<string> Categories(TripData trip) =>
        trip.Budget
            .Select(l => l.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Concat(["Food", "Lodging", "Games", "Supplies", "Merch"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

    // -------------------------------------------------------------- mutating

    public static BudgetLine AddLine(TripData trip, string category)
    {
        var line = new BudgetLine
        {
            Id = Ids.New("bl"),
            Item = "New item",
            Unit = 0,
            Qty = 1,
            Category = string.IsNullOrWhiteSpace(category) ? "Food" : category
        };

        trip.Budget.Add(line);
        return line;
    }

    public static void RemoveLine(TripData trip, string lineId) =>
        trip.Budget.RemoveAll(l => l.Id == lineId);

    public static void Update(TripData trip, string lineId, Action<BudgetLine> change)
    {
        var line = Find(trip, lineId);
        if (line is not null) change(line);
    }

    public static BudgetLine? Find(TripData trip, string lineId) =>
        trip.Budget.FirstOrDefault(l => l.Id == lineId);
}
