using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Core.Words;

/// <summary>
/// Draws words out of <see cref="WordBank"/>, one at a time.
///
/// There is no deck. A hand dealt before the bee starts would fix its difficulty curve before
/// anybody had spelled anything, and the one thing the host actually wants to change while the
/// room plays is exactly that — so a word is drawn when a turn needs one, at whatever tier the
/// host has the dial set to right now.
/// </summary>
public static class WordDeck
{
    /// <summary>
    /// Where the dial starts. The two school-list tiers below this are warm-ups nobody would be
    /// eliminated by, and twenty-five adults opening Saturday with a bee want to be made to
    /// think — so Moderate, and the host moves it from there.
    /// </summary>
    public const string DefaultDifficulty = "moderate";

    /// <summary>
    /// One word from <paramref name="tierKey"/> that is not in <paramref name="used"/>, or null
    /// if there is nothing left anywhere.
    ///
    /// A tier that is spent falls outwards to its neighbours — nearest first, easier before
    /// harder at equal distance — rather than ending the bee. Two of the six tiers hold under a
    /// hundred and thirty words between them, so "Difficult, and only Difficult" is a setting a
    /// long bee can genuinely exhaust, and running out of words is not a thing that should be
    /// allowed to happen in front of the room.
    /// </summary>
    public static BeeWord? Draw(string tierKey, IReadOnlySet<string> used, Random random)
    {
        foreach (var key in TiersOutwardFrom(tierKey))
        {
            var pool = WordBank.Pool(key).Where(e => !used.Contains(e.Word)).ToList();
            if (pool.Count == 0) continue;

            return Card(pool[random.Next(pool.Count)], key);
        }

        return null;
    }

    /// <summary>
    /// The tier asked for, then the rest by how far they are from it. Ties go to the easier one,
    /// because a bee that has run out of Expert words should get less brutal, not more.
    /// </summary>
    public static IEnumerable<string> TiersOutwardFrom(string tierKey)
    {
        var keys = WordBank.Tiers.Select(t => t.Key).ToList();
        var from = keys.IndexOf(tierKey);
        if (from < 0) from = keys.IndexOf(DefaultDifficulty);
        if (from < 0) from = 0;

        return keys
            .Select((key, i) => (key, distance: Math.Abs(i - from), harder: i > from))
            .OrderBy(x => x.distance).ThenBy(x => x.harder)
            .Select(x => x.key);
    }

    /// <summary>
    /// The tier one step easier or harder, clamped at both ends. What the host's two buttons do.
    /// </summary>
    public static string Shift(string tierKey, int steps)
    {
        var keys = WordBank.Tiers.Select(t => t.Key).ToList();
        var at = keys.IndexOf(tierKey);
        if (at < 0) at = Math.Max(0, keys.IndexOf(DefaultDifficulty));

        return keys[Math.Clamp(at + steps, 0, keys.Count - 1)];
    }

    /// <summary>One drawn word, carrying whatever the bank knows about it.</summary>
    private static BeeWord Card(BankEntry entry, string tierKey) => new()
    {
        Id = Ids.New("sb"),
        Word = entry.Word,
        TierKey = tierKey,
        Definition = entry.Definition,
        PartOfSpeech = entry.PartOfSpeech,
        Sentence = entry.Sentence
    };

    /// <summary>Fisher–Yates over a copy, so nothing handed in is ever mutated.</summary>
    public static List<T> Shuffle<T>(IReadOnlyList<T> source, Random random)
    {
        var a = source.ToList();

        for (var i = a.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }

        return a;
    }
}
