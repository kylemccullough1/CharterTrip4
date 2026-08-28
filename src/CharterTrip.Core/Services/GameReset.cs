using CharterTrip.Core.Models;
using CharterTrip.Core.Mystery;

namespace CharterTrip.Core.Services;

/// <summary>
/// Putting the weekend back to before anybody played anything.
///
/// Every game that can be played on the site keeps live state, and no two keep it in the same
/// shape — Jeopardy's is a board of used clues and buzzer codes, the mystery's is a cast, a pile
/// of scans and three trials, the bee's is a row of spellers and a rules slideshow, the four
/// played on their feet each hold a round number — and the points from all of them land in one
/// shared log. Clearing all of that meant finding a button on every one of those screens and
/// knowing the last one existed.
///
/// A game added to the site is not added to this by itself, and the failure mode is quiet: the
/// button still says it reset everything. Both halves have to be told — <see cref="All"/> so the
/// state actually goes, and <see cref="WhatWouldGo"/> so the button is not sitting there disabled
/// because it cannot see the one thing that is live.
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

        // The same, and it takes the rules slideshow down with it. That last part is what makes
        // this the bee's only way out of a particular corner: the slideshow is driven entirely
        // from the host's phone, and the wall renders it instead of the title card — so a bee left
        // on rule one with nobody still holding that phone has no button anywhere that ends it.
        SpellingBeeService.Reset(trip, random);

        // Phase back to the lobby and the played half replaced. The story is untouched.
        CastingService.Discard(trip);

        // The four played on their feet. Their points leave with the Clear below either way; what
        // these take is the half a Clear cannot reach — which round Police Sketch is on and the
        // faces it has already spent, the relay's finished clocks.
        RoundGameService.Reset(trip, trip.Party.Sketch, RoundGameService.SketchId);
        RoundGameService.Reset(trip, trip.Party.NoodleCup, RoundGameService.NoodleCupId);
        RoundGameService.Reset(trip, trip.Party.BeerRun, RoundGameService.BeerRunId);
        RelayService.Reset(trip, trip.Party.Relay);

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

        var bee = trip.SpellingBee.Game;

        if (bee.Phase != BeePhase.NotStarted)
        {
            lines.Add($"The spelling bee is running with {Count(bee.Order.Count, "speller")} in the row. "
                      + "Both codes are reissued, so the phones sign in again.");
        }
        else if (bee.RuleSlide >= 0)
        {
            // Its own line, because this is the state somebody is most likely to be looking at
            // while they press the button — the wall renders the slideshow instead of the title
            // card, so a bee stuck on rule one looks like a bee that will not start.
            lines.Add($"The spelling bee has rule {bee.RuleSlide + 1} up on the wall. "
                      + "The slideshow comes down and the wall goes back to its join codes.");
        }
        else if (bee.Ready.Count > 0)
        {
            lines.Add($"{Count(bee.Ready.Count, "speller")} signed into the bee. "
                      + "Both codes are reissued, so the phones sign in again.");
        }

        // One line for the four rather than four lines, because they are one game with four sets
        // of rules and a host who reset the weekend does not need it itemised.
        var standing = new (string Name, PartyGamePhase Phase)[]
        {
            ("Police Sketch", trip.Party.Sketch.Phase),
            ("the noodle cups", trip.Party.NoodleCup.Phase),
            ("the beer run", trip.Party.BeerRun.Phase),
            ("the relay", trip.Party.Relay.Phase),
        }.Where(g => g.Phase != PartyGamePhase.NotStarted).Select(g => g.Name).ToList();

        if (standing.Count > 0)
        {
            lines.Add(standing.Count == 1
                ? $"{Sentence(standing)} is part way through. It goes back to its rules card; "
                  + "the point values and the cast are kept."
                : $"{Sentence(standing)} are part way through. Each goes back to its rules card; "
                  + "the point values and the cast are kept.");
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

    /// <summary>A list of names as somebody would say it: "a, b and c".</summary>
    private static string Sentence(IReadOnlyList<string> names) =>
        names.Count switch
        {
            0 => "",
            1 => names[0],
            _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}"
        };

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
