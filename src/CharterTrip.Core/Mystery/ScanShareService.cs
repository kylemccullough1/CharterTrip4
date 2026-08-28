using CharterTrip.Core.Models;

namespace CharterTrip.Core.Mystery;

/// <summary>
/// The two things a phone's camera does: read a name tag, and read a clue card.
///
/// Neither is an in-app scanner. A QR code is a URL, the phone's own camera opens it, and the page
/// it lands on is an ordinary one — no permission prompt, no library, and no problem with iOS
/// Safari, which does not implement BarcodeDetector and is half the party.
/// </summary>
public static class ScanShareService
{
    // ------------------------------------------------------------------------------------------
    //  Name tags
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Two people met.
    ///
    /// Recorded once per direction and per pair — scanning somebody twice is common, and it should
    /// not make the room look busier than it was. Returns whether this was new, which is what tells
    /// the page whether to celebrate.
    /// </summary>
    public static bool RecordMeeting(TripData trip, string byCharacterId, string metCharacterId, DateTimeOffset now)
    {
        if (byCharacterId == metCharacterId) return false;

        var story = trip.Mystery.Story;
        if (story.Character(byCharacterId) is null || story.Character(metCharacterId) is null) return false;

        if (HasMet(trip, byCharacterId, metCharacterId)) return false;

        // Meeting is symmetric. Storing one row and reading it both ways means the two halves can
        // never disagree about whether a conversation happened.
        trip.Mystery.Play.Meetings.Add(new MysteryMeeting
        {
            ByCharacterId = byCharacterId,
            MetCharacterId = metCharacterId,
            At = now
        });

        return true;
    }

    public static bool HasMet(TripData trip, string a, string b) =>
        trip.Mystery.Play.Meetings.Any(m =>
            (m.ByCharacterId == a && m.MetCharacterId == b) ||
            (m.ByCharacterId == b && m.MetCharacterId == a));

    /// <summary>Everybody this character has talked to.</summary>
    public static IReadOnlyList<MysteryCharacter> Met(TripData trip, string characterId) =>
        trip.Mystery.Story.Characters
            .Where(c => c.Id != characterId && HasMet(trip, characterId, c.Id))
            .ToList();

    /// <summary>
    /// Everybody they have not.
    ///
    /// Shown on the phone beside the met list, dimmed, which is what turns an otherwise invisible
    /// telemetry mechanic into a checklist somebody can actually work through — and makes "talk to
    /// three people" score itself.
    /// </summary>
    public static IReadOnlyList<MysteryCharacter> Unmet(TripData trip, string characterId) =>
        trip.Mystery.Story.Characters
            .Where(c => c.Id != characterId && !HasMet(trip, characterId, c.Id))
            .ToList();

