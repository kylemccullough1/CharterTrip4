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

    // ---- signing a strip of phones in ---------------------------------------------------------

    /// <summary>
    /// What the testing panel's one button does, twenty-five times. Same call the real door makes,
    /// so a phone signed in this way is indistinguishable from one somebody typed into.
    /// </summary>
    [Fact]
    public void Seating_phones_fills_the_room_and_then_stops()
    {
        var trip = SeedLoader.Load();
        StoryLoader.SeedInto(trip);
        CastingService.OpenDoors(trip, new Random(1));

        var code = trip.Mystery.Play.PartyCode;
        var seated = new List<string>();

        for (var i = 0; i < 30; i++)
        {
            if (CastingService.SeatNextGuest(trip, code, new Random(i)) is not { } personId) break;
            seated.Add(personId);
        }

        Assert.Equal(21, seated.Count);
        Assert.Equal(seated.Count, seated.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(0, CastingService.SeatsLeft(trip));

        // The house parts are not guest seats, so they are still going.
        Assert.Equal(4, CastingService.UnclaimedStaffParts(trip).Count);
    }

    /// <summary>A code that is not the live one is not a door, and must not seat anybody.</summary>
    [Fact]
    public void A_dead_code_seats_nobody()
    {
        var trip = SeedLoader.Load();
        StoryLoader.SeedInto(trip);
        CastingService.OpenDoors(trip, new Random(1));

        Assert.Null(CastingService.SeatNextGuest(trip, "NOPE1", new Random(1)));
        Assert.Null(CastingService.SeatNextGuest(trip, "", new Random(1)));
        Assert.Equal(21, CastingService.SeatsLeft(trip));
    }

    [Fact]
    public void With_all_twenty_five_in_it_starts()
    {
        var (ready, reason) = CastingService.CanStart(Ready());

        Assert.True(ready);
        Assert.Contains("25", reason);
    }

    /// <summary>
    /// The two doors have to add up, and it is easy to believe they do not.
    ///
    /// Twenty-five seats are filled from two disjoint pools that never overlap: the guest picker
    /// draws from the roster minus the organizers, and the four house parts are refused to anybody
    /// who is not one. Seating everybody the guest door will seat therefore stops at twenty-one and
    /// leaves a game that cannot start — which is correct, and reads exactly like a bug from the
    /// room. This pins the arithmetic so a roster edit that breaks it fails here rather than on the
    /// night.
    /// </summary>
    [Fact]
    public void The_guest_door_and_the_house_parts_together_fill_every_seat()
    {
        var trip = SeedLoader.Load();
        StoryLoader.SeedInto(trip);
        CastingService.OpenDoors(trip, new Random(1));

        // Everybody the guest door will take. Organizers are not in this list at all.
        foreach (var person in CastingService.Unclaimed(trip).ToList())
            CastingService.ClaimCharacter(trip, person.Id, new Random(2));

        Assert.Equal(0, CastingService.SeatsLeft(trip));
        Assert.Equal(21, trip.Mystery.Play.Cast.Count(c => c.PersonId is not null));

        // Twenty-one of twenty-five, and still not startable: the four house parts are untouched.
        Assert.Equal(4, CastingService.UnclaimedStaffParts(trip).Count);
        Assert.False(CastingService.CanStart(trip).Ready);

        // There is exactly one organizer for each of them, which is the coupling that has to hold.
        var organizers = CastingService.Organizers(trip);
        Assert.Equal(4, organizers.Count);

        foreach (var (person, part) in organizers.Select(p => p.Id)
                     .Zip(CastingService.UnclaimedStaffParts(trip).Select(c => c.Id).ToList()))
        {
            Assert.True(CastingService.ClaimStaffPart(trip, person, part));
        }

        Assert.True(CastingService.CanStart(trip).Ready);
        Assert.Equal(25, trip.Mystery.Play.Cast.Count(c => c.PersonId is not null));
    }

    /// <summary>
    /// A guest cannot take a house part, whatever they type. This is the only thing standing between
    /// the party code and the guilty list, so it is worth a test of its own.
    /// </summary>
    [Fact]
    public void A_guest_cannot_take_a_house_part()
    {
        var trip = SeedLoader.Load();
        StoryLoader.SeedInto(trip);
        CastingService.OpenDoors(trip, new Random(1));

        var guest = trip.Roster.First(p => p.Role != TripRole.Admin);
        var part = CastingService.UnclaimedStaffParts(trip)[0];

        Assert.False(CastingService.ClaimStaffPart(trip, guest.Id, part.Id));
        Assert.Equal(4, CastingService.UnclaimedStaffParts(trip).Count);
    }

}
