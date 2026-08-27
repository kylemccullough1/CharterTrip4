using CharterTrip.Core.Mystery.Script;
using CharterTrip.Infrastructure.Mystery;

namespace CharterTrip.Tests;

/// <summary>
/// The Braun Manor content is hand-maintained JSON and the script records are C#. These tests are
/// what stop the two drifting apart — a renamed key shows up here rather than as an empty character
/// card on somebody's phone during the party.
///
/// <see cref="ScriptLoader.Load"/> validates as it loads and throws on anything incoherent, so the
/// first test doing nothing but calling it is load-bearing: it is the whole structural check.
/// </summary>
public class MysteryScriptTests
{
    private static readonly MysteryScript Script = ScriptLoader.Load();

    [Fact]
    public void Content_loads_and_is_coherent()
    {
        Assert.NotNull(Script);
        Assert.Empty(Script.Validate());
    }

    [Fact]
    public void Twenty_one_characters_with_the_fields_that_matter()
    {
        Assert.Equal(21, Script.Characters.Count);

        Assert.All(Script.Characters, c =>
        {
            Assert.NotEmpty(c.Id);
            Assert.NotEmpty(c.Name);
            Assert.NotEmpty(c.Motive);
            Assert.NotEmpty(c.SignatureItem);
            Assert.NotEmpty(c.TamperInsert);

            // The guilty/innocent pair is the mechanism of the whole game. A character missing
            // either reading cannot be dealt as a killer or cleared as a herring.
            Assert.NotEmpty(c.Acts.Guilty);
            Assert.NotEmpty(c.Acts.Innocent);
            Assert.NotEmpty(c.Seen.Guilty);
            Assert.NotEmpty(c.Seen.Innocent);

            // The herring exoneration is what makes burning an innocent feel fair at the reveal.
            Assert.NotEmpty(c.HerringTruth);

            Assert.NotEmpty(c.Trace.Name);
            Assert.NotEmpty(c.Trace.Text);
            Assert.NotEmpty(c.Zones);
        });
    }

    [Fact]
    public void Nine_zones_of_which_eight_take_players()
    {
        Assert.Equal(9, Script.Zones.Zones.Count);
        Assert.Equal(8, Script.Zones.Playable.Count());

        // The study is the murder scene. It holds a clue and takes nobody.
        var study = Script.Zones.ById("study");
        Assert.NotNull(study);
        Assert.False(study.PlayersAllowed);
        Assert.NotEmpty(study.ClueSpot);

        // Every zone has exactly one clue spot, which is what makes "nine clues, nine rooms" work.
        Assert.All(Script.Zones.Zones, z => Assert.NotEmpty(z.ClueSpot));
    }

    [Fact]
    public void Twenty_one_players_fit_the_playable_rooms()
    {
        var playable = Script.Zones.Playable.ToList();
        var min = playable.Sum(z => z.Capacity.Min);
        var max = playable.Sum(z => z.Capacity.Max);

        Assert.InRange(Script.Characters.Count, min, max);

        // The floor is the number the Dealer needs to refuse rather than reshuffle forever, so it
        // is worth pinning: if it moves, phase 5's fail-fast threshold moves with it.
        Assert.Equal(17, min);
        Assert.Equal(29, max);
    }

    [Fact]
    public void Six_factions_seating_everybody_exactly_once()
    {
        Assert.Equal(6, Script.Factions.Factions.Count);
        Assert.Equal(21, Script.Factions.TotalSeats);

        Assert.Equal(3, Script.Factions.ById("killer")?.Count);
        Assert.Equal(2, Script.Factions.ById("minion")?.Count);
        Assert.Equal(3, Script.Factions.ById("detective")?.Count);
        Assert.Equal(2, Script.Factions.ById("jester")?.Count);
        Assert.Equal(2, Script.Factions.ById("inheritance")?.Count);
        Assert.Equal(9, Script.Factions.ById("villager")?.Count);
    }

    [Fact]
    public void Every_slots_entry_names_a_real_guilt_slot()
    {
        var slots = Script.Characters.SelectMany(c => c.Slots).ToHashSet();
        Assert.Equal(["access", "means", "signature"], slots.OrderBy(s => s));

        // Slot supply, which is what the killer draw has to work with. Nobody is faction-pinned
        // any more, so the tag count and the eligible count agree — but they are still separate
        // questions, and ForSlot answers the one the Dealer needs.
        Assert.Equal(8, Script.Characters.Count(c => c.Slots.Contains("access")));
        Assert.Equal(8, Script.ForSlot("access").Count());
        Assert.Contains("access", Script.CharacterById("harry")!.Slots);

        Assert.Equal(5, Script.ForSlot("means").Count());
        Assert.Equal(6, Script.ForSlot("signature").Count());
    }

