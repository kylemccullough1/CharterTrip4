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

    /// <summary>A Jeopardy buzzer code, shared by everyone on that team.</summary>
    BuzzerTeam,

    /// <summary>The Jeopardy host code. A job, not a person.</summary>
    BuzzerHost
}

public readonly record struct CodeMatch(CodeKind Kind, string? PersonId = null, string? TeamId = null)
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

        // The party code, which is a door rather than an identity.
        if (trip.Mystery.PartyCode is { Length: > 0 } party &&
            string.Equals(Clean(party), cleaned, StringComparison.OrdinalIgnoreCase))
        {
            return new CodeMatch(CodeKind.MysteryParty);
        }

        if (JeopardyService.TeamForCode(trip, cleaned) is { } teamId)
            return new CodeMatch(CodeKind.BuzzerTeam, TeamId: teamId);

        if (JeopardyService.IsHostCode(trip, cleaned))
            return new CodeMatch(CodeKind.BuzzerHost);

        return CodeMatch.None;
    }

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
