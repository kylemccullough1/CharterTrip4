using CharterTrip.Core.Models;

namespace CharterTrip.Core.Words;

/// <summary>
/// "Use it in a sentence", for the two thousand words the dictionary never gave one to.
///
/// The bank's sentences come from Tatoeba and Wiktionary, which have real ones written by real
/// people — for ordinary words. Coverage falls off a cliff exactly where the bee gets
/// interesting: nearly every Easy word has a sentence and not one Expert word does, because
/// nobody has ever written a Tatoeba sentence containing <em>ecchymosis</em>.
///
/// A pronouncer at a real bee does not refuse the question, so neither does this. Where the bank
/// has a sentence it is used unchanged; where it does not, one is built out of the part of speech
/// the bank <em>does</em> know, so the host always has something to say instead of "there isn't
/// one for this one, sorry".
///
/// These are deliberately hollow — a frame the word sits in rather than a sentence about what it
/// means — which is the correct trade here: the speller is entitled to hear it used, not to be
/// told what it is. That is what the definition line above it is for.
/// </summary>
public static class BeeSentence
{
    /// <summary>Whether the sentence for this word is ours rather than the dictionary's.</summary>
    public static bool IsImprovised(BeeWord word) => string.IsNullOrWhiteSpace(word.Sentence);

    /// <summary>Something to read out, always.</summary>
    public static string For(BeeWord word) =>
        word.IsEmpty ? "" :
        !string.IsNullOrWhiteSpace(word.Sentence) ? word.Sentence :
        Compose(word.Word, word.PartOfSpeech);

    /// <summary>
    /// One frame, picked by the word rather than at random, so a host who taps back and forth
    /// hears the same sentence twice rather than a different one each time.
    /// </summary>
    private static string Compose(string word, string partOfSpeech)
    {
        var frames = Frames(partOfSpeech);
        return string.Format(frames[Pick(word, frames.Length)], word);
    }

    private static string[] Frames(string partOfSpeech) => partOfSpeech.Trim().ToLowerInvariant() switch
    {
        "verb" =>
        [
            "They were about to {0} when somebody finally stopped them.",
            "Do not {0} until everyone at the table is ready.",
            "She would {0} all afternoon if you let her."
        ],

        "adjective" =>
        [
            "It was the most {0} thing anybody saw all weekend.",
            "Nobody expected the evening to turn quite so {0}.",
            "He is {0} in a way you cannot teach."
        ],

        "adverb" =>
        [
            "He answered {0}, and that was the end of it.",
            "The whole thing came apart {0}.",
            "She took the last one {0}, in front of everybody."
        ],

        "noun" =>
        [
            "Nobody in this room could pick a {0} out of a lineup.",
            "There was exactly one {0} left by the end of the night.",
            "You do not see a {0} like that every day."
        ],

        // No part of speech either, or something the two sources disagreed about. These work
        // whatever the word turns out to be, because the word is being talked about rather than
        // used — which is the honest thing to do when we do not know how to use it.
        _ =>
        [
            "I have never once heard anybody use the word {0} in a sentence.",
            "Somebody at this table has definitely said {0} out loud this weekend.",
            "The whole argument came down to the word {0}."
        ]
    };

    /// <summary>
    /// A stable index from the word itself. Deliberately not <c>string.GetHashCode</c>, which is
    /// randomised per process — the same word would frame differently on the host's phone and on
    /// anything else that asked.
    /// </summary>
    private static int Pick(string word, int count)
    {
        var sum = 0;
        foreach (var c in word) sum = (sum * 31 + char.ToLowerInvariant(c)) % 100003;

        return sum % count;
    }
}
