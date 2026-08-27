using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>
/// The rules shared by Police Sketch, Pool Noodle Cups and Beer Run.
///
/// They differ only in what ends a round: Sketch has one team guess first, the other two count
/// what each team carried off. Both land in the same place — points into the trip's score log,
/// noted with the round they came from, and on to the next one.
///
/// No game here can end level. The last round finishing on a shared top score sends exactly the
/// teams who tied into sudden death, as many times as it takes.
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
        game.TieBreakTeamIds.Clear();
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

        var where = game.IsSuddenDeath ? "Sudden death" : $"Round {game.Round}";
        var note = game.CurrentCharacter is { Length: > 0 } character
            ? $"{where} · {character}"
            : where;

        ScoreService.Award(trip, gameId, teamId, game.PointValue, note, now);
        NextRound(trip, game, gameId);
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

        var where = game.IsSuddenDeath ? "Sudden death" : $"Round {game.Round}";

        foreach (var (teamId, count) in counts)
        {
            if (count <= 0) continue;

            ScoreService.Award(
                trip, gameId, teamId, count * game.PointValue, $"{where} · {count} {unit}", now);
        }

        NextRound(trip, game, gameId);
    }

    /// <summary>
    /// On to the next round — or, once the scheduled ones are done, to a winner. A shared top
    /// score is not a result, so it becomes a sudden-death round between whoever is level.
    ///
    /// Sudden death is judged on the running total rather than on the round alone, which comes
    /// to the same thing: the teams going into it are level by definition, so whatever separates
    /// them there separates them overall.
    /// </summary>
    public static void NextRound(TripData trip, RoundGame game, string gameId)
    {
        if (game.Phase != PartyGamePhase.Playing) return;

        game.CurrentCharacter = null;

        // An ordinary round with rounds still to play.
        if (!game.IsSuddenDeath && game.Round < game.RoundCount)
        {
            game.Round++;
            return;
        }

        var among = game.IsSuddenDeath
            ? game.TieBreakTeamIds.ToList()
            : trip.Teams.Select(t => t.Id).ToList();

        var leaders = TiedLeaders(trip, gameId, among);

        if (leaders.Count > 1)
        {
            game.TieBreakTeamIds = leaders;
            game.Round++;
            return;
        }

        game.TieBreakTeamIds.Clear();
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
        game.TieBreakTeamIds.Clear();

        ScoreService.Clear(trip, gameId);
    }

    /// <summary>
    /// Whose turn it is to be scored: everybody, or just the teams left standing in sudden death.
    /// </summary>
    public static IReadOnlyList<Team> ActiveTeams(TripData trip, RoundGame game) =>
        game.IsSuddenDeath
            ? trip.Teams.Where(t => game.TieBreakTeamIds.Contains(t.Id)).ToList()
            : trip.Teams;

    /// <summary>Characters not yet drawn this game.</summary>
    public static IReadOnlyList<SketchCharacter> RemainingCharacters(RoundGame game) =>
        game.Characters
            .Where(c => !game.UsedCharacters.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

    public static SketchCharacter? Find(RoundGame game, string? name) =>
        name is null
            ? null
            : game.Characters.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Everyone sharing the best score among <paramref name="among"/> — one name if somebody leads.</summary>
    private static List<string> TiedLeaders(TripData trip, string gameId, IReadOnlyList<string> among)
    {
        if (among.Count == 0) return [];

        var scores = among
            .Select(id => (Id: id, Score: ScoreService.ScoreFor(trip, gameId, id)))
            .ToList();

        var best = scores.Max(s => s.Score);
        return scores.Where(s => s.Score == best).Select(s => s.Id).ToList();
    }
}
