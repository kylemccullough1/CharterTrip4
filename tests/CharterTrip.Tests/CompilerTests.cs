using CharterTrip.Core.Models;
using CharterTrip.Core.Mystery.Deal;
using CharterTrip.Core.Mystery.Script;
using CharterTrip.Core.Mystery.Text;
using CharterTrip.Infrastructure.Mystery;

namespace CharterTrip.Tests;

/// <summary>
/// The compiler, checked for the two things that matter: every sentence a player reads is composed
/// rather than invented, and no placeholder ever reaches a screen unfilled.
///
/// A leftover <c>{name}</c> in front of twenty-one people is the visible version of this going
/// wrong, so it is asserted across many seeds rather than one.
/// </summary>
public class CompilerTests
{
    private static readonly MysteryScript Script = ScriptLoader.Load();

    private static MysteryDeal Deal(int seed = 1234)
    {
        var result = Dealer.Deal(Script, [.. Enumerable.Range(1, 21).Select(i => $"p-{i}")], seed);
        Assert.True(result.Ok, result.Failure?.Reason);
        return result.Deal!;
    }

    private static MysteryState StateWith(MysteryDeal deal, params string[] convicted)
    {
        var state = new MysteryState { Active = true, Deal = deal };
        state.Clues.AddRange(Dealer.LayOutClues(Script, deal));

        // Two convictions per trial, the way rounds.json runs it.
        foreach (var chunk in convicted.Chunk(2))
            state.Trials.Add(new MysteryTrial { RoundId = "trial", ConvictedCharacterIds = [.. chunk] });

        return state;
    }

    /// <summary>Anything that still looks like a template hole.</summary>
    private static void AssertNoPlaceholders(string text, string where)
    {
        Assert.DoesNotContain("{", text);
        Assert.DoesNotContain("}", text);
        Assert.False(string.IsNullOrWhiteSpace(text), $"{where} composed to nothing.");
    }

    [Fact]
    public void The_study_scene_carries_the_method_that_was_actually_used()
    {
        for (var seed = 1; seed <= 50; seed++)
        {
            var deal = Deal(seed);
            var scene = Compiler.StudyScene(Script, deal);

            AssertNoPlaceholders(scene, "study scene");

            // The flavour belongs to the means killer, and to nobody else. Getting this wrong
            // would put a poisoned nightcap in a game where the bottle did it.
            var means = deal.KillerFor("means")!;
            Assert.Contains(Script.StoryBeats.MethodBeats[means].SceneFlavor, scene);

            foreach (var (id, beat) in Script.StoryBeats.MethodBeats.Where(b => b.Key != means))
                Assert.DoesNotContain(beat.SceneFlavor, scene);
        }
    }

    [Fact]
    public void Every_killer_gets_a_briefing_for_the_slot_they_filled()
    {
        for (var seed = 1; seed <= 100; seed++)
        {
            var deal = Deal(seed);

            foreach (var killer in deal.Killers)
            {
                var briefing = Compiler.KillerBriefing(Script, deal, killer);

                Assert.NotNull(briefing);
                AssertNoPlaceholders(briefing!, $"{killer.CharacterId} briefing");
            }
        }
    }

    [Fact]
    public void The_access_killers_briefing_matches_the_route_they_came_by()
    {
        for (var seed = 1; seed <= 100; seed++)
        {
            var deal = Deal(seed);
            var access = deal.Cast.Single(c => c.GuiltSlot == "access");

            Assert.Equal(
                Script.StoryBeats.AccessBeats[deal.AccessRoute].Briefing,
                Compiler.KillerBriefing(Script, deal, access));
        }
    }

    [Fact]
    public void Nobody_but_a_killer_gets_a_killer_briefing()
    {
        var deal = Deal();

        foreach (var member in deal.Cast.Where(c => !c.IsKiller))
        {
            Assert.Null(Compiler.KillerBriefing(Script, deal, member));

            // Red herrings especially. They genuinely do not know why the room is looking at them,
            // and that is the whole reason burning one is a real risk.
            Assert.Null(Compiler.CoverStory(Script, deal, member));
        }
    }

