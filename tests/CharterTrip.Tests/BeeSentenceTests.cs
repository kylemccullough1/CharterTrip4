using CharterTrip.Core.Models;
using CharterTrip.Core.Words;

namespace CharterTrip.Tests;

/// <summary>
/// "Use it in a sentence" is the question a speller asks most and the one the bank answers least
/// — every Easy word has a real sentence and not one Expert word does. These are about the host
/// never being left with nothing to read.
/// </summary>
public class BeeSentenceTests
{
    private static BeeWord Word(string word, string pos = "", string sentence = "") =>
        new() { Id = "sb-1", Word = word, PartOfSpeech = pos, Sentence = sentence };

    [Fact]
    public void A_real_sentence_is_used_exactly_as_written()
    {
        var word = Word("rhythm", "noun", "The rhythm of the drums carried across the water.");

        Assert.Equal("The rhythm of the drums carried across the water.", BeeSentence.For(word));
        Assert.False(BeeSentence.IsImprovised(word));
    }

    [Fact]
    public void A_word_with_no_sentence_still_gets_one()
    {
        var word = Word("ecchymosis", "noun");

        var sentence = BeeSentence.For(word);

        Assert.Contains("ecchymosis", sentence);
        Assert.True(BeeSentence.IsImprovised(word));
        Assert.EndsWith(".", sentence);
    }

    /// <summary>
    /// Every part of speech the bank uses — and the ones it does not, and none at all — has to
    /// produce a real sentence with the word in it rather than falling through to an empty frame.
    /// </summary>
    [Theory]
    [InlineData("noun")]
    [InlineData("verb")]
    [InlineData("adjective")]
    [InlineData("adverb")]
    [InlineData("interjection")]
    [InlineData("")]
    public void Every_part_of_speech_produces_a_sentence_with_the_word_in_it(string partOfSpeech)
    {
        var sentence = BeeSentence.For(Word("floruit", partOfSpeech));

        Assert.Contains("floruit", sentence);
        Assert.EndsWith(".", sentence);
        Assert.DoesNotContain("{0}", sentence);
    }

    /// <summary>A verb is framed as a verb, not as a thing you can have one of.</summary>
    [Fact]
    public void The_frame_fits_the_part_of_speech()
    {
        var asVerb = BeeSentence.For(Word("expiscate", "verb"));
        var asNoun = BeeSentence.For(Word("expiscate", "noun"));

        Assert.NotEqual(asVerb, asNoun);
    }

    /// <summary>
    /// The same word has to frame the same way every time. It is drawn on the host's phone, and a
    /// sentence that changed on each render would be unreadable to somebody halfway through
    /// saying it out loud.
    /// </summary>
    [Fact]
    public void The_same_word_always_gets_the_same_sentence()
    {
        var first = BeeSentence.For(Word("nacelle", "noun"));

        for (var i = 0; i < 5; i++) Assert.Equal(first, BeeSentence.For(Word("nacelle", "noun")));
    }

    [Fact]
    public void Different_words_do_not_all_get_the_same_frame()
    {
        var words = new[] { "nacelle", "sauger", "damson", "ullage", "notturno", "floruit", "decastich" };

        var shapes = words
            .Select(w => BeeSentence.For(Word(w, "noun")).Replace(w, "{0}"))
            .Distinct()
            .Count();

        Assert.True(shapes > 1, "every word came out in the same frame");
    }

    [Fact]
    public void A_word_with_nothing_in_it_gets_nothing_back()
    {
        Assert.Equal("", BeeSentence.For(Word("")));
    }

    /// <summary>
    /// The real bank, walked end to end: every word the bee can deal has a sentence to read.
    /// </summary>
    [Fact]
    public void Every_word_in_the_bank_has_something_to_read()
    {
        foreach (var tier in WordBank.Tiers)
        {
            foreach (var entry in WordBank.Pool(tier.Key))
            {
                var word = new BeeWord
                {
                    Word = entry.Word,
                    PartOfSpeech = entry.PartOfSpeech,
                    Sentence = entry.Sentence
                };

                var sentence = BeeSentence.For(word);
                Assert.False(string.IsNullOrWhiteSpace(sentence), $"{entry.Word} has nothing to read");
            }
        }
    }
}
