using CharterTrip.Core.Models;
using CharterTrip.Core.Mystery;
using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

/// <summary>
/// The one button on the site that cannot be undone by pressing it again.
///
/// Two things are being guarded. The first is that it clears everything — the failure it exists to
/// prevent is a host thinking the weekend is reset and discovering on Saturday night that one
/// game's points were still in the standings. The second is that it clears nothing else: the
/// Jeopardy board and the murder mystery's story are somebody's work, and a reset that took them
/// would be the worst button in the app.
/// </summary>
public class GameResetTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 28, 22, 0, 0, TimeSpan.Zero);

    /// <summary>A weekend halfway through: both games played, points on the board.</summary>
    private static TripData Played()
    {
        var trip = new TripData
        {
            Teams =
            [
                new Team { Id = "jou", Name = "Team Jou" },
                new Team { Id = "ali", Name = "Team Ali" }
            ]
        };

        trip.Jeopardy.Title = "Charter Trip Jeopardy";
        trip.Jeopardy.Categories.Add(new JeopardyCategory
        {
            Name = "Braun Manor",
            Clues =
            [
                new JeopardyClue { Id = "c1", Value = 5, Clue = "Who?", Response = "Braun" },
                new JeopardyClue { Id = "c2", Value = 10, Clue = "Where?", Response = "The study" }
            ]
        });

        JeopardyService.Reset(trip, new Random(1));
        trip.Jeopardy.Game.Phase = JeopardyPhase.Board;
        trip.Jeopardy.Game.UsedClueIds.Add("c1");
        trip.Jeopardy.Game.JoinedTeamIds.Add("jou");
        trip.Jeopardy.Game.HostJoined = true;

        trip.Mystery.Story.Seeded = true;
        trip.Mystery.Story.Characters.Add(new MysteryCharacter { Id = "braun", Name = "James Braun", Staff = MysteryStaffRole.Host });
        trip.Mystery.Story.Characters.Add(new MysteryCharacter { Id = "molly", Name = "Molly Henderson" });
        trip.Mystery.Story.Beats.Premise = "A man nobody likes throws a party.";

        CastingService.OpenDoors(trip, new Random(2));
        CastingService.ClaimCharacter(trip, "person-1", new Random(3));
        PhaseService.GoToPhase(trip, MysteryPhase.Trial1, T0);

        trip.Games.Add(new Game { Id = "spelling", Name = "Spelling Bee", Rules = ["Spell it.", "Sit down if you miss."] });
        SpellingBeeService.Reset(trip, new Random(4));
        SpellingBeeService.ShowRule(trip, 1);
        trip.SpellingBee.Game.Ready.Add("person-1");

        trip.Party.Sketch.Phase = PartyGamePhase.Playing;
        trip.Party.Sketch.Round = 3;
        trip.Party.Sketch.UsedCharacters.Add("braun");
        trip.Party.Sketch.CurrentCharacter = "molly";
        trip.Party.Relay.Phase = PartyGamePhase.Playing;

        trip.Scores.Add(new ScoreEntry { Id = "s1", TeamId = "jou", GameId = "jeopardy", Points = 15, At = T0 });
        trip.Scores.Add(new ScoreEntry { Id = "s2", TeamId = "ali", GameId = "relay", Points = 30, At = T0 });

        return trip;
    }

    [Fact]
    public void It_puts_jeopardy_back_to_the_title_card()
    {
        var trip = Played();
        var code = trip.Jeopardy.Game.PartyCode;

        GameReset.All(trip, new Random(9));

        Assert.Equal(JeopardyPhase.NotStarted, trip.Jeopardy.Game.Phase);
        Assert.Empty(trip.Jeopardy.Game.UsedClueIds);
        Assert.Empty(trip.Jeopardy.Game.JoinedTeamIds);
        Assert.False(trip.Jeopardy.Game.HostJoined);

        // New codes, or a phone left connected from the last game can buzz into the new one.
        Assert.NotEqual(code, trip.Jeopardy.Game.PartyCode);
    }

    [Fact]
    public void It_closes_the_mystery_and_empties_its_cast()
    {
        var trip = Played();

        GameReset.All(trip, new Random(9));

        Assert.Equal(MysteryPhase.Lobby, trip.Mystery.Phase);
        Assert.Empty(trip.Mystery.Play.Cast);
        Assert.Empty(trip.Mystery.Play.Trials);
        Assert.Equal("", trip.Mystery.Play.PartyCode);
    }

    /// <summary>
    /// The relay's points are the ones that used to survive. Jeopardy clears its own on reset and
    /// the mystery never scored any, so a reset that only called those two left every other game's
    /// points in the standings — which is the exact bug this button exists to not have.
    /// </summary>
    [Fact]
    public void It_empties_the_standings_for_every_game_not_just_jeopardy()
    {
        var trip = Played();

        GameReset.All(trip, new Random(9));

        Assert.Empty(trip.Scores);
    }

    [Fact]
    public void It_does_not_touch_anything_anybody_wrote()
    {
        var trip = Played();

        GameReset.All(trip, new Random(9));

        Assert.Equal("Charter Trip Jeopardy", trip.Jeopardy.Title);
        Assert.Equal(2, trip.Jeopardy.Categories[0].Clues.Count);
        Assert.Equal("Braun", trip.Jeopardy.Categories[0].Clues[0].Response);

        Assert.True(trip.Mystery.Story.Seeded);
        Assert.Equal(2, trip.Mystery.Story.Characters.Count);
        Assert.Equal("A man nobody likes throws a party.", trip.Mystery.Story.Beats.Premise);

        Assert.Equal(2, trip.Teams.Count);
    }

    [Fact]
    public void Pressing_it_twice_is_the_same_as_pressing_it_once()
    {
        var trip = Played();

        GameReset.All(trip, new Random(9));
        var code = trip.Jeopardy.Game.PartyCode;

        GameReset.All(trip, new Random(9));

        Assert.Equal(MysteryPhase.Lobby, trip.Mystery.Phase);
        Assert.Empty(trip.Scores);
        Assert.Equal(code, trip.Jeopardy.Game.PartyCode);
    }

    // ------------------------------------------------------------------------------------------
    //  What the screen says before it does it
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void A_weekend_nobody_has_played_has_nothing_to_clear()
    {
        var trip = new TripData();

        Assert.Empty(GameReset.WhatWouldGo(trip));
        Assert.False(GameReset.AnythingToClear(trip));
    }

    [Fact]
    public void It_names_every_game_it_would_take()
    {
        var lines = GameReset.WhatWouldGo(Played());

        Assert.Equal(5, lines.Count);
        Assert.Contains(lines, l => l.Contains("Jeopardy"));
        Assert.Contains(lines, l => l.Contains("spelling bee"));
        Assert.Contains(lines, l => l.Contains("Police Sketch") && l.Contains("the relay"));
        Assert.Contains(lines, l => l.Contains("murder mystery"));
        Assert.Contains(lines, l => l.Contains("45 points"));
    }

    // ------------------------------------------------------------------------------------------
    //  The games that were live but invisible to it
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The bee was not in the reset at all, and the way that showed up was the slideshow: the wall
    /// renders one rule at a time instead of the title card, driven from a phone, so a bee left on
    /// rule two by a host who has put their phone down looks like a bee that will not start — and
    /// the one button that says it resets every game left it exactly where it was.
    /// </summary>
    [Fact]
    public void It_takes_the_bees_rules_off_the_wall()
    {
        var trip = Played();

        GameReset.All(trip, new Random(9));

        Assert.Equal(-1, trip.SpellingBee.Game.RuleSlide);
        Assert.Equal(BeePhase.NotStarted, trip.SpellingBee.Game.Phase);
        Assert.Empty(trip.SpellingBee.Game.Ready);
    }

    /// <summary>
    /// A bee stranded on a rule with nothing else played is the whole of what there is to clear.
    /// If the summary cannot see it the button is disabled, and the state that most needs the
    /// button is the one state the button refuses to act on.
    /// </summary>
    [Fact]
    public void A_bee_stuck_on_a_rule_is_something_to_clear()
    {
        var trip = new TripData();
        trip.Games.Add(new Game { Id = "spelling", Name = "Spelling Bee", Rules = ["Spell it."] });
        SpellingBeeService.ShowRule(trip, 0);

        Assert.True(GameReset.AnythingToClear(trip));

        GameReset.All(trip, new Random(9));

        Assert.Equal(-1, trip.SpellingBee.Game.RuleSlide);
    }

    /// <summary>
    /// Points are not the whole of a round game's state. Clearing the standings left Police Sketch
    /// on round three with a face already spent, so the next round opened halfway through the cast.
    /// </summary>
    [Fact]
    public void It_puts_the_standing_games_back_to_their_rules_card()
    {
        var trip = Played();

        GameReset.All(trip, new Random(9));

        Assert.Equal(PartyGamePhase.NotStarted, trip.Party.Sketch.Phase);
        Assert.Equal(1, trip.Party.Sketch.Round);
        Assert.Empty(trip.Party.Sketch.UsedCharacters);
        Assert.Null(trip.Party.Sketch.CurrentCharacter);
        Assert.Equal(PartyGamePhase.NotStarted, trip.Party.Relay.Phase);
    }

    /// <summary>Settings are how a game is set up, not how it went. They survive.</summary>
    [Fact]
    public void It_keeps_what_the_committee_set_up()
    {
        var trip = Played();
        trip.Party.Sketch.PointValue = 20;
        trip.Party.Sketch.Characters.Add(new SketchCharacter { Name = "James Braun" });

        GameReset.All(trip, new Random(9));

        Assert.Equal(20, trip.Party.Sketch.PointValue);
        Assert.Single(trip.Party.Sketch.Characters);
    }

    /// <summary>
    /// Straight after a reset the button has nothing left to do, which is what greys it out. A
    /// summary that still listed things would make the button look broken.
    /// </summary>
    [Fact]
    public void There_is_nothing_left_to_clear_once_it_has_run()
    {
        var trip = Played();

        GameReset.All(trip, new Random(9));

        Assert.False(GameReset.AnythingToClear(trip));
    }
}