    [Fact]
    public void Each_killer_gets_one_distinct_red_herring_to_point_at()
    {
        for (var seed = 1; seed <= 100; seed++)
        {
            var deal = Deal(seed);

            var pointed = deal.Killers
                .Select(k => Compiler.HerringFor(deal, k.GuiltSlot!))
                .ToList();

            Assert.All(pointed, h => Assert.NotNull(h));
            Assert.Equal(3, pointed.Select(h => h!.CharacterId).Distinct().Count());

            // And a killer never points at another killer — that is what herrings are for.
            Assert.All(pointed, h => Assert.False(h!.IsKiller));

            foreach (var killer in deal.Killers)
            {
                var cover = Compiler.CoverStory(Script, deal, killer);
                Assert.NotNull(cover);
                AssertNoPlaceholders(cover!, $"{killer.CharacterId} cover story");
            }
        }
    }

    [Fact]
    public void A_cover_story_quotes_the_herrings_guilty_reading_verbatim()
    {
        var deal = Deal();
        var killer = deal.Killers.First();
        var herring = Compiler.HerringFor(deal, killer.GuiltSlot!)!;
        var character = Script.CharacterById(herring.CharacterId)!;

        var cover = Compiler.CoverStory(Script, deal, killer)!;

        // The killer points at something the room will independently hear about from a witness.
        // Same authored sentence, which is why the frame holds up.
        Assert.Contains(character.Name, cover);
        Assert.Contains(character.Seen.Guilty, cover);
    }

    [Fact]
    public void Everybody_witnesses_between_one_and_four_people()
    {
        for (var seed = 1; seed <= 100; seed++)
        {
            var deal = Deal(seed);

            foreach (var member in deal.Cast)
            {
                var seen = Compiler.WitnessStatementsFor(Script, deal, member.CharacterId);

                // Rooms hold two to five, so everybody sees at least one other person. Three
                // co-located plus at most one sighting from next door is the cap.
                Assert.InRange(seen.Count, 1, 4);
                Assert.All(seen, s => AssertNoPlaceholders(s.Text, $"{member.CharacterId} on {s.AboutCharacterId}"));

                // Never yourself.
                Assert.DoesNotContain(member.CharacterId, seen.Select(s => s.AboutCharacterId));
                Assert.Equal(seen.Count, seen.Select(s => s.AboutCharacterId).Distinct().Count());
            }
        }
    }

    [Fact]
    public void A_witness_reports_the_reading_the_subject_was_actually_dealt()
    {
        for (var seed = 1; seed <= 50; seed++)
        {
            var deal = Deal(seed);

            foreach (var member in deal.Cast)
            {
                foreach (var statement in Compiler.WitnessStatementsFor(Script, deal, member.CharacterId))
                {
                    var subject = deal.Cast.Single(c => c.CharacterId == statement.AboutCharacterId);
                    var character = Script.CharacterById(statement.AboutCharacterId)!;

                    // This is the mechanism of the whole game: a killer and a red herring produce
                    // the same sentence, so the room cannot tell them apart from testimony alone.
                    Assert.Equal(character.Seen.For(subject.ShowsGuilty), statement.Text);
                }
            }
        }
    }

    [Fact]
    public void Six_people_get_reported_guilty_and_they_are_the_killers_and_the_herrings()
    {
        var deal = Deal();

        var reportedGuilty = deal.Cast
            .SelectMany(m => Compiler.WitnessStatementsFor(Script, deal, m.CharacterId))
            .Where(s => Script.CharacterById(s.AboutCharacterId)!.Seen.Guilty == s.Text)
            .Select(s => s.AboutCharacterId)
            .Distinct()
            .ToList();

        Assert.All(reportedGuilty, id => Assert.True(deal.Cast.Single(c => c.CharacterId == id).ShowsGuilty));
    }

    [Fact]
    public void A_cross_zone_sighting_is_flagged_as_seen_from_the_next_room()
    {
        var found = 0;

        for (var seed = 1; seed <= 100; seed++)
        {
            var deal = Deal(seed);

            foreach (var sighting in deal.CrossZoneSightings)
            {
                var seen = Compiler.WitnessStatementsFor(Script, deal, sighting.ObserverCharacterId);
                var statement = seen.SingleOrDefault(s =>
                    s.AboutCharacterId == sighting.SubjectCharacterId && s.FromNextRoom);

                Assert.NotNull(statement);
                found++;
            }
        }

        Assert.True(found > 0, "No seed produced a cross-zone sighting.");
    }

