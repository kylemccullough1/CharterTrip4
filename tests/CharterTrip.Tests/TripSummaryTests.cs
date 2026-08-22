using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

public class TripSummaryTests
{
    /// <summary>Teams in the order they are stored: JAKE — Jou, Ali, Kyle, Em.</summary>
    private static TripData Jake() => new()
    {
        Teams =
        [
            new Team { Id = "jou",  Name = "Team Jou" },
            new Team { Id = "ali",  Name = "Team Ali" },
            new Team { Id = "kyle", Name = "Team Kyle" },
            new Team { Id = "em",   Name = "Team Em" }
        ]
    };

    private static void Award(TripData trip, string teamId, int points) =>
        trip.Scores.Add(new ScoreEntry { Id = Ids.New("sc"), TeamId = teamId, Points = points });

    private static List<string> Order(TripData trip) =>
        TripSummary.Standings(trip).Select(s => s.Team.Id).ToList();

    [Fact]
    public void With_no_points_the_board_is_in_JAKE_order()
    {
        // Alphabetical ties gave Ali, Em, Jou, Kyle — which is most of the weekend, since the
        // board sits at nil-all until the first game is scored.
        Assert.Equal(["jou", "ali", "kyle", "em"], Order(Jake()));
    }

    [Fact]
    public void Points_still_win_over_the_stored_order()
    {
        var trip = Jake();
        Award(trip, "em", 30);
        Award(trip, "kyle", 10);

        Assert.Equal(["em", "kyle", "jou", "ali"], Order(trip));
    }

    [Fact]
    public void Teams_level_on_points_fall_back_to_JAKE_order()
    {
        var trip = Jake();
        Award(trip, "em", 20);
        Award(trip, "ali", 20);

        // Both on 20, so Ali comes before Em because Ali is stored first.
        Assert.Equal(["ali", "em", "jou", "kyle"], Order(trip));
    }

    [Fact]
    public void A_team_total_is_the_sum_of_its_entries()
    {
        var trip = Jake();
        Award(trip, "jou", 5);
        Award(trip, "jou", 15);
        Award(trip, "ali", 100);

        Assert.Equal(20, TripSummary.TeamTotal(trip, "jou"));
        Assert.Equal(0, TripSummary.TeamTotal(trip, "kyle"));
    }

    [Fact]
    public void Negative_points_are_counted_too()
    {
        var trip = Jake();
        Award(trip, "jou", 20);
        Award(trip, "jou", -25);

        Assert.Equal(-5, TripSummary.TeamTotal(trip, "jou"));
        Assert.Equal("jou", Order(trip)[^1]);       // last place
    }

    // ------------------------------------------------------------- countdown

    private static TripInfo Window() => new()
    {
        StartsAt = new DateTimeOffset(2026, 8, 28, 21, 0, 0, TimeSpan.Zero),
        EndsAt = new DateTimeOffset(2026, 8, 30, 17, 0, 0, TimeSpan.Zero)
    };

    [Fact]
    public void Countdown_counts_days_then_hours_then_switches_to_live()
    {
        var trip = Window();

        Assert.Equal("6 days", TripSummary.ToStart(trip, new DateTimeOffset(2026, 8, 22, 21, 0, 0, TimeSpan.Zero)).Headline);
        Assert.Equal("5 hrs", TripSummary.ToStart(trip, new DateTimeOffset(2026, 8, 28, 16, 0, 0, TimeSpan.Zero)).Headline);
        Assert.Equal("Happening now", TripSummary.ToStart(trip, new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero)).Headline);
        Assert.Equal("That's a wrap", TripSummary.ToStart(trip, new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)).Headline);
    }

    [Fact]
    public void One_day_out_is_singular()
    {
        var headline = TripSummary.ToStart(Window(), new DateTimeOffset(2026, 8, 27, 21, 0, 0, TimeSpan.Zero)).Headline;
        Assert.Equal("1 day", headline);
    }
}
