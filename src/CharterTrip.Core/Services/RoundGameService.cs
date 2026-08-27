using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>
/// The rules shared by Police Sketch, Pool Noodle Cups and Beer Run.
///
/// They differ only in what ends a round: Sketch has one team guess first, the other two count
/// what each team carried off. Both land in the same place — points into the trip's score log,
/// noted with the round they came from, and on to the next one.
/// </summary>
public static class RoundGameService
{
    public const string SketchId = "sketch";
    public const string NoodleCupId = "noodlecup";
    public const string BeerRunId = "beerrun";

    public static void Begin(RoundGame game)
    {
        game.Phase = PartyGamePhase.Playing;
        game.Round = 1;
        game.CurrentPrompt = null;
        game.UsedPrompts.Clear();
    }

    /// <summary>
    /// Put a character up for this round. Recorded as used straight away so it cannot come up
    /// twice, even if the round is scored more than once.
    /// </summary>
    public static void PickPrompt(RoundGame game, string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return;

        game.CurrentPrompt = prompt;
        if (!game.UsedPrompts.Contains(prompt, StringComparer.OrdinalIgnoreCase))
            game.UsedPrompts.Add(prompt);
    }

    /// <summary>Sketch: one team guessed first and takes the round's points.</summary>
    public static void AwardRoundWinner(
        TripData trip, RoundGame game, string gameId, string teamId, DateTimeOffset now)
    {
        if (game.Phase != PartyGamePhase.Playing) return;

        var note = game.CurrentPrompt is { Length: > 0 } prompt
            ? $"Round {game.Round} · {prompt}"
            : $"Round {game.Round}";

        ScoreService.Award(trip, gameId, teamId, game.PointValue, note, now);
        NextRound(game);
    }

    /// <summary>
    /// Cups and beers: everyone scores what they carried off. A team that got none is not
    /// written down at all — a row worth zero points is noise in the undo list.
    /// </summary>
    public static void AwardRoundCounts(
        TripData trip,
        RoundGame game,
        string gameId,
        IReadOnlyDictionary<string, int> counts,
        string unit,
        DateTimeOffset now)
    {
        if (game.Phase != PartyGamePhase.Playing) return;

        var round = game.Round;

        foreach (var (teamId, count) in counts)
        {
            if (count <= 0) continue;

            ScoreService.Award(
                trip, gameId, teamId, count * game.PointValue, $"Round {round} · {count} {unit}", now);
        }

        NextRound(game);
    }

    /// <summary>On past the last round is the end of the game, not round seven.</summary>
    public static void NextRound(RoundGame game)
    {
        if (game.Phase != PartyGamePhase.Playing) return;

        game.CurrentPrompt = null;

        if (game.Round >= game.RoundCount)
        {
            game.Phase = PartyGamePhase.Finished;
            return;
        }

        game.Round++;
    }

    /// <summary>
    /// Back to the rules card with the points given back. Settings — the point value, the round
    /// count, the character list — survive: they are how the game is set up, not how it went.
    /// </summary>
    public static void Reset(TripData trip, RoundGame game, string gameId)
    {
        game.Phase = PartyGamePhase.NotStarted;
        game.Round = 1;
        game.CurrentPrompt = null;
        game.UsedPrompts.Clear();

        ScoreService.Clear(trip, gameId);
    }

    /// <summary>Characters not yet drawn this game.</summary>
    public static IReadOnlyList<string> RemainingPrompts(RoundGame game) =>
        game.Prompts
            .Where(p => !game.UsedPrompts.Contains(p, StringComparer.OrdinalIgnoreCase))
            .ToList();
}
