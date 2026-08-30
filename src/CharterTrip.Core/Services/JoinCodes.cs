using System.Security.Cryptography;
using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>What a code turned out to be.</summary>
public enum CodeKind
{
    /// <summary>Nothing on this trip matches. Never say which of the three it failed to match.</summary>
    Unknown,

    /// <summary>A roster person's own link — this is how somebody becomes themselves.</summary>
    Person,

    /// <summary>
    /// The murder mystery's one party code.
    ///
    /// Unlike every other kind, this signs nobody in on its own — it only proves you are in the
    /// house. Who you are is the next question, and the answer comes from tapping your own name.
    /// </summary>
    MysteryParty,

    /// <summary>
    /// Jeopardy's one code, which is the code on the wall.
    ///
    /// It used to be one code per team, and typing it *was* being that team. Now it is a door like
    /// the other two: which buzzer you get is decided by the name you tap, and that comes off the
    /// roster — so there is no team-shaped input for anybody to get wrong.
    /// </summary>
    BuzzerParty,

    /// <summary>
    /// The spelling bee's guest code, which is the code on the wall.
    ///
    /// A door, exactly like the mystery's: it proves you are in the room and says nothing about
    /// who you are. Tapping your own name off the list is the rest of it, and it is also how you
    /// join the bee — being in the row and being signed in are one act.
    /// </summary>
    BeeParty,

    /// <summary>
    /// One of the murder mystery's nine clue cards, by the number printed under its QR. Opens the
    /// same page the QR does; signs nobody in.
    /// </summary>
    Clue
}

public readonly record struct CodeMatch(CodeKind Kind, string? PersonId = null, string? TeamId = null, string? ClueToken = null)
{
    public static readonly CodeMatch None = new(CodeKind.Unknown);
    public bool Found => Kind != CodeKind.Unknown;
}

/// <summary>
/// One front door for every code on this trip.
///
/// There used to be a per-game entrance: /buzz/{code} resolved a Jeopardy team and nothing else
/// could use it. That does not survive a second game — the murder mystery needs each of 21 people
/// to be a distinct person on their own phone, not a team sharing a code. So codes resolve here
/// instead, and the route that consumes this is /join/{code} for all of them.
///
/// Resolution order matters: a person's own token wins over anything else, because being yourself
/// is the identity every game can derive from. Somebody signed in as themselves already has a team.
/// </summary>
public static class JoinCodes
{
    /// <summary>
    /// No O/0, I/1, S/5, B/8 — the same alphabet the buzzer codes use, for the same reason: these
    /// get read off a screen or a name tag and typed by somebody holding a drink.
    /// </summary>
    private const string Alphabet = "ACDEFGHJKMNPQRTUVWXY34679";

    /// <summary>
    /// Ten characters, where a buzzer code is four.
    ///
    /// A buzzer code is a room-scoped convenience that is reset between games and shown on a wall.
    /// A join token is a bearer credential: it is the whole proof that somebody is who they say
    /// they are, it lasts the weekend, and it lives in a link that gets printed on a badge. Four
    /// characters is 390,000 guesses — a script gets through that. Ten is about 10^14, and still
    /// short enough to type off a card if a camera will not focus.
    /// </summary>
    private const int TokenLength = 10;

    /// <summary>
    /// Give everybody on the roster a join token who does not have one, and report whether
    /// anything changed — the same shape as <see cref="JeopardyService.EnsureCodes"/>, so a caller
    /// can skip a write when there was nothing to do.
    ///
    /// Existing tokens are never reissued. Somebody who has already followed their link, or whose
    /// badge is already printed, keeps working.
    /// </summary>
    public static bool EnsureTokens(TripData trip)
    {
        var issued = trip.Roster
            .Where(p => !string.IsNullOrWhiteSpace(p.JoinToken))
            .Select(p => p.JoinToken!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changed = false;

        foreach (var person in trip.Roster.Where(p => string.IsNullOrWhiteSpace(p.JoinToken)))
        {
            string token;
            do
            {
                token = NewToken();
            }
            while (!issued.Add(token));   // vanishingly unlikely, cheap to rule out

            person.JoinToken = token;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Work out what a typed or scanned code is.
    ///
    /// Case-insensitive, and tolerant of the spaces and dashes somebody adds when copying a code
    /// off a card. Returns <see cref="CodeMatch.None"/> for anything unrecognised — the caller
    /// must not tell the visitor which kind of code it failed to be.
    /// </summary>
    public static CodeMatch Resolve(TripData trip, string? code)
    {
        var cleaned = Clean(code);
        if (cleaned.Length == 0) return CodeMatch.None;

        // A person's own token first. Being yourself outranks being a seat at a table, and every
        // game can work out the rest from it.
        var person = trip.Roster.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(p.JoinToken) &&
            string.Equals(Clean(p.JoinToken), cleaned, StringComparison.OrdinalIgnoreCase));

        if (person is not null)
            return new CodeMatch(CodeKind.Person, PersonId: person.Id, TeamId: NullIfBlank(person.TeamId));

        // Then the doors. Each of these proves you are in the room and nothing else; who you are is
        // the next question, and the answer is choosing your own name on the far side.
        //
        // There used to be a host code per game, resolved above these so that a four-character
        // collision could never read as "the guest code happened to match the host's, so here is
        // the word list". There is one code per game now and the host job is offered behind it to
        // a browser signed in as an organizer, so both the second code and the ordering rule that
        // protected it have gone.
        if (Matches(trip.Mystery.Play.PartyCode, cleaned)) return new CodeMatch(CodeKind.MysteryParty);

        // A clue's number. One digit cannot collide with a four-character door or a ten-character
        // token, so the order here is only for reading.
        var clue = trip.Mystery.Play.ClueStates.FirstOrDefault(s => Matches(s.Token, cleaned));
        if (clue is not null) return new CodeMatch(CodeKind.Clue, ClueToken: clue.Token);
        if (SpellingBeeService.IsGuestCode(trip, cleaned)) return new CodeMatch(CodeKind.BeeParty);

        if (JeopardyService.IsPartyCode(trip, cleaned)) return new CodeMatch(CodeKind.BuzzerParty);

        return CodeMatch.None;
    }

    private static bool Matches(string? code, string cleaned) =>
        code is { Length: > 0 } && string.Equals(Clean(code), cleaned, StringComparison.OrdinalIgnoreCase);

    /// <summary>The link to hand one person, for the print sheet and the roster admin page.</summary>
    public static string PathFor(RosterPerson person) => $"/join/{person.JoinToken}";

    private static string NewToken() =>
        new(Enumerable.Range(0, TokenLength)
            .Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)])
            .ToArray());

    /// <summary>Strip what a human adds by hand, and nothing else.</summary>
    private static string Clean(string? code) =>
        code is null
            ? ""
            : new string(code.Where(char.IsLetterOrDigit).ToArray());

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
