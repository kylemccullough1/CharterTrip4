using CharterTrip.Core.Models;
using CharterTrip.Core.Words;

namespace CharterTrip.Tests;

/// <summary>
/// The picking logic ported from the Word Deck tool. The bank itself is an embedded resource
/// rather than seed data, so these are also what stops the file going missing from the build
/// without anybody noticing until the bee opens on Saturday with nothing to read.
/// </summary>
public class WordDeckTests
{
    private static Random Rng(int seed = 42) => new(seed);

    // ------------------------------------------------------------------- bank

    [Fact]
    public void The_bank_is_embedded_and_every_tier_has_words()
    {
        Assert.Equal(6, WordBank.Tiers.Count);
        Assert.All(WordBank.Tiers, t => Assert.NotEmpty(WordBank.Pool(t.Key)));
        Assert.Equal(3850, WordBank.TotalWords);
    }

    [Fact]
    public void The_tiers_run_easiest_first()
    {
        Assert.Equal(
            ["easy", "easyModerate", "moderate", "moderatelyDifficult", "difficult", "expert"],
            WordBank.Tiers.Select(t => t.Key));
    }

    [Fact]
    public void An_unknown_tier_is_empty_rather_than_an_exception()
    {
        Assert.Empty(WordBank.Pool("moderatly-dificult"));
        Assert.False(WordBank.IsTier("moderatly-dificult"));
    }

    [Fact]
    public void No_word_appears_twice_inside_a_tier()
    {
        foreach (var tier in WordBank.Tiers)
        {
            var pool = WordBank.Pool(tier.Key);
            Assert.Equal(pool.Count, pool.Select(e => e.Word).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    // ------------------------------------------------------------------ draw

    [Fact]
    public void A_draw_comes_out_of_the_tier_it_was_asked_for()
    {
        var word = WordDeck.Draw("expert", new HashSet<string>(), Rng());

        Assert.NotNull(word);
        Assert.Equal("expert", word!.TierKey);
        Assert.Contains(WordBank.Pool("expert"), e => e.Word == word.Word);
        Assert.NotEmpty(word.Id);
    }

    [Fact]
    public void A_draw_carries_whatever_the_bank_knows_about_the_word()
    {
        // Picked out of the tier with the best coverage, so this is testing the plumbing rather
        // than the dictionary's gaps.
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        BeeWord? withHelp = null;

        for (var i = 0; i < 40 && withHelp is null; i++)
        {
            var drawn = WordDeck.Draw("easyModerate", used, Rng(i));
            if (drawn is null) break;

            used.Add(drawn.Word);
            if (drawn.HasHelp) withHelp = drawn;
        }

        Assert.NotNull(withHelp);

        var entry = WordBank.Pool("easyModerate").First(e => e.Word == withHelp!.Word);
        Assert.Equal(entry.Definition, withHelp!.Definition);
        Assert.Equal(entry.PartOfSpeech, withHelp.PartOfSpeech);
        Assert.Equal(entry.Sentence, withHelp.Sentence);
    }

    [Fact]
    public void A_word_already_used_is_never_drawn_again()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Drain a whole tier one word at a time. Every draw has to be new, and the tier has to
        // hand over exactly what it holds before it starts reaching elsewhere.
        for (var i = 0; i < WordBank.Pool("difficult").Count; i++)
        {
            var word = WordDeck.Draw("difficult", used, Rng(i));

            Assert.NotNull(word);
            Assert.Equal("difficult", word!.TierKey);
            Assert.True(used.Add(word.Word), $"{word.Word} came round twice");
        }
    }

    [Fact]
    public void An_emptied_tier_falls_out_to_its_neighbours_rather_than_giving_up()
    {
        var used = WordBank.Pool("difficult").Select(e => e.Word).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var word = WordDeck.Draw("difficult", used, Rng());

        Assert.NotNull(word);
        Assert.NotEqual("difficult", word!.TierKey);
    }

    [Fact]
    public void With_the_whole_bank_spent_there_is_nothing_to_draw()
    {
        var used = WordBank.Tiers
            .SelectMany(t => WordBank.Pool(t.Key))
            .Select(e => e.Word)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Null(WordDeck.Draw("moderate", used, Rng()));
    }

    [Fact]
    public void An_unknown_tier_draws_at_the_default_difficulty()
    {
        var word = WordDeck.Draw("moderatly-dificult", new HashSet<string>(), Rng());

        Assert.NotNull(word);
        Assert.Equal(WordDeck.DefaultDifficulty, word!.TierKey);
    }

    // ------------------------------------------------------------------ dial

    [Fact]
    public void Falling_out_of_a_tier_goes_nearest_first_and_easier_on_a_tie()
    {
        Assert.Equal(
            ["moderate", "easyModerate", "moderatelyDifficult", "easy", "difficult", "expert"],
            WordDeck.TiersOutwardFrom("moderate"));
    }

    [Fact]
    public void Falling_out_of_the_hardest_tier_only_goes_one_way()
    {
        Assert.Equal(
            ["expert", "difficult", "moderatelyDifficult", "moderate", "easyModerate", "easy"],
            WordDeck.TiersOutwardFrom("expert"));
    }

    [Fact]
    public void The_dial_moves_one_tier_at_a_time()
    {
        Assert.Equal("moderatelyDifficult", WordDeck.Shift("moderate", 1));
        Assert.Equal("easyModerate", WordDeck.Shift("moderate", -1));
        Assert.Equal("difficult", WordDeck.Shift("moderate", 2));
    }

    [Fact]
    public void The_dial_stops_at_both_ends()
    {
        Assert.Equal("easy", WordDeck.Shift("easy", -1));
        Assert.Equal("expert", WordDeck.Shift("expert", 1));
        Assert.Equal("easy", WordDeck.Shift("moderate", -50));
        Assert.Equal("expert", WordDeck.Shift("moderate", 50));
    }

    [Fact]
    public void A_dial_pointing_at_nothing_is_treated_as_the_default()
    {
        Assert.Equal(WordDeck.Shift(WordDeck.DefaultDifficulty, 1), WordDeck.Shift("nonsense", 1));
    }

    // ---------------------------------------------------------------- shuffle

    [Fact]
    public void Shuffling_keeps_everybody_and_leaves_the_source_alone()
    {
        var people = new[] { "ann", "ben", "cal", "dee", "eve" };

        var shuffled = WordDeck.Shuffle(people, Rng());

        Assert.Equal(people.Order(), shuffled.Order());
        Assert.Equal(["ann", "ben", "cal", "dee", "eve"], people);
    }
}
