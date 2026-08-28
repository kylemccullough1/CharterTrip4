using System.Security.Cryptography;
using CharterTrip.Core.Models;

namespace CharterTrip.Core.Mystery;

/// <summary>
/// Getting twenty-five people into the game: the two codes, who is playing whom, and their photos.
///
/// Static methods over <see cref="TripData"/>, following <c>JeopardyService</c>. Everything here is
/// meant to be called from inside <c>MutateAsync</c>, which is what makes a check and the act it
/// guards share one lock — and the door is exactly where that matters, because twenty-one people tap
/// their own names inside about ninety seconds.
/// </summary>
public static class CastingService
{
    /// <summary>
    /// No O/0, I/1, S/5, B/8 — the same alphabet as the buzzer codes and the join tokens, for the
    /// same reason: this gets read off a screen and typed by somebody holding a drink.
    /// </summary>
    private const string CodeAlphabet = "ACDEFGHJKMNPQRTUVWXY34679";

    /// <summary>
    /// Five, where a buzzer code is four.
    ///
    /// It is a door rather than a credential — it proves you are in the house and says nothing about
    /// who you are — but it is live all evening rather than for one game, and the host one opens the
    /// four parts that come with the guilty list attached.
    /// </summary>
    private const int CodeLength = 5;

    /// <summary>Twelve, and not derived from anything. See <see cref="NewToken"/>.</summary>
    private const int TokenLength = 12;

    // ------------------------------------------------------------------------------------------
    //  Starting a game
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Open the doors: mint both codes, lay out a seat per character, and give every clue card a
    /// token to print.
    ///
    /// Nobody is cast here. Guests take a part at the door with the party code, and the four
    /// organizers pick one with the host code — so this can be run days before anybody arrives, and
    /// re-run after a cancellation without anybody losing the character they already have.
    /// </summary>
    public static void OpenDoors(TripData trip, Random random)
    {
        var play = trip.Mystery.Play;

        if (string.IsNullOrWhiteSpace(play.PartyCode)) play.PartyCode = NewCode(random);
        if (string.IsNullOrWhiteSpace(play.HostCode)) play.HostCode = NewCode(random);

        // Distinct, or the host door is unreachable behind the guest one — Resolve checks the host
        // code first, so a collision would silently hand every guest the organizers' picker.
        while (string.Equals(play.HostCode, play.PartyCode, StringComparison.OrdinalIgnoreCase))
            play.HostCode = NewCode(random);

        foreach (var character in trip.Mystery.Story.Characters)
        {
            if (play.ForCharacter(character.Id) is not null) continue;

            play.Cast.Add(new MysteryCastMember
            {
                CharacterId = character.Id,
                BadgeToken = NewToken()
            });
        }

        // A seat for a character somebody has since deleted from the story has nowhere to sit.
        play.Cast.RemoveAll(c => trip.Mystery.Story.Character(c.CharacterId) is null);

        foreach (var clue in trip.Mystery.Story.Clues)
        {
            if (play.StateFor(clue.Id) is not null) continue;
            play.ClueStates.Add(new MysteryClueState { ClueId = clue.Id, Token = NewToken() });
        }

        play.ClueStates.RemoveAll(s => trip.Mystery.Story.Clue(s.ClueId) is null);
    }

    /// <summary>
    /// Tear down the evening and keep the story.
    ///
    /// The written half is work — characters, rooms, clues, prose, all of it edited on the site —
    /// and the played half is one night. Discarding has to be safe enough to do repeatedly while
    /// rehearsing, so it never touches <see cref="MysteryStory"/>.
    /// </summary>
    public static void Discard(TripData trip)
    {
        trip.Mystery.Phase = MysteryPhase.Lobby;
        trip.Mystery.Play = new MysteryPlay();
    }

    // ------------------------------------------------------------------------------------------
    //  Guests
    // ------------------------------------------------------------------------------------------

