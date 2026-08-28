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

    // ---- the prose is not written yet, and says so --------------------------------------------

    [Fact]
    public void The_shipped_story_is_structure_without_prose()
    {
        // Deliberate: the game runs from day one and the Content gaps panel is the writing to-do
        // list. If this ever fails it means somebody started writing, which is the point.
        Assert.All(Story.Guests, c => Assert.True(MysteryText.IsPlaceholder(c.Backstory)));
        Assert.All(Story.Guests, c => Assert.True(MysteryText.IsPlaceholder(c.DislikesBraun)));
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
