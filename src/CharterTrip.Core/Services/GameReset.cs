using CharterTrip.Core.Models;
using CharterTrip.Core.Mystery;

namespace CharterTrip.Core.Services;

/// <summary>
/// Putting the weekend back to before anybody played anything.
///
/// Two games keep live state and they keep it in completely different shapes — Jeopardy's is a
/// board of used clues and buzzer codes, the mystery's is a cast, a pile of scans and three trials
/// — and the points from every game land in one shared log. Clearing all of that meant finding
/// three buttons on three screens and knowing the third one existed.
///
/// Written content is never touched. The Jeopardy board's clues and the murder mystery's story are
/// somebody's work, and they survive this exactly the way they survive a discard: what goes is one
/// night of playing them.
/// </summary>
public static class GameReset
{
    /// <summary>
    /// Wipe every game's play state and empty the standings.
    ///
    /// Meant to be called inside a mutation announced as <c>TripArea.All</c>, because it touches
    /// three areas at once and a board open in another room has to hear about all three.
    /// </summary>
    public static void All(TripData trip, Random random)
    {
        // Back to the title card, with fresh codes so a phone left connected from the last game
        // cannot buzz into the new one. Clears the Jeopardy score entries as part of its contract.
        JeopardyService.Reset(trip, random);

        // Phase back to the lobby and the played half replaced. The story is untouched.
        CastingService.Discard(trip);

        // Everything else that ever awarded a point. Jeopardy's are already gone; this is the
        // relay, the cornhole and whatever else gets a scoring widget — and it is a Clear rather
        // than a list of game ids, so a game added later cannot quietly leave its points behind.
        trip.Scores.Clear();
    }

    /// <summary>
    /// What a reset would actually take away, in sentences, or nothing at all.
    ///
    /// One press stands between a host and a weekend of scores, so the screen says what there is to
    /// lose rather than asking somebody to remember. Empty means the button has nothing to do,
    /// which is also worth showing.
    /// </summary>
    public static IReadOnlyList<string> WhatWouldGo(TripData trip)
    {
        var lines = new List<string>();

        var jeopardy = trip.Jeopardy.Game;
        var played = jeopardy.UsedClueIds.Count;

        if (played > 0 || jeopardy.Phase != JeopardyPhase.NotStarted)
        {
            lines.Add($"Jeopardy has been played — {Count(played, "clue")} gone from the board. "
                      + "Every buzzer code is reissued, so the phones sign in again.");
        }
        else if (jeopardy.JoinedTeamIds.Count > 0 || jeopardy.HostJoined)
        {
            lines.Add($"{Count(jeopardy.JoinedTeamIds.Count, "Jeopardy buzzer")} signed in. "
                      + "Their codes are reissued, so the phones sign in again.");
        }

        var mystery = trip.Mystery;

        if (mystery.Phase != MysteryPhase.Lobby || mystery.Play.PartyCode.Length > 0)
        {
            var seated = mystery.Play.Cast.Count(c => c.PersonId is not null);

            lines.Add($"The murder mystery is open at {Midsentence(MysteryPhases.Label(mystery.Phase))}, "
                      + $"with {Count(seated, "part")} taken. Both door codes change; the story is kept.");
        }

        if (trip.Scores.Count > 0)
        {
            lines.Add($"{trip.Scores.Sum(s => s.Points)} points across "
                      + $"{Count(trip.Scores.Count, "entry", "entries")} leave the standings.");
        }

        return lines;
    }

    /// <summary>Whether there is anything for the button to do.</summary>
    public static bool AnythingToClear(TripData trip) => WhatWouldGo(trip).Count > 0;

    private static string Count(int n, string singular, string? plural = null) =>
        n == 1 ? $"1 {singular}" : $"{n} {plural ?? singular + "s"}";

    /// <summary>
    /// A phase label dropped into the middle of a sentence.
    ///
    /// The labels are written to head a screen, so they open with a capital — "First trial", "The
    /// party". Read halfway through a sentence that reads as a stutter, and none of them is a
    /// proper noun.
    /// </summary>
    private static string Midsentence(string label) =>
        label.Length == 0 ? label : char.ToLowerInvariant(label[0]) + label[1..];
}
