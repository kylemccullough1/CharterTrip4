using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

public class BudgetServiceTests
{
    private static TripData Trip(params (string Item, decimal Unit, decimal Qty, string Cat)[] lines)
    {
        var trip = new TripData { Trip = { PricePerPerson = 100 } };
        var n = 0;
        foreach (var (item, unit, qty, cat) in lines)
            trip.Budget.Add(new BudgetLine { Id = $"bl{n++}", Item = item, Unit = unit, Qty = qty, Category = cat });
        return trip;
    }

    private static void AddRoster(TripData trip, int count, int paid, decimal amount = 100)
    {
        for (var i = 0; i < count; i++)
            trip.Roster.Add(new RosterPerson { Id = $"p{i}", Name = $"P{i}", Paid = i < paid, Amount = i < paid ? amount : 0 });
    }

    [Fact]
    public void Total_multiplies_unit_by_quantity()
    {
        var trip = Trip(("Eggs", 4.47m, 2, "Food"), ("Bacon", 14.44m, 1, "Food"));
        Assert.Equal(23.38m, BudgetService.Total(trip));
    }

    [Fact]
    public void Fractional_quantities_are_supported()
    {
        // Skirt steak is bought by the pound, not the unit.
        var trip = Trip(("Skirt steak", 8.11m, 16.5m, "Food"));
        Assert.Equal(133.82m, BudgetService.Total(trip));
    }

    [Fact]
    public void An_empty_budget_totals_zero_rather_than_throwing()
    {
        Assert.Equal(0m, BudgetService.Total(new TripData()));
    }

    [Fact]
    public void Categories_carry_a_subtotal_and_a_share_and_come_back_biggest_first()
    {
        var trip = Trip(
            ("Vrbo", 300m, 1, "Lodging"),
            ("Eggs", 50m, 1, "Food"),
            ("Bacon", 50m, 1, "Food"));

        var cats = BudgetService.ByCategory(trip);

        Assert.Equal(["Lodging", "Food"], cats.Select(c => c.Name));
        Assert.Equal(300m, cats[0].Subtotal);
        Assert.Equal(100m, cats[1].Subtotal);
        Assert.Equal(75m, cats[0].ShareOfTotal);
        Assert.Equal(25m, cats[1].ShareOfTotal);
    }

    [Fact]
    public void Lines_with_no_category_are_grouped_rather_than_lost()
    {
        var trip = Trip(("Mystery spend", 10m, 1, ""));
        var cat = Assert.Single(BudgetService.ByCategory(trip));

        Assert.Equal("Uncategorised", cat.Name);
        Assert.Equal(10m, cat.Subtotal);
    }

    [Fact]
    public void Shares_do_not_divide_by_zero_on_an_empty_budget()
    {
        var trip = Trip(("Free thing", 0m, 1, "Food"));
        Assert.Equal(0m, BudgetService.ByCategory(trip)[0].ShareOfTotal);
    }

    [Fact]
    public void Per_person_divides_by_the_roster()
    {
        var trip = Trip(("Everything", 2600m, 1, "Food"));
        AddRoster(trip, 26, 0);

        Assert.Equal(100m, BudgetService.Summarize(trip).PerPerson);
    }

    [Fact]
    public void Per_person_does_not_divide_by_zero_when_the_roster_is_empty()
    {
        var trip = Trip(("Everything", 500m, 1, "Food"));
        var summary = BudgetService.Summarize(trip);

        Assert.Equal(1, summary.Headcount);
        Assert.Equal(500m, summary.PerPerson);
    }

    [Fact]
    public void Variance_is_what_is_charged_minus_what_is_planned()
    {
        var trip = Trip(("Spend", 2000m, 1, "Food"));
        AddRoster(trip, 26, 0);          // charged 100 each => 2600

        var summary = BudgetService.Summarize(trip);
        Assert.Equal(2600m, summary.ChargedTotal);
        Assert.Equal(600m, summary.Variance);           // surplus
    }

    [Fact]
    public void Variance_goes_negative_when_the_plan_costs_more_than_the_price()
    {
        var trip = Trip(("Spend", 3000m, 1, "Food"));
        AddRoster(trip, 26, 0);

        Assert.Equal(-400m, BudgetService.Summarize(trip).Variance);
    }

    [Fact]
    public void Collected_counts_only_the_people_marked_paid()
    {
        var trip = Trip(("Spend", 100m, 1, "Food"));
        AddRoster(trip, 26, paid: 6, amount: 177m);

        var summary = BudgetService.Summarize(trip);
        Assert.Equal(6, summary.PaidCount);
        Assert.Equal(1062m, summary.Collected);
        Assert.Equal(2600m - 1062m, summary.Outstanding);
    }

    [Fact]
    public void Categories_for_the_dropdown_include_the_standard_set_without_duplicates()
    {
        var trip = Trip(("Eggs", 1m, 1, "Food"), ("Beer", 1m, 1, "Drinks"));
        var cats = BudgetService.Categories(trip);

        Assert.Contains("Drinks", cats);
        Assert.Contains("Lodging", cats);
        Assert.Equal(cats.Count, cats.Distinct().Count());
    }

    [Fact]
    public void Add_and_remove_a_line()
    {
        var trip = Trip(("Eggs", 4.47m, 2, "Food"));

        var added = BudgetService.AddLine(trip, "Games");
        Assert.Equal(2, trip.Budget.Count);
        Assert.Equal("Games", added.Category);
        Assert.Equal(1m, added.Qty);

        BudgetService.RemoveLine(trip, added.Id);
        Assert.Single(trip.Budget);
    }

    [Fact]
    public void Update_changes_a_line_and_the_total_follows()
    {
        var trip = Trip(("Eggs", 4.47m, 2, "Food"));
        BudgetService.Update(trip, trip.Budget[0].Id, l => l.Qty = 5);

        Assert.Equal(22.35m, BudgetService.Total(trip));
    }
}

public class MoneyTests
{
    [Theory]
    [InlineData("12.50", 12.50)]
    [InlineData("$12.50", 12.50)]
    [InlineData(" 1,299.99 ", 1299.99)]
    [InlineData("0", 0)]
    [InlineData("8.11", 8.11)]
    public void Parses_the_shapes_people_actually_type(string input, double expected)
    {
        Assert.True(Money.TryParse(input, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData(null)]
    public void Rejects_what_is_not_a_number(string? input)
    {
        Assert.False(Money.TryParse(input, out _));
    }

    [Fact]
    public void ParseOr_keeps_the_old_value_when_the_input_is_rubbish()
    {
        Assert.Equal(4.47m, Money.ParseOr("not a price", 4.47m));
        Assert.Equal(9m, Money.ParseOr("9", 4.47m));
    }

    [Fact]
    public void Formats_as_dollars_and_plain_numbers()
    {
        Assert.Equal("$1,299.99", Money.Format(1299.99m));
        Assert.Equal("$0.00", Money.Format(0m));
        Assert.Equal("2.5", Money.Plain(2.5m));
        Assert.Equal("26", Money.Plain(26m));
    }
}
