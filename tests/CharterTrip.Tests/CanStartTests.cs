using CharterTrip.Core.Models;
using CharterTrip.Core.Mystery;
using CharterTrip.Infrastructure.Mystery;
using CharterTrip.Infrastructure.Seed;

namespace CharterTrip.Tests;

/// <summary>
/// What the console says when the evening cannot start yet.
///
/// This is read by somebody standing in a room full of people wondering why the button is grey, so
/// it has to name who is missing. It used to say "4 of the four house parts are still going", which
/// is a sentence nobody can act on.
/// </summary>
public class CanStartTests
{
    private static TripData Ready()
    {
        var trip = SeedLoader.Load();
        StoryLoader.SeedInto(trip);
        CastingService.OpenDoors(trip, new Random(1));

        var organizers = CastingService.Organizers(trip).Select(p => p.Id).ToList();
        var parts = CastingService.UnclaimedStaffParts(trip).Select(c => c.Id).ToList();

        foreach (var (person, part) in organizers.Zip(parts))
            CastingService.ClaimStaffPart(trip, person, part);

        foreach (var person in CastingService.Unclaimed(trip).ToList())
            CastingService.ClaimCharacter(trip, person.Id, new Random(2));

        return trip;
    }

    [Fact]
    public void An_empty_room_names_the_house_parts_and_counts_the_guests()
    {
        var trip = SeedLoader.Load();
        StoryLoader.SeedInto(trip);
        CastingService.OpenDoors(trip, new Random(1));

        var (ready, reason) = CastingService.CanStart(trip);

        Assert.False(ready);
        Assert.Contains("James Braun", reason);
        Assert.Contains("21 guests are not here yet", reason);
    }

    [Fact]
    public void With_the_house_parts_taken_it_only_counts_the_guests()
    {
        var trip = SeedLoader.Load();
        StoryLoader.SeedInto(trip);
        CastingService.OpenDoors(trip, new Random(1));

        foreach (var (person, part) in CastingService.Organizers(trip).Select(p => p.Id)
                     .Zip(CastingService.UnclaimedStaffParts(trip).Select(c => c.Id).ToList()))
        {
            CastingService.ClaimStaffPart(trip, person, part);
        }

        var (ready, reason) = CastingService.CanStart(trip);

        Assert.False(ready);
        Assert.DoesNotContain("James Braun", reason);
        Assert.Contains("21 guests are not here yet", reason);
    }

    /// <summary>One person outstanding reads as a person, not as "1 guests".</summary>
    [Fact]
    public void One_missing_guest_is_written_as_one()
    {
        var trip = Ready();
        trip.Mystery.Play.Cast.First(c => trip.Mystery.Story.Character(c.CharacterId) is { IsStaff: false })
            .PersonId = null;

        var (ready, reason) = CastingService.CanStart(trip);

        Assert.False(ready);
        Assert.Contains("One guest is not here yet", reason);
    }

    /// <summary>
    /// Nothing starts without the host, because Braun is the only part that drives the evening.
    /// </summary>
    [Fact]
    public void The_host_is_required_like_everybody_else()
    {
        var trip = Ready();
        var braun = trip.Mystery.Story.StaffParts.First(c => c.Staff == MysteryStaffRole.Host);
        trip.Mystery.Play.ForCharacter(braun.Id)!.PersonId = null;

        var (ready, reason) = CastingService.CanStart(trip);

        Assert.False(ready);
        Assert.Contains(braun.Name, reason);
    }

    [Fact]
    public void With_all_twenty_five_in_it_starts()
    {
        var (ready, reason) = CastingService.CanStart(Ready());

        Assert.True(ready);
        Assert.Contains("25", reason);
    }
}