    [Fact]
    public void Sixteen_characters_are_killer_eligible_and_five_never_are()
    {
        Assert.Equal(16, Script.KillerEligible.Count());

        var ineligible = Script.Characters.Where(c => c.IneligibleAsKiller).Select(c => c.Id).ToList();

        // Carrying no guilt slots is now the only thing that keeps a character out of the draw —
        // a killer picked with no slot would have no beat to compose a briefing from.
        Assert.Equal(5, ineligible.Count);
        Assert.All(ineligible, id => Assert.Empty(Script.CharacterById(id)!.Slots));

        // Isla is one of the five. The data set's README listed her under "fixed inheritance",
        // which made it look like unpinning her would make her a killer candidate; she carries no
        // slots, so it did not.
        Assert.Contains("isla", ineligible);
    }

    [Fact]
    public void Nobody_is_pinned_to_a_faction()
    {
        // The inheritance claim used to be Harry and Isla in every game. It is drawn from the
        // non-killer pool now, like every other faction, so both are ordinary candidates for
        // anything — including being killers.
        Assert.All(Script.Characters, c => Assert.Null(c.FixedFaction));

        Assert.Contains(Script.KillerEligible, c => c.Id == "harry");
        Assert.DoesNotContain("isla", Script.KillerEligible.Select(c => c.Id));

        // Isla is out because she carries no slots, not because of who she is — the distinction
        // matters if slots are ever added to her.
        Assert.Empty(Script.CharacterById("isla")!.Slots);
    }

    [Fact]
    public void The_faction_draw_has_exactly_enough_people_left_after_the_killers()
    {
        // 21 characters, 3 drawn as killers, and the remaining five factions have to account for
        // the other 18 exactly. With inheritance now in this pool rather than pre-assigned, an
        // off-by-one here would leave somebody with no role on the night.
        var killers = Script.Factions.ById("killer")!.Count;
        var drawnAfterKillers = Script.Factions.Factions
            .Where(f => f.Id != "killer")
            .Sum(f => f.Count);

        Assert.Equal(Script.Characters.Count - killers, drawnAfterKillers);
        Assert.Equal(18, drawnAfterKillers);
    }

    [Fact]
    public void An_access_killer_can_always_be_placed_somewhere_that_reaches_the_study()
    {
        var accessZones = Script.Zones.AccessGranting.Select(z => z.Id).ToHashSet();
        Assert.Equal(4, accessZones.Count);

        Assert.Contains(Script.ForSlot("access"), c => c.Zones.Any(accessZones.Contains));
    }

    [Fact]
    public void Nine_rounds_and_three_trials_that_add_up()
    {
        Assert.Equal(9, Script.Rounds.Rounds.Count);
        Assert.Equal(3, Script.Rounds.Trials.Count());

        // The summary field lied about this once already.
        Assert.Equal(Script.Rounds.ScheduledMinutes, Script.Rounds.TotalRuntimeMinutes);

        // Two convictions per trial is six slots, which is what the balance rests on.
        Assert.All(Script.Rounds.Trials, t => Assert.Equal(2, t.Convictions));
    }