    [Fact]
    public void A_full_briefing_composes_for_every_character_on_every_seed()
    {
        for (var seed = 1; seed <= 100; seed++)
        {
            var deal = Deal(seed);

            foreach (var member in deal.Cast)
            {
                var briefing = Compiler.BriefingFor(Script, deal, member.CharacterId);

                Assert.NotNull(briefing);
                AssertNoPlaceholders(briefing!.Name, "name");
                AssertNoPlaceholders(briefing.Motive, "motive");
                AssertNoPlaceholders(briefing.Fear, "fear");
                AssertNoPlaceholders(briefing.ZoneName, "zone name");
                AssertNoPlaceholders(briefing.FactionName, "faction name");
                Assert.NotEmpty(briefing.Witnessed);
            }
        }
    }

    [Fact]
    public void Only_the_two_claimants_are_told_they_have_a_rival()
    {
        var deal = Deal();

        foreach (var member in deal.Cast)
        {
            var briefing = Compiler.BriefingFor(Script, deal, member.CharacterId)!;

            if (member.FactionId == "inheritance")
                Assert.NotNull(briefing.RivalName);
            else
                Assert.Null(briefing.RivalName);
        }
    }

    // ---- clue text -------------------------------------------------------------------------

    [Fact]
    public void Every_clue_reads_as_something_on_every_seed()
    {
        for (var seed = 1; seed <= 100; seed++)
        {
            var deal = Deal(seed);

            foreach (var clue in Dealer.LayOutClues(Script, deal))
            {
                AssertNoPlaceholders(Compiler.ClueText(Script, clue), $"clue in {clue.ZoneId}");
                AssertNoPlaceholders(Compiler.ClueName(Script, clue), $"clue name in {clue.ZoneId}");
            }
        }
    }

    [Fact]
    public void A_trace_clue_reads_the_characters_authored_trace()
    {
        var deal = Deal();
        var clue = Dealer.LayOutClues(Script, deal).First(c => c.TraceCharacterId is not null);

        Assert.Equal(
            Script.CharacterById(clue.TraceCharacterId!)!.Trace.Text,
            Compiler.ClueText(Script, clue));
    }

    [Fact]
    public void A_room_with_no_trace_shows_its_own_clue_spot_rather_than_invented_prose()
    {
        var deal = Deal();
        var clue = Dealer.LayOutClues(Script, deal).First(c => c.TraceCharacterId is null);

        // The content has no authored neutral-clue text. Describing where the card sits is the
        // honest fallback; putting words in the author's mouth is not.
        Assert.Equal(Script.Zones.ById(clue.ZoneId)!.ClueSpot, Compiler.ClueText(Script, clue));
    }

    [Theory]
    [InlineData("subtle")]
    [InlineData("blatant")]
    [InlineData("plant")]
    public void A_tampered_clue_gains_the_targets_belongings(string mode)
    {
        var deal = Deal();
        var clue = Dealer.LayOutClues(Script, deal).First(c => c.TraceCharacterId is not null);
        var framed = deal.Cast.First(c => c.CharacterId != clue.TraceCharacterId);
        var insert = Script.CharacterById(framed.CharacterId)!.TamperInsert;

        var original = Compiler.ClueText(Script, clue);
        clue.Tamper = new MysteryTamper { Mode = mode, ByCharacterId = "x", TargetCharacterId = framed.CharacterId };
        var tampered = Compiler.ClueText(Script, clue);

        Assert.StartsWith(original, tampered);
        Assert.Contains(insert, tampered);
        AssertNoPlaceholders(tampered, "tampered clue");
    }

    [Fact]
    public void A_scrubbed_clue_loses_its_reading_entirely()
    {
        var deal = Deal();
        var clue = Dealer.LayOutClues(Script, deal).First(c => c.TraceCharacterId is not null);
        var original = Compiler.ClueText(Script, clue);

        clue.Tamper = new MysteryTamper { Mode = "scrub", ByCharacterId = clue.TraceCharacterId! };

        // Erasing yourself is often stronger than framing somebody else, and that only works if
        // the original text is genuinely gone rather than appended to.
        var scrubbed = Compiler.ClueText(Script, clue);
        Assert.Equal(Script.StoryBeats.TamperSystem.ScrubRender, scrubbed);
        Assert.DoesNotContain(original, scrubbed);
    }

    [Fact]
    public void Forensics_reports_untouched_or_the_original_reading()
    {
        var deal = Deal();
        var clue = Dealer.LayOutClues(Script, deal).First(c => c.TraceCharacterId is not null);
        var original = Compiler.ClueText(Script, clue);

        AssertNoPlaceholders(Compiler.Forensics(Script, clue), "clean forensics");

        clue.Tamper = new MysteryTamper { Mode = "scrub", ByCharacterId = "x" };
        var report = Compiler.Forensics(Script, clue);

        // The detective's only ground truth. It has to survive a scrub, which is exactly the case
        // where the card itself no longer says anything.
        AssertNoPlaceholders(report, "tampered forensics");
        Assert.Contains(original, report);
    }

