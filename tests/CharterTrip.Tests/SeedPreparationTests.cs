using CharterTrip.Core.Models;
using CharterTrip.Core.Services;
using CharterTrip.Infrastructure.Seed;

namespace CharterTrip.Tests;

/// <summary>
/// Refreshing the seed from a live trip is only safe if it keeps what the host wrote and drops
/// what the weekend produced. Getting that backwards is how a reset puts a stale scoreboard and
/// somebody's used buzzer code back on the wall.
/// </summary>
public class SeedPreparationTests
{
    private static TripData PlayedTrip()
    {
        var trip = SeedLoader.Load();

        trip.Revision = 74;
        trip.Scores.Add(new ScoreEntry
        {
            Id = "sc-1", TeamId = trip.Teams[0].Id, GameId = "jeopardy", Points = 15, Note = "15 · correct"
        });

        var game = trip.Jeopardy.Game;
        game.Phase = JeopardyPhase.Judging;
        game.UsedClueIds.Add(trip.Jeopardy.Categories[0].Clues[0].Id);
        game.BuzzerCodes[trip.Teams[0].Id] = "XY4H";
        game.HostCode = "AAKH";

        trip.Mystery.Active = true;
        trip.Mystery.CurrentRoundIndex = 2;
        trip.Mystery.Deal = new MysteryDeal
        {
            Seed = 1234,
            Cast =
            [
                new MysteryCastMember
                {
                    CharacterId = "wilhelm",
                    PersonId = trip.Roster[0].Id,
                    ZoneId = "entry",
                    FactionId = "killer",
                    GuiltSlot = "access"
                }
            ]
        };
        trip.Mystery.Clues.Add(new MysteryClue
        {
            Id = "mc-1", Token = "ACDEFGHJKM", ZoneId = "entry", TraceCharacterId = "wilhelm", Found = true
        });
        trip.Mystery.Trials.Add(new MysteryTrial { RoundId = "trial_1", ConvictedCharacterIds = ["hugo"] });

        return trip;
    }

    [Fact]
    public void Play_state_does_not_survive()
    {
        var trip = PlayedTrip();

        SeedPreparation.Prepare(trip, DateTimeOffset.UnixEpoch);

        Assert.Equal(0, trip.Revision);
        Assert.Empty(trip.Scores);
        Assert.Equal(JeopardyPhase.NotStarted, trip.Jeopardy.Game.Phase);
        Assert.Empty(trip.Jeopardy.Game.UsedClueIds);
        Assert.Empty(trip.Jeopardy.Game.BuzzerCodes);
        Assert.Equal("", trip.Jeopardy.Game.HostCode);
        Assert.False(trip.Mystery.Active);
        Assert.Equal(-1, trip.Mystery.CurrentRoundIndex);

        // The whole mystery goes, deal included. Nothing in it is authored any more — it is all
        // generated from the script and a seed, so there is no host work to preserve.
        Assert.Null(trip.Mystery.Deal);
        Assert.Empty(trip.Mystery.Clues);
        Assert.Empty(trip.Mystery.Trials);

        // Issued identity, and this file goes into git.
        Assert.All(trip.Roster, p => Assert.True(string.IsNullOrEmpty(p.JoinToken)));
    }

    [Fact]
    public void Everything_the_host_wrote_does()
    {
        var trip = PlayedTrip();
        trip.Itinerary[0].Items[0].Title = "Check-in, moved earlier";
        trip.Itinerary[0].Items[0].StartMinutes = 840;

        SeedPreparation.Prepare(trip, DateTimeOffset.UnixEpoch);

        Assert.Equal("Check-in, moved earlier", trip.Itinerary[0].Items[0].Title);
        Assert.Equal(840, trip.Itinerary[0].Items[0].StartMinutes);
        Assert.Equal(25, trip.Roster.Count);
        Assert.Equal(25, trip.Jeopardy.Categories.Sum(c => c.Clues.Count));

        // The roster itself survives — names, teams and roles are the host's work. Only the
        // issued token on each person goes.
        Assert.All(trip.Roster, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
        Assert.Equal(4, trip.Roster.Count(p => p.Role == TripRole.Admin));
    }

    /// <summary>A prepared trip has to still satisfy everything SeedDataTests asserts.</summary>
    [Fact]
    public void The_current_seed_is_already_in_prepared_form()
    {
        var seed = SeedLoader.Load();
        var prepared = SeedLoader.Load();

        SeedPreparation.Prepare(prepared, seed.UpdatedUtc);

        Assert.Equal(seed.Revision, prepared.Revision);
        Assert.Equal(seed.UpdatedUtc, prepared.UpdatedUtc);
        Assert.Equal(seed.Scores.Count, prepared.Scores.Count);
        Assert.Equal(seed.Jeopardy.Game.Phase, prepared.Jeopardy.Game.Phase);
        Assert.Equal(seed.Mystery.CurrentRoundIndex, prepared.Mystery.CurrentRoundIndex);
    }
}
