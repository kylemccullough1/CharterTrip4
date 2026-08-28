using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>
/// The rules shared by Police Sketch, Pool Noodle Cups and Beer Run.
///
/// They differ only in what ends a round: Sketch has one team guess first, the other two count
/// what each team carried off. Both land in the same place — points into the trip's score log,
/// noted with the round they came from, and on to the next one.
///
/// A game here is the rounds it was set up with and nothing more. Ending level is a joint win —
/// the scoreboard has room for two names, and a tie-break round nobody asked for is worse than
/// one on the wall.
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
        game.CurrentCharacter = null;
        game.UsedCharacters.Clear();
    }

    /// <summary>
    /// Put a character up for this round. Recorded as used straight away so it cannot come up
    /// twice, even if the round is scored more than once.
    /// </summary>
    public static void PickCharacter(RoundGame game, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        game.CurrentCharacter = name;
        if (!game.UsedCharacters.Contains(name, StringComparer.OrdinalIgnoreCase))
            game.UsedCharacters.Add(name);
    }

    /// <summary>Sketch: one team guessed first and takes the round's points.</summary>
    public static void AwardRoundWinner(
        TripData trip, RoundGame game, string gameId, string teamId, DateTimeOffset now)
    {
        if (game.Phase != PartyGamePhase.Playing) return;

        var where = $"Round {game.Round}";
        var note = game.CurrentCharacter is { Length: > 0 } character
            ? $"{where} · {character}"
            : where;

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

        var where = $"Round {game.Round}";

        foreach (var (teamId, count) in counts)
        {
            if (count <= 0) continue;

            ScoreService.Award(
                trip, gameId, teamId, count * game.PointValue, $"{where} · {count} {unit}", now);
        }

        NextRound(game);
    }

    /// <summary>
    /// On to the next round — or, once the scheduled ones are done, to the final scores. Whatever
    /// the board says at that point is the result, a tie at the top included.
    /// </summary>
    public static void NextRound(RoundGame game)
    {
        if (game.Phase != PartyGamePhase.Playing) return;

        game.CurrentCharacter = null;

        if (game.Round < game.RoundCount)
        {
            game.Round++;
            return;
        }

        game.Phase = PartyGamePhase.Finished;
    }

    /// <summary>
    /// Back to the rules card with the points given back. Settings — the point value, the round
    /// count, the cast — survive: they are how the game is set up, not how it went.
    /// </summary>
    public static void Reset(TripData trip, RoundGame game, string gameId)
    {
        game.Phase = PartyGamePhase.NotStarted;
        game.Round = 1;
        game.CurrentCharacter = null;
        game.UsedCharacters.Clear();

        ScoreService.Clear(trip, gameId);
    }

    /// <summary>
    /// How many there are to go round this round, all teams together.
    ///
    /// For a game with a number to win this is not a choice. To be sure one corner reaches the
    /// number, the stack has to be more than every corner can hold one short of it — so at least
    /// (win - 1) x teams + 1. For only one corner to reach it, every other corner has to be able
    /// to stop one short — so at most win + (win - 1) x (teams - 1). Those are the same
    /// expression. Thirteen, for four beers to win across four corners, and nothing else.
    ///
    /// A game with no number to win is one each instead, which is where the cups started: four
    /// players, four cups, and the round is over when the last one is placed.
    /// </summary>
    public static int RoundPool(TripData trip, RoundGame game) =>
        game.TakeToWin is { } win && win > 0
            ? (win - 1) * trip.Teams.Count + 1
            : trip.Teams.Count;

    /// <summary>
    /// The most one team can be credited with in a round: the number that wins it, or — for a game
    /// with no such number — the whole stack, since nothing stops one team taking the lot.
    /// </summary>
    public static int MostOneTeamCanTake(TripData trip, RoundGame game) =>
        game.TakeToWin ?? RoundPool(trip, game);

    /// <summary>Characters not yet drawn this game.</summary>
    public static IReadOnlyList<SketchCharacter> RemainingCharacters(RoundGame game) =>
        game.Characters
            .Where(c => !game.UsedCharacters.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

    public static SketchCharacter? Find(RoundGame game, string? name) =>
        name is null
            ? null
            : game.Characters.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}
