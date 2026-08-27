using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>
/// Turns a live trip into a seed.
///
/// The seed is the floor the app lands on when it starts with no data file. A seed frozen at the
/// first commit makes that landing brutal — every itinerary change since is gone — so it is meant
/// to be refreshed from the real trip as the weekend gets planned.
///
/// The line drawn here is between what the host wrote and what happened during play. Everything
/// authored survives: the itinerary, the roster, the Jeopardy board. Everything earned or issued
/// does not: scores, buzzer codes, join tokens, and the whole of the murder mystery. A seed that
/// restored a half-finished game would be worse than useless — it would put a stale scoreboard
/// back on the wall.
///
/// "Issued" is the important half, because this file goes into git. Buzzer codes were always
/// cleared here; join tokens have to be too, and for a stronger reason — a buzzer code is worth
/// one evening's mischief, while a join token is somebody's identity for the weekend. Twenty-five
/// of them committed to a repository is twenty-five accounts anybody who can read it can use.
/// </summary>
public static class SeedPreparation
{
    /// <summary>
    /// Strip a loaded trip back to a publishable seed, in place.
    /// <paramref name="stamp"/> becomes the seed's UpdatedUtc, so the file says when it was taken.
    /// </summary>
    public static void Prepare(TripData trip, DateTimeOffset stamp)
    {
        ArgumentNullException.ThrowIfNull(trip);

        // A seed is the starting point by definition, whatever revision it was captured at.
        trip.Revision = 0;
        trip.UpdatedUtc = stamp;

        // Points are awarded during play and live only in this log. Carrying them into a seed
        // would hand every team a scoreboard they did not earn the next time the app reset.
        trip.Scores.Clear();

        // The board is content; the game on top of it is not.
        trip.Jeopardy.Game = new JeopardyGame();

        // The murder mystery keeps nothing. Everything in it — the cast, the guilty list, the clue
        // layout, the votes — is generated from the script and a seed at deal time, so there is no
        // authored half to preserve. This used to be a field-by-field reset because the old game's
        // characters and clue cards were typed in by hand.
        trip.Mystery = new MysteryState();

        // Issued identity, not authored content, and this file is committed. See the class note.
        foreach (var person in trip.Roster)
            person.JoinToken = null;
    }
}