    [Fact]
    public void Both_trial_cuts_have_a_tie_rule_written_down()
    {
        var procedure = Script.Rounds.TrialProcedure;

        Assert.All(procedure.Phases, Assert.NotEmpty);
        Assert.NotEmpty(procedure.EarlyEnd);

        // These are the two places a trial can wedge with the room standing still, so the rule has
        // to actually be present to implement in phase 9.
        Assert.Contains("tie", procedure.Phase2, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tie", procedure.Phase5, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Story_beats_cover_every_way_the_murder_can_have_happened()
    {
        var beats = Script.StoryBeats;

        Assert.Equal(5, beats.MethodBeats.Count);
        Assert.Equal(3, beats.AccessBeats.Count);
        Assert.NotEmpty(beats.SignatureBeats.Briefing);
        Assert.NotEmpty(beats.SignatureBeats.Reveal);

        // Every means-tagged character needs a method beat, or the study scene cannot be composed
        // for the game where they were drawn.
        foreach (var character in Script.ForSlot("means"))
            Assert.True(beats.MethodBeats.ContainsKey(character.Id),
                $"{character.Id} can be the means killer but has no method beat.");

        // Every access route needs a beat, for the same reason.
        foreach (var route in Script.Zones.AccessRoutes.Keys)
            Assert.True(beats.AccessBeats.ContainsKey(route),
                $"Route '{route}' has no access beat.");

        Assert.All(beats.MethodBeats.Values, b =>
        {
            Assert.NotEmpty(b.SceneFlavor);
            Assert.NotEmpty(b.Briefing);
            Assert.NotEmpty(b.Reveal);
        });
    }

    [Fact]
    public void Both_endgames_and_every_conviction_card_have_text()
    {
        var beats = Script.StoryBeats;

        Assert.Contains("town_win", beats.EndgameReveals.Keys);
        Assert.Contains("killer_win", beats.EndgameReveals.Keys);
        Assert.All(beats.EndgameReveals.Values, Assert.NotEmpty);

        // Non-killer convictions all read as GUEST during play; the endgame is where truth lands.
        Assert.Contains("killer", beats.ConvictionReveals.Keys);
        Assert.Contains("guest", beats.ConvictionReveals.Keys);
        Assert.All(beats.ConvictionReveals.Values, Assert.NotEmpty);

        Assert.NotEmpty(beats.AssemblyRules.WitnessStatements);
        Assert.NotEmpty(beats.AssemblyRules.CoverStoryTemplate);
    }

    [Fact]
    public void Tamper_renders_exist_for_every_mode_a_player_can_choose()
    {
        var tamper = Script.StoryBeats.TamperSystem;

        // The jester picks subtle or blatant; the killers pick plant or scrub. Each needs a render
        // or the clue text cannot be rewritten when the ability fires.
        foreach (var mode in new[] { "subtle", "blatant", "plant" })
            Assert.False(string.IsNullOrEmpty(tamper.RenderFor(mode)), $"No render for '{mode}'.");

        Assert.NotEmpty(tamper.ScrubRender);
        Assert.NotEmpty(tamper.ForensicsResult);
        Assert.NotEmpty(tamper.Rules);
    }

    [Fact]
    public void Shared_charge_abilities_are_the_ones_that_can_race()
    {
        var shared = Script.Factions.Factions
            .SelectMany(f => f.Abilities.Select(a => (Faction: f.Id, Ability: a)))
            .Where(x => x.Ability.Shared)
            .ToList();

        // Exactly two: the killers' collective charge and the minions' collective charge. These
        // are the "two people press the button at the same moment" case that phase 4's
        // MutateAsync<T> overload exists for.
        Assert.Equal(2, shared.Count);
        Assert.Contains(shared, x => x.Faction == "killer");
        Assert.Contains(shared, x => x.Faction == "minion");
        Assert.All(shared, x => Assert.Equal(1, x.Ability.Charges));
    }

    [Fact]
    public void Every_ability_is_either_one_effect_or_a_choice_of_modes()
    {
        var abilities = Script.Factions.Factions.SelectMany(f => f.Abilities).ToList();
        Assert.NotEmpty(abilities);

        Assert.All(abilities, a =>
        {
            Assert.NotEmpty(a.Id);
            Assert.NotEmpty(a.Name);
            Assert.NotEmpty(a.Unlock);
            Assert.True(a.Charges > 0, $"{a.Id} has no charges.");

            // At least one, never neither: an ability with no text and no modes does nothing.
            // Both is legitimate — the jester's self_frame carries a summary line and then the
            // two variants it can be spent as.
            var hasText = !string.IsNullOrWhiteSpace(a.Text);
            Assert.True(hasText || a.HasModes, $"{a.Id} has neither text nor modes, so it does nothing.");

            if (a.HasModes) Assert.All(a.Modes!.Values, Assert.NotEmpty);
        });
    }

    [Fact]
    public void Ghosts_can_only_say_canned_things()
    {
        var ghosts = Script.Ghosts.Ghosts;

        Assert.Equal(12, ghosts.CannedMessages.Count);
        Assert.All(ghosts.CannedMessages, Assert.NotEmpty);
        Assert.NotEmpty(ghosts.TrialReactions);
        Assert.Equal(1, ghosts.Haunt.ChargesPerGhost);

        // The one hard rule in the design. If this text stops saying so, someone has loosened it.
        Assert.Contains("no free text", ghosts.Rules, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Four_organizer_seats_the_host_and_three_facilitators()
    {
        var npcs = Script.Ghosts.Npcs;

        Assert.Equal("James Braun", npcs.Braun.Name);
        Assert.NotEmpty(npcs.Braun.HardRule);

        Assert.Equal(3, npcs.Facilitators.Count);
        Assert.All(npcs.Facilitators, f =>
        {
            Assert.NotEmpty(f.Name);
            Assert.NotEmpty(f.Zone);
            Assert.NotEmpty(f.Orders);
        });
    }

    [Fact]
    public void The_prompt_engine_knows_its_cadence_and_its_priorities()
    {
        var engine = Script.Prompts.Engine;

        Assert.Equal(5, engine.CadenceMinutes);
        Assert.Equal(
            ["underserved", "contradiction", "proximity", "clue_linked", "faction"],
            engine.PriorityOrder);

        // Every priority category needs a pool to draw from, and every faction needs its own.
        foreach (var category in engine.PriorityOrder.Where(c => c != "faction"))
            Assert.NotEmpty(Script.Prompts.Templates.For(category));

        foreach (var faction in Script.Factions.Factions)
            Assert.NotEmpty(Script.Prompts.Templates.For(faction.Id));
    }

    [Fact]
    public void Anchored_traces_stay_put_and_the_rest_can_spill()
    {
        var anchored = Script.Characters.Where(c => !c.Trace.IsPortable).ToList();

        // Four traces are pinned to a zone: Carla's to the driveway, and three to the lawn. The
        // spillover rule in phase 5 has to leave exactly these alone.
        Assert.Equal(4, anchored.Count);
        Assert.Equal("driveway", Script.CharacterById("carla")?.Trace.AnchorZone);

        foreach (var character in anchored)
            Assert.NotNull(Script.Zones.ById(character.Trace.AnchorZone!));
    }

    // A validator nobody has watched fail is not known to work. These break the real script in the
    // ways a hand-edit on the day would break it, and check each one is actually caught — otherwise
    // Content_loads_and_is_coherent passes for the wrong reason forever.

    [Fact]
    public void A_character_placed_in_a_zone_that_does_not_exist_is_caught()
    {
        var broken = Script with
        {
            Characters = [.. Script.Characters.Select((c, i) =>
                i == 0 ? c with { Zones = ["the_orangery"] } : c)]
        };

        Assert.Contains(broken.Validate(), p => p.Contains("the_orangery"));
    }

    [Fact]
    public void A_faction_table_that_does_not_seat_everybody_is_caught()
    {
        var broken = Script with
        {
            Factions = Script.Factions with
            {
                Factions = [.. Script.Factions.Factions.Select((f, i) =>
                    i == 0 ? f with { Count = f.Count + 1 } : f)]
            }
        };

        Assert.Contains(broken.Validate(), p => p.Contains("no role"));
    }

    [Fact]
    public void A_roster_too_small_to_place_is_caught()
    {
        // The floor is 17. Below it the rooms cannot meet their minimums and the Dealer would
        // reshuffle forever instead of failing, which is the wedge this exists to prevent.
        var broken = Script with { Characters = [.. Script.Characters.Take(16)] };

        Assert.Contains(broken.Validate(), p => p.Contains("unsatisfiable"));
    }

    [Fact]
    public void A_stale_runtime_summary_is_caught()
    {
        var broken = Script with { Rounds = Script.Rounds with { TotalRuntimeMinutes = 110 } };

        Assert.Contains(broken.Validate(), p => p.Contains("110") && p.Contains("120"));
    }

    [Fact]
    public void A_guilt_slot_with_nobody_eligible_to_fill_it_is_caught()
    {
        // Strip every means tag: the draw would dead-end with no means killer to pick.
        var broken = Script with
        {
            Characters = [.. Script.Characters.Select(c =>
                c with { Slots = [.. c.Slots.Where(s => s != "means")] })]
        };

        Assert.Contains(broken.Validate(), p => p.Contains("'means'"));
    }

    [Fact]
    public void Loading_twice_gives_the_same_game()
    {
        // Phase 5's determinism rests on the script being identical every load — nothing in the
        // path stateful, nothing order-dependent. Compared field by field rather than with record
        // equality, because a record holding IReadOnlyList compares those lists by reference and
        // would pass this test for the wrong reason.
        var a = ScriptLoader.Load();
        var b = ScriptLoader.Load();

        Assert.Equal(a.Characters.Select(c => c.Id), b.Characters.Select(c => c.Id));
        Assert.Equal(a.Characters.Select(c => c.Trace.Text), b.Characters.Select(c => c.Trace.Text));
        Assert.Equal(a.Zones.Zones.Select(z => z.Id), b.Zones.Zones.Select(z => z.Id));
        Assert.Equal(a.Factions.Factions.Select(f => f.Id), b.Factions.Factions.Select(f => f.Id));
        Assert.Equal(a.Rounds.Rounds.Select(r => r.Id), b.Rounds.Rounds.Select(r => r.Id));
        Assert.Equal(a.StoryBeats.MethodBeats.Keys.Order(), b.StoryBeats.MethodBeats.Keys.Order());
        Assert.Equal(a.Ghosts.Ghosts.CannedMessages, b.Ghosts.Ghosts.CannedMessages);
        Assert.Equal(a.Rounds.TrialProcedure, b.Rounds.TrialProcedure);
    }
}