    /// <summary>Roster members who have not yet taken a part. The list on the door screen.</summary>
    public static IReadOnlyList<RosterPerson> Unclaimed(TripData trip)
    {
        var taken = trip.Mystery.Play.Cast
            .Where(c => c.PersonId is not null)
            .Select(c => c.PersonId!)
            .ToHashSet(StringComparer.Ordinal);

        return trip.Roster
            .Where(p => p.Role != TripRole.Admin && !taken.Contains(p.Id))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>The four organizers, for the host door's first question.</summary>
    public static IReadOnlyList<RosterPerson> Organizers(TripData trip) =>
        trip.Roster
            .Where(p => p.Role == TripRole.Admin)
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Guest seats still going.</summary>
    public static int SeatsLeft(TripData trip) =>
        GuestSeats(trip).Count(c => c.PersonId is null);

    public static int TotalSeats(TripData trip) => GuestSeats(trip).Count();

    /// <summary>
    /// Take a name and hand back a character.
    ///
    /// Random, out of whatever is left. This is the only randomness in the game — the story itself
    /// is fixed, and which of twenty-one written parts a particular friend ends up holding is the
    /// one thing worth leaving to chance.
    ///
    /// Idempotent by person, so somebody coming back on a second phone gets the character they
    /// already had rather than a second one, and returns null when there is nothing left to give.
    /// </summary>
    public static MysteryCastMember? ClaimCharacter(TripData trip, string personId, Random random)
    {
        if (trip.Mystery.Play.ForPerson(personId) is { } already) return already;

        var open = GuestSeats(trip).Where(c => c.PersonId is null).ToList();
        if (open.Count == 0) return null;

        var seat = open[random.Next(open.Count)];
        seat.PersonId = personId;
        seat.JoinedAt = DateTimeOffset.UtcNow;
        return seat;
    }

    /// <summary>
    /// Seat the next person still waiting at the door, and say who it was.
    ///
    /// What one phone does when somebody types the party code and taps a name, with the choosing
    /// taken out — so a caller can do it twenty-five times without twenty-five browsers. Refuses if
    /// the code is not live, because then there is no door to walk through and nothing here should
    /// invent one. Null when the room is full.
    /// </summary>
    public static string? SeatNextGuest(TripData trip, string code, Random random)
    {
        if (!string.Equals(trip.Mystery.Play.PartyCode, code, StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.IsNullOrWhiteSpace(code)) return null;

        var next = Unclaimed(trip).FirstOrDefault();
        if (next is null) return null;

        return ClaimCharacter(trip, next.Id, random) is null ? null : next.Id;
    }

    // ------------------------------------------------------------------------------------------
    //  The house parts
    // ------------------------------------------------------------------------------------------

    /// <summary>Braun and the facilitators nobody has picked up yet.</summary>
    public static IReadOnlyList<MysteryCharacter> UnclaimedStaffParts(TripData trip)
    {
        var taken = trip.Mystery.Play.Cast
            .Where(c => c.PersonId is not null)
            .Select(c => c.CharacterId)
            .ToHashSet(StringComparer.Ordinal);

        return trip.Mystery.Story.StaffParts
            .Where(c => !taken.Contains(c.Id))
            .ToList();
    }

    /// <summary>
    /// Take one of the four house parts.
    ///
    /// Refused for anybody who is not an organizer, and that refusal is the only reason the host
    /// QR is safe to have on a screen at all: a guest who scans it reaches a picker of four
    /// organizer names and cannot get past it. Loosening this hands out the guilty list.
    /// </summary>
    public static bool ClaimStaffPart(TripData trip, string personId, string characterId)
    {
        var person = trip.Roster.FirstOrDefault(p => p.Id == personId);
        if (person is null || person.Role != TripRole.Admin) return false;

        var character = trip.Mystery.Story.Character(characterId);
        if (character is null || !character.IsStaff) return false;

        var seat = trip.Mystery.Play.ForCharacter(characterId);
        if (seat is null) return false;

        // Somebody else got there between the page rendering and the tap.
        if (seat.PersonId is not null && seat.PersonId != personId) return false;

        // Swapping parts: give up whatever they were holding first, or they hold two.
        foreach (var other in trip.Mystery.Play.Cast.Where(c => c.PersonId == personId && c.CharacterId != characterId))
            other.PersonId = null;

        seat.PersonId = personId;
        seat.JoinedAt ??= DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>Which character this person is playing, staff or guest.</summary>
    public static MysteryCharacter? CharacterFor(TripData trip, string? personId)
    {
        if (personId is null) return null;
        var seat = trip.Mystery.Play.ForPerson(personId);
        return seat is null ? null : trip.Mystery.Story.Character(seat.CharacterId);
    }

    public static bool IsStaff(TripData trip, string? personId) =>
        CharacterFor(trip, personId)?.IsStaff ?? false;

    public static bool IsHost(TripData trip, string? personId) =>
        CharacterFor(trip, personId)?.Staff == MysteryStaffRole.Host;

    // ------------------------------------------------------------------------------------------
    //  Photos
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Attach the picture somebody took at the door. Empty clears it back to a monogram.
    /// </summary>
    public static bool SetPhoto(TripData trip, string personId, string? photoUrl)
    {
        var seat = trip.Mystery.Play.ForPerson(personId);
        if (seat is null) return false;

        seat.PhotoUrl = photoUrl ?? "";
        return true;
    }

    /// <summary>Marks the letter as read, so a reload lands on the sheet rather than the envelope.</summary>
    public static bool MarkLetterOpened(TripData trip, string personId)
    {
        var seat = trip.Mystery.Play.ForPerson(personId);
        if (seat is null || seat.LetterOpened) return false;

        seat.LetterOpened = true;
        return true;
    }

    // ------------------------------------------------------------------------------------------
    //  Are we ready?
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether the evening can start, and if not, the sentence to put beside the greyed button.
    ///
    /// A reason rather than a bare false, because the host is standing in a room full of people
    /// wondering why nothing is happening. A photo is never part of it — somebody whose camera is
    /// being difficult must not be able to hold up the party.
    /// </summary>
    public static (bool Ready, string Reason) CanStart(TripData trip)
    {
        var story = trip.Mystery.Story;

        if (story.Characters.Count == 0)
            return (false, "There is no story yet.");

        // Named rather than counted. "4 house parts still going" meant nothing to anybody standing
        // in the room; "waiting on Braun and Chloe" is something they can go and fix.
        var missingStaff = UnclaimedStaffParts(trip).Select(c => c.Name).ToList();
        var seatsLeft = SeatsLeft(trip);

        if (missingStaff.Count > 0 && seatsLeft > 0)
            return (false, $"Waiting on {Names(missingStaff)}, and {Guests(seatsLeft)} not here yet.");

        if (missingStaff.Count > 0)
            return (false, $"Waiting on {Names(missingStaff)}.");

        if (seatsLeft > 0)
            return (false, $"{Guests(seatsLeft)} not here yet.");

        return (true, $"All {trip.Mystery.Play.Cast.Count} of them are in.");
    }

    // ------------------------------------------------------------------------------------------

    private static string Guests(int count) =>
        count == 1 ? "One guest is" : $"{count} guests are";

    private static string Names(IReadOnlyList<string> names) => names.Count switch
    {
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => string.Join(", ", names.Take(names.Count - 1)) + $" and {names[^1]}"
    };

    private static IEnumerable<MysteryCastMember> GuestSeats(TripData trip) =>
        trip.Mystery.Play.Cast.Where(c => trip.Mystery.Story.Character(c.CharacterId) is { IsStaff: false });

    private static string NewCode(Random random) =>
        new(Enumerable.Range(0, CodeLength)
            .Select(_ => CodeAlphabet[random.Next(CodeAlphabet.Length)])
            .ToArray());

    /// <summary>
    /// A badge or clue token.
    ///
    /// Cryptographic, and deliberately not derived from anything about the game. Nine guessable clue
    /// tokens would let somebody read every card from the sofa, and walking to the room is the whole
    /// mechanic; a guessable badge token would let somebody scan a person they never met.
    /// </summary>
    private static string NewToken() =>
        new(Enumerable.Range(0, TokenLength)
            .Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)])
            .ToArray());
}
