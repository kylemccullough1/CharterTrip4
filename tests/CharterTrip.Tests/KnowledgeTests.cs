using CharterTrip.Core.Abstractions;
using CharterTrip.Core.Models;
using CharterTrip.Core.Mystery;
using CharterTrip.Infrastructure.Mystery;

namespace CharterTrip.Tests;

/// <summary>
/// Who is told about whom.
///
/// The one rule you cannot check by looking at a screen, because being told the wrong name is
/// invisible until somebody acts on it — and by then three people have spent an hour trusting
/// somebody who is not on their side.
/// </summary>
public class KnowledgeTests
{
    private static TripData At(MysteryPhase phase)
    {
        var trip = new TripData();
        StoryLoader.SeedInto(trip);
        trip.Mystery.Phase = phase;
        return trip;
    }

    private static string OneIn(TripData trip, string factionId) =>
        trip.Mystery.Story.Guests.First(c => c.FactionId == factionId).Id;

    private static string[] Names(IEnumerable<MysteryCharacter> people) =>
        people.Select(c => c.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();

    [Fact]
    public void Nobody_is_told_anything_before_the_study()
    {
        var trip = At(MysteryPhase.Introductions);

        foreach (var guest in trip.Mystery.Story.Guests)
            Assert.Empty(KnowledgeService.AlliesFor(trip, guest.Id));
    }

    [Fact]
    public void Killers_are_given_each_other_and_their_associates()
    {
        var trip = At(MysteryPhase.StudyScene);
        var me = OneIn(trip, "killer");

        var expected = trip.Mystery.Story.Guests
            .Where(c => c.Id != me && c.FactionId is "killer" or "minion");

        Assert.Equal(Names(expected), Names(KnowledgeService.AlliesFor(trip, me)));
    }

    /// <summary>
    /// Deliberately asymmetric. An associate who could name the other one would hand the room two
    /// convictions for the price of one, and their whole job is to be individually deniable.
    /// </summary>
    [Fact]
    public void Associates_are_given_the_killers_but_never_each_other()
    {
        var trip = At(MysteryPhase.StudyScene);
        var me = OneIn(trip, "minion");

        var told = KnowledgeService.AlliesFor(trip, me);

        Assert.Equal(Names(trip.Mystery.Story.Killers), Names(told));
        Assert.DoesNotContain(told, c => c.FactionId == "minion");
    }

    [Fact]
    public void Detectives_are_given_each_other_and_nobody_else()
    {
        var trip = At(MysteryPhase.StudyScene);
        var me = OneIn(trip, "detective");

        var told = KnowledgeService.AlliesFor(trip, me);

        Assert.All(told, c => Assert.Equal("detective", c.FactionId));
        Assert.Equal(2, told.Count);
        Assert.DoesNotContain(told, c => c.Id == me);
    }

    /// <summary>They are competing for the same conviction slot. Telling them would ruin it.</summary>
    [Fact]
    public void Jesters_are_told_nothing()
    {
        var trip = At(MysteryPhase.StudyScene);
        Assert.Empty(KnowledgeService.AlliesFor(trip, OneIn(trip, "jester")));
    }

    [Fact]
    public void Villagers_are_told_nothing()
    {
        var trip = At(MysteryPhase.StudyScene);
        Assert.Empty(KnowledgeService.AlliesFor(trip, OneIn(trip, "villager")));
    }

    [Fact]
    public void A_claimant_is_told_exactly_one_name_and_it_is_their_rival()
    {
        var trip = At(MysteryPhase.StudyScene);
        var me = trip.Mystery.Story.Guests.First(c => c.FactionId == "inheritance");

        var told = Assert.Single(KnowledgeService.AlliesFor(trip, me.Id));

        Assert.Equal(me.RivalCharacterId, told.Id);
        Assert.Equal(me.Id, told.RivalCharacterId);
    }

    /// <summary>Staff have no faction, so there is nobody they could be told about.</summary>
    [Fact]
    public void The_four_running_the_evening_are_told_nothing()
    {
        var trip = At(MysteryPhase.Investigation);

        foreach (var staff in trip.Mystery.Story.StaffParts)
            Assert.Empty(KnowledgeService.AlliesFor(trip, staff.Id));
    }

    [Fact]
    public void Nobody_is_ever_told_about_themselves()
    {
        var trip = At(MysteryPhase.Investigation);

        foreach (var guest in trip.Mystery.Story.Guests)
            Assert.DoesNotContain(KnowledgeService.AlliesFor(trip, guest.Id), c => c.Id == guest.Id);
    }

    /// <summary>
    /// The rule reads KnowsEachOther off the story rather than hard-coding the list, so a decision
    /// to let the jesters find each other is one field rather than a code change.
    /// </summary>
    [Fact]
    public void A_faction_that_learns_to_know_itself_starts_naming_names()
    {
        var trip = At(MysteryPhase.StudyScene);
        trip.Mystery.Story.Faction("jester")!.KnowsEachOther = true;

        var told = KnowledgeService.AlliesFor(trip, OneIn(trip, "jester"));

        Assert.Single(told);
        Assert.Equal("jester", told[0].FactionId);
    }
}