    // ---- conviction and endgame ------------------------------------------------------------

    [Fact]
    public void A_conviction_card_names_a_killer_and_calls_everybody_else_a_guest()
    {
        var deal = Deal();

        var killer = deal.Killers.First();
        var card = Compiler.ConvictionCard(Script, deal, killer.CharacterId, blameTaken: false);
        AssertNoPlaceholders(card, "killer card");

        // The authored card reads "THE SYNDICATE'S HAND", not the word KILLER — rounds.json's
        // phase_6 summary says KILLER, but conviction_reveals is the text that actually renders.
        Assert.Contains("THE SYNDICATE'S HAND", card);

        // Blame-take changes what the room is told, never what is true.
        var blamed = Compiler.ConvictionCard(Script, deal, killer.CharacterId, blameTaken: true);
        Assert.DoesNotContain("THE SYNDICATE'S HAND", blamed);
        Assert.Contains("SYNDICATE ASSOCIATE", blamed);

        // A convicted detective must not read as a detective — that hands the killers a kill list.
        var detective = deal.InFaction("detective").First();
        var guest = Compiler.ConvictionCard(Script, deal, detective.CharacterId, blameTaken: false);
        Assert.Contains("GUEST", guest, StringComparison.OrdinalIgnoreCase);

        var jester = deal.InFaction("jester").First();
        Assert.Contains("GUEST", Compiler.ConvictionCard(Script, deal, jester.CharacterId, false),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_endgame_composes_for_every_outcome_without_a_hole_in_it()
    {
        for (var seed = 1; seed <= 40; seed++)
        {
            var deal = Deal(seed);
            var killers = deal.Killers.Select(k => k.CharacterId).ToList();
            var others = deal.Cast.Where(c => !c.IsKiller).Select(c => c.CharacterId).Take(6).ToList();

            // Every number of killers caught, from a clean sweep to none at all.
            foreach (var convicted in new[]
                     {
                         killers.ToArray(),
                         killers.Take(2).Concat(others.Take(1)).ToArray(),
                         killers.Take(1).Concat(others.Take(3)).ToArray(),
                         others.Take(4).ToArray()
                     })
            {
                var paragraphs = Compiler.Endgame(Script, StateWith(deal, convicted));

                Assert.NotEmpty(paragraphs);
                foreach (var paragraph in paragraphs)
                    AssertNoPlaceholders(paragraph, $"seed {seed} endgame");
            }
        }
    }

    [Fact]
    public void The_one_missing_content_fragment_is_reported_rather_than_guessed_at()
    {
        var deal = Deal();
        var gaps = Compiler.MissingFragments(Script, StateWith(deal));

        // story_beats.json's detective_reveal wants a {result_line} that nothing provides. The
        // compiler will not invent it, Endgame omits that paragraph, and it shows up here so it
        // can be written. If this list grows, a new template arrived with a hole in it.
        Assert.Equal(["detective_reveal → {result_line}"], gaps);
    }

    [Fact]
    public void A_paragraph_with_an_unfilled_placeholder_is_omitted_rather_than_shown()
    {
        var deal = Deal();
        var paragraphs = Compiler.Endgame(Script, StateWith(deal, deal.Killers.First().CharacterId));

        // The detectives are named in the reveal only once {result_line} exists. Until then the
        // paragraph is absent, which is better than a visible template hole on the wall.
        Assert.DoesNotContain(paragraphs, p => p.Contains("The investigators were"));
        Assert.All(paragraphs, p => Assert.DoesNotContain("{", p));
    }

    [Fact]
    public void Town_wins_at_two_killers_convicted_and_the_killers_at_two_surviving()
    {
        var deal = Deal();
        var killers = deal.Killers.Select(k => k.CharacterId).ToList();
        var innocent = deal.Cast.Where(c => !c.IsKiller).Select(c => c.CharacterId).Take(4).ToList();

        // Ruleset B, and the two halves are exhaustive: 0 or 1 convicted is a killer win, 2 or 3
        // is a town win, and there is no outcome where nobody wins.
        Assert.False(Compiler.Outcome(StateWith(deal, [.. innocent]), default).TownWon);
        Assert.False(Compiler.Outcome(StateWith(deal, killers[0], innocent[0], innocent[1]), default).TownWon);
        Assert.True(Compiler.Outcome(StateWith(deal, killers[0], killers[1], innocent[0]), default).TownWon);
        Assert.True(Compiler.Outcome(StateWith(deal, [.. killers]), default).TownWon);
    }

    [Fact]
    public void A_town_win_still_reads_as_all_three_caught()
    {
        var deal = Deal();
        var killers = deal.Killers.Select(k => k.CharacterId).ToList();

        // Two in custody means the third is rolled up off the back of them, so there is one town
        // ending rather than a partial one.
        var twoOfThree = Compiler.Endgame(Script, StateWith(deal, killers[0], killers[1]))[0];
        var allThree = Compiler.Endgame(Script, StateWith(deal, [.. killers]))[0];

        Assert.Equal(allThree, twoOfThree);
        Assert.Contains("All three hands", twoOfThree);
    }

    [Fact]
    public void A_convicted_jester_wins_and_an_unconvicted_one_does_not()
    {
        var deal = Deal();
        var jester = deal.InFaction("jester").First();

        var won = Compiler.Outcome(StateWith(deal, jester.CharacterId), default);
        Assert.Contains(jester.CharacterId, won.PersonalWinnerCharacterIds);

        var lost = Compiler.Outcome(StateWith(deal), default);
        Assert.DoesNotContain(jester.CharacterId, lost.PersonalWinnerCharacterIds);
    }

    [Fact]
    public void A_claimant_needs_their_rival_convicted_and_needs_to_survive()
    {
        var deal = Deal();
        var claimants = deal.InFaction("inheritance").ToList();
        var (a, b) = (claimants[0], claimants[1]);

        // Rival convicted, and survived: a win.
        var won = Compiler.Outcome(StateWith(deal, b.CharacterId), default);
        Assert.Contains(a.CharacterId, won.PersonalWinnerCharacterIds);
        Assert.DoesNotContain(b.CharacterId, won.PersonalWinnerCharacterIds);

        // Both convicted: no fallback, so neither wins.
        var both = Compiler.Outcome(StateWith(deal, a.CharacterId, b.CharacterId), default);
        Assert.DoesNotContain(a.CharacterId, both.PersonalWinnerCharacterIds);
        Assert.DoesNotContain(b.CharacterId, both.PersonalWinnerCharacterIds);

        // Neither convicted: also nothing.
        var neither = Compiler.Outcome(StateWith(deal), default);
        Assert.DoesNotContain(a.CharacterId, neither.PersonalWinnerCharacterIds);
    }

    [Fact]
    public void Blame_take_cannot_change_who_actually_won()
    {
        var deal = Deal();
        var killers = deal.Killers.Select(k => k.CharacterId).ToList();
        var state = StateWith(deal, killers[0], killers[1]);

        var outcome = Compiler.Outcome(state, default);

        // The reveal card can lie. The tally cannot — this is the detectives' only ground truth.
        Assert.Equal(2, outcome.KillersConvicted);
        Assert.True(outcome.TownWon);
    }

    [Fact]
    public void Every_red_herring_gets_their_name_cleared_at_the_end()
    {
        var deal = Deal();
        var paragraphs = Compiler.Endgame(Script, StateWith(deal, deal.Herrings.First().CharacterId));

        foreach (var herring in deal.Herrings)
        {
            var character = Script.CharacterById(herring.CharacterId)!;

            // What makes burning an innocent feel fair rather than arbitrary.
            Assert.Contains(paragraphs, p => p.Contains(character.HerringTruth));
        }
    }

    [Fact]
    public void The_same_deal_always_composes_the_same_words()
    {
        var deal = Deal(99);

        Assert.Equal(Compiler.StudyScene(Script, deal), Compiler.StudyScene(Script, deal));

        foreach (var member in deal.Cast)
        {
            var first = Compiler.BriefingFor(Script, deal, member.CharacterId)!;
            var second = Compiler.BriefingFor(Script, deal, member.CharacterId)!;

            Assert.Equal(first.KillerBriefing, second.KillerBriefing);
            Assert.Equal(first.CoverStory, second.CoverStory);
            Assert.Equal(
                first.Witnessed.Select(w => (w.AboutCharacterId, w.Text)),
                second.Witnessed.Select(w => (w.AboutCharacterId, w.Text)));
        }
    }
}
