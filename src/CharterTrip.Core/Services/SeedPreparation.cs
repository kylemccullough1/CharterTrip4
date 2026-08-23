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
/// authored survives: the itinerary, the roster, the Jeopardy board, the mystery cast and their
/// secrets. Everything earned or issued does not: scores, buzzer codes, which clues have been
/// used, which round the mystery is on. A seed that restored a half-finished game would be worse
/// than useless — it would put a stale scoreboard back on the wall.
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

        var mystery = trip.Mystery;
        mystery.Active = false;
        mystery.CastRevealed = false;
        mystery.VotingOpen = false;
        mystery.CurrentRound = -1;

        // Clue cards are written by the host, so they stay — but a released clue is a played one.
        foreach (var clue in mystery.Clues)
            clue.Released = false;
    }
}
