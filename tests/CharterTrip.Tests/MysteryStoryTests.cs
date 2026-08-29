using CharterTrip.Core.Models;
using CharterTrip.Core.Mystery;
using CharterTrip.Infrastructure.Mystery;

namespace CharterTrip.Tests;

/// <summary>
/// The starting story, and the two things about it that have to be true before anybody writes a
/// word of prose: it is structurally coherent, and the phase gates cannot drift.
/// </summary>
public class MysteryStoryTests
{
    private static readonly MysteryStory Story = StoryLoader.Load();

    // ---- the shape of the cast --------------------------------------------------------------

    [Fact]
    public void The_house_holds_twenty_one_guests_and_four_staff()
    {
        Assert.Equal(21, Story.Guests.Count());
        Assert.Equal(4, Story.StaffParts.Count());
        Assert.Single(Story.StaffParts, c => c.Staff == MysteryStaffRole.Host);
    }

    [Fact]
    public void Every_character_id_is_unique()
    {
        var ids = Story.Characters.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [Fact]
    public void Every_guest_stands_in_a_room_that_takes_players()
    {
        foreach (var guest in Story.Guests)
        {
            var zone = Story.Zone(guest.ZoneId);
            Assert.NotNull(zone);
            Assert.True(zone!.PlayersAllowed, $"{guest.Id} is standing in {zone.Id}, which takes nobody.");
        }
    }

    [Fact]
    public void Every_guest_is_in_a_faction_that_exists()
    {
        foreach (var guest in Story.Guests)
            Assert.NotNull(Story.Faction(guest.FactionId));
    }

    /// <summary>
    /// The staff are in the room but not in the game: no faction, no ballot, nothing to find.
    /// A facilitator who could be voted for would be a conviction slot wasted on somebody who
    /// cannot be guilty.
    /// </summary>
    [Fact]
    public void Staff_are_in_no_faction_and_are_never_guilty()
    {
        foreach (var staff in Story.StaffParts)
        {
            Assert.Equal("", staff.FactionId);
            Assert.Null(staff.GuiltSlot);
            Assert.False(staff.IsHerring);
        }
    }

    // ---- the murder --------------------------------------------------------------------------

    [Fact]
    public void Three_killers_hold_one_guilt_slot_each()
    {
        var killers = Story.Killers.ToList();
        Assert.Equal(3, killers.Count);
        Assert.Equal(
            new[] { "access", "means", "signature" },
            killers.Select(k => k.GuiltSlot!).OrderBy(s => s, StringComparer.Ordinal));
    }

    /// <summary>
    /// Two killers in one room is one conversation that clears both of them, and the whole evening
    /// turns on nobody being able to alibi a hand.
    /// </summary>
    [Fact]
    public void No_two_killers_share_a_room()
    {
        var rooms = Story.Killers.Select(k => k.ZoneId).ToList();
        Assert.Equal(rooms.Count, rooms.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_red_herring_is_never_actually_guilty()
    {
        Assert.All(Story.Characters.Where(c => c.IsHerring), c => Assert.Null(c.GuiltSlot));
        Assert.Contains(Story.Characters, c => c.IsHerring);
    }

    [Fact]
    public void The_factions_add_up_to_the_twenty_one_people_playing()
    {
        var counted = Story.Guests
            .GroupBy(c => c.FactionId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        Assert.Equal(3, counted["killer"]);
        Assert.Equal(2, counted["minion"]);
        Assert.Equal(3, counted["detective"]);
        Assert.Equal(2, counted["jester"]);
        Assert.Equal(2, counted["inheritance"]);
        Assert.Equal(9, counted["villager"]);
        Assert.Equal(21, counted.Values.Sum());
    }

    /// <summary>Stored both ways rather than derived, so a briefing and the reveal cannot disagree.</summary>
    [Fact]
    public void The_two_claimants_name_each_other()
    {
        var claimants = Story.Guests.Where(c => c.FactionId == "inheritance").ToList();
        Assert.Equal(2, claimants.Count);
        Assert.Equal(claimants[1].Id, claimants[0].RivalCharacterId);
        Assert.Equal(claimants[0].Id, claimants[1].RivalCharacterId);
    }

    // ---- the house ---------------------------------------------------------------------------

    [Fact]
    public void Every_room_holds_exactly_one_clue_card()
    {
        Assert.Equal(Story.Zones.Count, Story.Clues.Count);

        foreach (var zone in Story.Zones)
            Assert.Single(Story.Clues, c => c.ZoneId == zone.Id);
    }

    [Fact]
    public void Every_adjacency_names_a_real_room()
    {
        foreach (var zone in Story.Zones)
            foreach (var next in zone.Adjacent)
                Assert.NotNull(Story.Zone(next));
    }

    [Fact]
    public void Beefs_name_real_people_and_never_pair_somebody_with_themselves()
    {
        foreach (var beef in Story.Beefs)
        {
            Assert.NotNull(Story.Character(beef.ACharacterId));
            Assert.NotNull(Story.Character(beef.BCharacterId));
            Assert.NotEqual(beef.ACharacterId, beef.BCharacterId);
        }
    }

    /// <summary>
    /// Two apiece is the floor that makes the mingling round work: somebody with no history has
    /// nothing to open with but the weather.
    /// </summary>
    [Fact]
    public void Everybody_has_history_with_at_least_two_people()
    {
        foreach (var guest in Story.Guests)
            Assert.True(Story.Beefs.Count(b => b.Involves(guest.Id)) >= 2,
                $"{guest.Id} has nobody to be awkward with.");
    }

    [Fact]
    public void No_pair_carries_the_same_history_twice()
    {
        var pairs = Story.Beefs
            .Select(b => string.CompareOrdinal(b.ACharacterId, b.BCharacterId) < 0
                ? (b.ACharacterId, b.BCharacterId)
                : (b.BCharacterId, b.ACharacterId))
            .ToList();

        Assert.Equal(pairs.Count, pairs.Distinct().Count());
    }

    // ---- what the game can ask of people ------------------------------------------------------

    [Fact]
    public void Every_faction_objective_names_a_faction_that_exists()
    {
        foreach (var objective in Story.Objectives.Where(o => o.Audience == MysteryAudience.Faction))
        {
            Assert.NotNull(objective.FactionId);
            Assert.NotNull(Story.Faction(objective.FactionId!));
        }
    }

    /// <summary>
    /// An ability that unlocks in a phase the evening never reaches is a power nobody ever gets,
    /// and it fails silently — the player simply waits all night for a button that does not arrive.
    /// </summary>
    [Fact]
    public void Every_ability_unlocks_somewhere_the_evening_actually_goes()
    {
        var reachable = MysteryPhases.Order.ToHashSet();

        foreach (var faction in Story.Factions)
            foreach (var ability in faction.Abilities)
                Assert.Contains(ability.Unlock, reachable);
    }

    [Fact]
    public void Abilities_unlock_only_once_people_know_what_they_are()
    {
        foreach (var faction in Story.Factions)
            foreach (var ability in faction.Abilities)
                Assert.True(MysteryPhases.RolesRevealed(ability.Unlock),
                    $"{faction.Id}.{ability.Id} unlocks in {ability.Unlock}, before anybody has a role.");
    }

    // ---- the prose ----------------------------------------------------------------------------

    /// <summary>
    /// The story used to ship as structure without prose, and a test here asserted exactly that:
    /// every guest's backstory was a row of dots, and the comment said if it ever failed it meant
    /// somebody had started writing, which was the point. Somebody did. This is the same test with
    /// the sign flipped, widened to every field a player can actually read.
    ///
    /// It is worth keeping rather than deleting. <c>MysteryText.IsPlaceholder</c> is still how an
    /// unwritten field looks, a field added to the model later still arrives as dots, and a
    /// surface that omits a line rather than showing dots to the room will do it silently. This is
    /// what notices.
    /// </summary>
    [Fact]
    public void The_shipped_story_is_written()
    {
        foreach (var c in Story.Characters)
        {
            Written($"{c.Id}.voice", c.Voice);
            Written($"{c.Id}.backstory", c.Backstory);
            Written($"{c.Id}.whyInvited", c.WhyInvited);
            Written($"{c.Id}.dislikesBraun", c.DislikesBraun);

            // The staff are in the room but not in the game — nobody scans them, nobody plants on
            // them, so the four fields that exist to be found do not apply. MysteryStoryEditor.Gaps
            // scopes them the same way.
            if (c.Staff is null)
            {
                Written($"{c.Id}.observable", c.Observable);
                Written($"{c.Id}.seenAs", c.SeenAs);
                Written($"{c.Id}.signatureItem", c.SignatureItem);
                Written($"{c.Id}.tamperInsert", c.TamperInsert);
            }

            Assert.All(c.Dialogue.Life, l => Written($"{c.Id}.life", l));
            Assert.All(c.Dialogue.Weather, l => Written($"{c.Id}.weather", l));

            foreach (var topic in c.Dialogue.Topics)
            {
                Written($"{c.Id}.topic", topic.Prompt);
                Assert.NotEmpty(topic.Lines);
                Assert.All(topic.Lines, l => Written($"{c.Id}.{topic.Prompt}", l));
            }
        }

        foreach (var z in Story.Zones)
        {
            Written($"{z.Id}.notes", z.Notes);
            Written($"{z.Id}.clueSpot", z.ClueSpot);
        }

        foreach (var clue in Story.Clues)
        {
            Written($"{clue.Id}.name", clue.Name);
            Written($"{clue.Id}.text", clue.Text);
        }

        foreach (var f in Story.Factions)
        {
            Written($"{f.Id}.blurb", f.Blurb);
            Written($"{f.Id}.knowledge", f.Knowledge);
            Written($"{f.Id}.winCondition", f.WinCondition);

            foreach (var a in f.Abilities)
            {
                Written($"{f.Id}.{a.Id}.text", a.Text);
                Assert.All(a.Modes, m => Written($"{f.Id}.{a.Id}.{m.Id}", m.Text));
            }
        }

        foreach (var b in Story.Beefs)
        {
            Written($"{b.Id}.subject", b.Subject);
            Written($"{b.Id}.aSays", b.ASays);
            Written($"{b.Id}.bSays", b.BSays);
        }

        foreach (var slide in Story.Slides)
        {
            Written($"{slide.Id}.title", slide.Title);
            Written($"{slide.Id}.braunSays", slide.BraunSays);
            Assert.NotEmpty(slide.Bullets);
            Assert.All(slide.Bullets, b => Written($"{slide.Id}.bullet", b));
        }

        Assert.All(Story.Objectives, o => Written($"{o.Id}.text", o.Text));

        var beats = Story.Beats;
        Written("beats.premise", beats.Premise);
        Written("beats.invitationLetter", beats.InvitationLetter);
        Written("beats.murderAnnouncement", beats.MurderAnnouncement);
        Written("beats.studyScene", beats.StudyScene);
        Written("beats.houseRules", beats.HouseRules);
        Written("beats.townWin", beats.TownWin);
        Written("beats.killerWin", beats.KillerWin);
        Written("beats.tamperScrubbed", beats.TamperScrubbed);

        Assert.NotEmpty(beats.RevealParagraphs);
        Assert.All(beats.RevealParagraphs, p => Written("beats.reveal", p));
    }

    private static void Written(string what, string? value) =>
        Assert.False(MysteryText.IsPlaceholder(value), $"{what} is still unwritten.");

    /// <summary>
    /// The one field the writing changed structurally rather than textually. The editor will not
    /// accept an age outside this range, so the content should not carry one either.
    /// </summary>
    [Fact]
    public void Everybody_has_an_age_the_editor_would_accept() =>
        Assert.All(Story.Characters, c =>
            Assert.True(c.Age is > 0 and < 130, $"{c.Id} is {c.Age}."));

    /// <summary>
    /// The two frames a tampered clue is rewritten through, and the hole the guest's belongings go
    /// in. <c>ScanShareService.Compose</c> substitutes <c>{insert}</c> and, finding no brace,
    /// returns the card untouched — so a frame written without one makes the killers' Plant and the
    /// jester's self-framing do nothing at all, with no error anywhere to say so.
    /// </summary>
    [Fact]
    public void A_tamper_frame_has_somewhere_to_put_the_belongings()
    {
        Assert.Contains("{insert}", Story.Beats.TamperSubtle, StringComparison.Ordinal);
        Assert.Contains("{insert}", Story.Beats.TamperBlatant, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>ObjectiveBus</c> fills an objective by replacing <c>{slot}</c> for each name in
    /// <c>Slots</c>. A slot with no brace to fill is an instruction that silently loses its target;
    /// a brace with no slot behind it is a brace on somebody's phone.
    /// </summary>
    [Fact]
    public void Every_objective_slot_matches_a_hole_in_its_text()
    {
        foreach (var o in Story.Objectives)
        {
            foreach (var slot in o.Slots)
                Assert.Contains("{" + slot + "}", o.Text, StringComparison.Ordinal);

            var holes = System.Text.RegularExpressions.Regex.Matches(o.Text, @"\{(\w+)\}")
                .Select(m => m.Groups[1].Value);

            foreach (var hole in holes)
                Assert.Contains(hole, o.Slots);
        }
    }

    [Fact]
    public void Names_and_jobs_are_real_because_the_game_needs_them_to_be()
    {
        Assert.All(Story.Characters, c => Assert.False(MysteryText.IsPlaceholder(c.Name)));
        Assert.All(Story.Characters, c => Assert.False(MysteryText.IsPlaceholder(c.Job)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("....")]
    [InlineData("........")]
    [InlineData("  ..  ")]
    public void A_row_of_dots_is_how_an_unwritten_field_looks(string value) =>
        Assert.True(MysteryText.IsPlaceholder(value));

    [Theory]
    [InlineData("Wilhelm Shepard")]
    [InlineData("He was pacing. Again.")]
    [InlineData("...and then he left.")]
    public void Anything_somebody_actually_typed_is_not_a_placeholder(string value) =>
        Assert.False(MysteryText.IsPlaceholder(value));
}
