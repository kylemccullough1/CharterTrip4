using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

/// <summary>
/// The leading team's colour is written straight into a stylesheet, and it is typed by hand on
/// the teams page. So what counts as a colour is worth pinning down.
/// </summary>
public class AccentPaletteTests
{
    private static TripData Jake() => new()
    {
        Teams =
        [
            new Team { Id = "jou",  Name = "Team Jou",  Color = "#d4af37" },
            new Team { Id = "ali",  Name = "Team Ali",  Color = "#2e9e7e" },
            new Team { Id = "kyle", Name = "Team Kyle", Color = "#c94f5a" },
            new Team { Id = "em",   Name = "Team Em",   Color = "#4a7fd6" }
        ]
    };

    private static void Award(TripData trip, string teamId, int points) =>
        trip.Scores.Add(new ScoreEntry { Id = Ids.New("sc"), TeamId = teamId, Points = points });

    // ------------------------------------------------------------------ parsing

    [Theory]
    [InlineData("#c94f5a", "201, 79, 90")]
    [InlineData("#000000", "0, 0, 0")]
    [InlineData("#ffffff", "255, 255, 255")]
    [InlineData("#FFF", "255, 255, 255")]
    [InlineData("#abc", "170, 187, 204")]
    [InlineData("  #4a7fd6  ", "74, 127, 214")]
    public void A_hex_colour_comes_back_as_a_triplet(string input, string expected)
    {
        var accent = AccentPalette.Parse(input);

        Assert.NotNull(accent);
        Assert.Equal(expected, accent!.Value.Rgb);
    }

    /// <summary>
    /// Anything that is not plainly a hex colour is refused rather than escaped — the site simply
    /// stays gold. Note the last two: a value that closed the declaration and opened a rule of
    /// its own is the reason this is a whitelist and not a blacklist.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("red")]
    [InlineData("rgb(1,2,3)")]
    [InlineData("d4af37")]
    [InlineData("#12345")]
    [InlineData("#gggggg")]
    [InlineData("#d4af37; } body { display: none")]
    [InlineData("#fff</style><script>alert(1)</script>")]
    public void Anything_that_is_not_a_hex_colour_is_refused(string? input) =>
        Assert.Null(AccentPalette.Parse(input));

    // ------------------------------------------------------------------ the leader

    [Fact]
    public void The_leader_is_the_team_out_in_front()
    {
        var trip = Jake();
        Award(trip, "kyle", 40);
        Award(trip, "em", 20);

        Assert.Equal("#c94f5a", AccentPalette.Leader(trip)?.Hex);
    }

    [Fact]
    public void Nobody_leads_a_board_that_has_not_been_scored_on()
    {
        Assert.Null(AccentPalette.Leader(Jake()));
        Assert.Null(AccentPalette.Leader(new TripData()));
    }

    /// <summary>
    /// Level at the top is not a lead. Standings fall back to the stored order to break a tie,
    /// so without this the site would announce a leader who is only first alphabetically.
    /// </summary>
    [Fact]
    public void Level_at_the_top_leaves_the_site_gold()
    {
        var trip = Jake();
        Award(trip, "kyle", 30);
        Award(trip, "em", 30);

        Assert.Null(AccentPalette.Leader(trip));
    }

    [Fact]
    public void A_leader_whose_colour_is_nonsense_leaves_the_site_gold()
    {
        var trip = Jake();
        trip.Teams.Single(t => t.Id == "kyle").Color = "chartreuse";
        Award(trip, "kyle", 40);

        Assert.Null(AccentPalette.Leader(trip));
    }

    [Fact]
    public void Going_behind_hands_the_colour_over()
    {
        var trip = Jake();
        Award(trip, "kyle", 40);
        Assert.Equal("#c94f5a", AccentPalette.Leader(trip)?.Hex);

        Award(trip, "em", 50);
        Assert.Equal("#4a7fd6", AccentPalette.Leader(trip)?.Hex);
    }
}