    /// <summary>
    /// Who has been left out, fewest conversations first.
    ///
    /// The list the facilitators aim their objectives at. It is the difference between a good party
    /// and four people standing near a wall, and it is the one thing the app can see that a person
    /// working the room cannot.
    /// </summary>
    public static IReadOnlyList<(MysteryCharacter Character, int Meetings)> Underserved(TripData trip) =>
        trip.Mystery.Story.Guests
            .Select(c => (Character: c, Meetings: trip.Mystery.Play.Meetings.Count(m =>
                m.ByCharacterId == c.Id || m.MetCharacterId == c.Id)))
            .OrderBy(x => x.Meetings)
            .ThenBy(x => x.Character.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>The history between two people, if they have any. Fires on the badge scan.</summary>
    public static MysteryBeef? BeefBetween(TripData trip, string a, string b) =>
        trip.Mystery.Story.Beefs.FirstOrDefault(x =>
            (x.ACharacterId == a && x.BCharacterId == b) ||
            (x.ACharacterId == b && x.BCharacterId == a));

    /// <summary>What this character says when they run into that one.</summary>
    public static string? BeefLineFor(TripData trip, string speakerId, string otherId)
    {
        var beef = BeefBetween(trip, speakerId, otherId);
        if (beef is null) return null;

        var line = beef.ACharacterId == speakerId ? beef.ASays : beef.BSays;
        return MysteryText.Written(line);
    }

    // ------------------------------------------------------------------------------------------
    //  Clue cards
    // ------------------------------------------------------------------------------------------

    public static MysteryClueCard? ClueForToken(TripData trip, string token)
    {
        var state = trip.Mystery.Play.ClueStates
            .FirstOrDefault(s => string.Equals(s.Token, token, StringComparison.OrdinalIgnoreCase));

        return state is null ? null : trip.Mystery.Story.Clue(state.ClueId);
    }

    public static MysteryCastMember? CastForBadge(TripData trip, string token) =>
        trip.Mystery.Play.Cast
            .FirstOrDefault(c => string.Equals(c.BadgeToken, token, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Somebody walked into a room and read a card.
    ///
    /// Recorded on arrival with no confirm button — you are standing in front of it holding a phone,
    /// which is the only confirmation that means anything. Once per person per card.
    /// </summary>
    public static bool RecordClueScan(TripData trip, string characterId, string clueId, DateTimeOffset now)
    {
        if (trip.Mystery.Story.Clue(clueId) is null) return false;
        if (trip.Mystery.Story.Character(characterId) is null) return false;

        if (trip.Mystery.Play.ClueScans.Any(s => s.CharacterId == characterId && s.ClueId == clueId))
            return false;

        trip.Mystery.Play.ClueScans.Add(new MysteryClueScan
        {
            CharacterId = characterId,
            ClueId = clueId,
            At = now
        });

        return true;
    }

    public static bool HasScanned(TripData trip, string characterId, string clueId) =>
        trip.Mystery.Play.ClueScans.Any(s => s.CharacterId == characterId && s.ClueId == clueId);

    /// <summary>Who got there first. Credited on the board, which makes hunting worth doing.</summary>
    public static string? FirstFinder(TripData trip, string clueId) =>
        trip.Mystery.Play.ClueScans
            .Where(s => s.ClueId == clueId)
            .OrderBy(s => s.At)
            .Select(s => s.CharacterId)
            .FirstOrDefault();

    public static bool IsFound(TripData trip, string clueId) => FirstFinder(trip, clueId) is not null;

    /// <summary>
    /// Who scanned this card and when, oldest first — or nothing at all before the trail opens.
    ///
    /// The gate lives here rather than in each caller, so a page that forgets to check gets an empty
    /// list instead of a map of everybody's evening.
    /// </summary>
    public static IReadOnlyList<MysteryClueScan> Trail(TripData trip, string clueId) =>
        PhaseService.TrailVisible(trip)
            ? trip.Mystery.Play.ClueScans.Where(s => s.ClueId == clueId).OrderBy(s => s.At).ToList()
            : [];

    // ------------------------------------------------------------------------------------------
    //  Tampering
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Work something into a card, or take something out of it.
    ///
    /// A card holds at most one tamper, and a second attempt is refused — which is what stops two
    /// jesters turning one clue into a pile of everybody's belongings. The check and the write are
    /// in the same method so that, called inside a mutation, two people pressing at once cannot
    /// both succeed.
    /// </summary>
    public static bool Tamper(
        TripData trip,
        string clueId,
        string mode,
        string byCharacterId,
        string? targetCharacterId,
        DateTimeOffset now)
    {
        var state = trip.Mystery.Play.StateFor(clueId);
        if (state is null || state.Tamper is not null) return false;

        state.Tamper = new MysteryTamper
        {
            Mode = mode,
            ByCharacterId = byCharacterId,

            // A scrub removes rather than adds, so it frames nobody.
            TargetCharacterId = mode == "scrub" ? null : targetCharacterId ?? byCharacterId,
            At = now
        };

        return true;
    }

    /// <summary>Has anything been touched? What the board announces, never saying which card.</summary>
    public static bool AnyTampering(TripData trip) =>
        trip.Mystery.Play.ClueStates.Any(s => s.Tamper is not null);

    /// <summary>
    /// The card as this particular person holds it.
    ///
    /// A tamper only shows to somebody who scanned <em>after</em> it happened. Getting there first
    /// means holding the original, which is the whole reason to walk fast — and it is derived from
    /// the two timestamps rather than snapshotted, so the copy on a phone and the truth in the
    /// document can never drift apart.
    /// </summary>
    public static string ReadingFor(TripData trip, string clueId, string characterId)
    {
        var scan = trip.Mystery.Play.ClueScans
            .FirstOrDefault(s => s.ClueId == clueId && s.CharacterId == characterId);

        return Compose(trip, clueId, scan?.At);
    }

    /// <summary>
    /// The card as the room last saw it.
    ///
    /// The board shows the latest-scanned version, so a lie only becomes public when somebody
    /// physically re-scans the card. Which means the room re-reads the whole feed and argues about
    /// what changed, and whoever wrote the early text down becomes suddenly valuable.
    /// </summary>
    public static string PublicReading(TripData trip, string clueId)
    {
        var latest = trip.Mystery.Play.ClueScans
            .Where(s => s.ClueId == clueId)
            .OrderByDescending(s => s.At)
            .FirstOrDefault();

        return Compose(trip, clueId, latest?.At);
    }

    /// <summary>The card as written, for the detectives' forensics and for the reveal.</summary>
    public static string OriginalReading(TripData trip, string clueId) =>
        MysteryText.Written(trip.Mystery.Story.Clue(clueId)?.Text) ?? "";

    private static string Compose(TripData trip, string clueId, DateTimeOffset? asOf)
    {
        var clue = trip.Mystery.Story.Clue(clueId);
        if (clue is null) return "";

        var text = MysteryText.Written(clue.Text) ?? "";

        var tamper = trip.Mystery.Play.StateFor(clueId)?.Tamper;
        if (tamper is null || asOf is null || tamper.At > asOf) return text;

        var beats = trip.Mystery.Story.Beats;

        if (tamper.Mode == "scrub")
            return MysteryText.Written(beats.TamperScrubbed) ?? text;

        var frame = tamper.Mode == "blatant" ? beats.TamperBlatant : beats.TamperSubtle;
        var insert = MysteryText.Written(
            trip.Mystery.Story.Character(tamper.TargetCharacterId ?? "")?.TamperInsert);

        // Nobody has written the frame or the belongings yet: show the card as it stands rather
        // than a sentence with a hole in it.
        if (MysteryText.IsPlaceholder(frame) || insert is null) return text;

        var added = frame!.Replace("{insert}", insert, StringComparison.Ordinal);
        return string.IsNullOrEmpty(text) ? added : text + " " + added;
    }
}
