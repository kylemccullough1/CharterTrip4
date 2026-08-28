using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

public class JeopardyServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 28, 22, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(int ms) => T0.AddMilliseconds(ms);

    private static TripData Trip(int categories = 2, int perCategory = 2)
    {
        var trip = new TripData
        {
            Teams =
            [
                new Team { Id = "jou", Name = "Team Jou" },
                new Team { Id = "ali", Name = "Team Ali" },
                new Team { Id = "kyle", Name = "Team Kyle" }
            ]
        };

        for (var c = 0; c < categories; c++)
        {
            var cat = new JeopardyCategory { Name = $"Cat{c}" };
            for (var i = 1; i <= perCategory; i++)
                cat.Clues.Add(new JeopardyClue
                {
                    Id = $"c{c}-{i * 5}", Value = i * 5,
                    Clue = $"Clue {c}-{i}", Response = $"Answer {c}-{i}"
                });
            trip.Jeopardy.Categories.Add(cat);
        }

        trip.Jeopardy.Final = new JeopardyFinal { Value = 30, Clue = "Name every charter member, in order." };
        return trip;
    }

    /// <summary>
    /// Put a clue up and, since most tests care about buzzing and judging rather than the reveal
    /// pause itself, open its buzzers straight away too. A pick is only legal from the board now
    /// that phones can make one, so the game has to actually be sitting there first.
    /// </summary>
    private static void OpenClue(TripData trip, string clueId)
    {
        trip.Jeopardy.Game.Phase = JeopardyPhase.Board;
        trip.Jeopardy.Game.BuzzersOpen = false;
        JeopardyService.PickClue(trip, clueId);
        JeopardyService.OpenBuzzers(trip, clueId, T0);
    }

    /// <summary>Start the final and, as with <see cref="OpenClue"/>, skip straight past its reveal pause.</summary>
    private static void OpenFinal(TripData trip)
    {
        JeopardyService.StartFinal(trip);
        JeopardyService.OpenBuzzers(trip, JeopardyService.FinalClueId, T0);
    }

    /// <summary>Play a clue right through: pick it, buzz, judge, and move past the answer.</summary>
    private static void PlayClue(TripData trip, string clueId, string teamId, bool correct = true)
    {
        OpenClue(trip, clueId);
        JeopardyService.Buzz(trip, teamId, At(100));
        if (correct) JeopardyService.JudgeCorrect(trip, T0); else JeopardyService.JudgeWrong(trip, T0);
        JeopardyService.Continue(trip);
    }

    // ------------------------------------------------------------------ setup

    [Fact]
    public void Reset_gives_the_room_its_one_door()
    {
        var trip = Trip();
        JeopardyService.Reset(trip, new Random(1));

        var game = trip.Jeopardy.Game;
        Assert.Equal(4, game.PartyCode.Length);
        Assert.Equal(JeopardyPhase.NotStarted, game.Phase);
    }

    [Fact]
    public void Reset_wipes_the_scores_out_of_the_trip_tally()
    {
        var trip = Trip();
        trip.Scores.Add(new ScoreEntry { Id = "a", TeamId = "jou", GameId = "jeopardy", Points = 20 });
        trip.Scores.Add(new ScoreEntry { Id = "b", TeamId = "jou", GameId = "spelling", Points = 10 });

        JeopardyService.Reset(trip, new Random(1));

        Assert.Equal(0, JeopardyService.ScoreFor(trip, "jou"));
        Assert.Single(trip.Scores);                       // the spelling bee is untouched
        Assert.Equal("spelling", trip.Scores[0].GameId);
    }

    [Fact]
    public void Reset_issues_new_codes_so_a_stale_phone_cannot_buzz_into_the_next_game()
    {
        var trip = Trip();
        JeopardyService.Reset(trip, new Random(1));
        var before = trip.Jeopardy.Game.PartyCode;

        JeopardyService.Reset(trip, new Random(9));

        Assert.NotEqual(before, trip.Jeopardy.Game.PartyCode);
        Assert.False(JeopardyService.IsPartyCode(trip, before));
    }

    /// <summary>
    /// The race settles itself on the first buzz. It used to collect everyone and wait for the
    /// host to confirm a result the whole room had just watched happen.
    /// </summary>
    [Fact]
    public void The_buzz_off_hands_the_first_pick_to_whoever_rings_in_first()
    {
        var trip = Trip();
        JeopardyService.Reset(trip, new Random(1));
        JeopardyService.StartBuzzOff(trip, T0);

        JeopardyService.Buzz(trip, "kyle", At(420));

        Assert.Equal("kyle", trip.Jeopardy.Game.PickingTeamId);
        Assert.False(trip.Jeopardy.Game.BuzzersOpen);

        // Still on the board — the race does not put a clue up, it decides who chooses one.
        Assert.Equal(JeopardyPhase.Board, trip.Jeopardy.Game.Phase);
    }

    /// <summary>The winning buzz survives the settle: the card on screen reads its time off it.</summary>
    [Fact]
    public void The_winning_buzz_is_kept_so_the_screen_can_show_the_time()
    {
        var trip = Trip();
        JeopardyService.StartBuzzOff(trip, T0);

        JeopardyService.Buzz(trip, "kyle", At(420));

        var won = Assert.Single(trip.Jeopardy.Game.Buzzes);
        Assert.Equal("kyle", won.TeamId);
        Assert.Equal(420, won.Milliseconds);
    }

    [Fact]
    public void Once_the_race_is_won_nobody_else_can_ring_in()
    {
        var trip = Trip();
        JeopardyService.StartBuzzOff(trip, T0);
        JeopardyService.Buzz(trip, "kyle", At(420));

        Assert.False(JeopardyService.Buzz(trip, "jou", At(430)));
        Assert.Equal("kyle", trip.Jeopardy.Game.PickingTeamId);
    }

    // ---------------------------------------------------------------- buzzing

    [Fact]
    public void Buzzes_are_ordered_by_arrival_and_carry_a_reaction_time()
    {
        var trip = Trip();
        OpenClue(trip, "c0-5");

        Assert.True(JeopardyService.Buzz(trip, "ali", At(650)));

        var buzz = Assert.Single(trip.Jeopardy.Game.Buzzes);
        Assert.Equal("ali", buzz.TeamId);
        Assert.Equal(650, buzz.Milliseconds);
    }

    [Fact]
    public void The_first_buzz_closes_the_buzzers_and_hands_over_to_the_host()
    {
        var trip = Trip();
        OpenClue(trip, "c0-5");

        JeopardyService.Buzz(trip, "ali", At(300));

        Assert.False(trip.Jeopardy.Game.BuzzersOpen);
        Assert.Equal(JeopardyPhase.Judging, trip.Jeopardy.Game.Phase);
        Assert.False(JeopardyService.Buzz(trip, "jou", At(310)));   // too late
    }

    [Fact]
    public void A_team_cannot_buzz_twice_on_the_same_clue()
    {
        var trip = Trip();
        OpenClue(trip, "c0-5");

        Assert.True(JeopardyService.Buzz(trip, "ali", At(100)));
        trip.Jeopardy.Game.BuzzersOpen = true;                       // pretend it reopened
        Assert.False(JeopardyService.Buzz(trip, "ali", At(200)));
    }

    [Fact]
    public void Buzzing_does_nothing_when_the_buzzers_are_shut()
    {
        var trip = Trip();
        Assert.False(JeopardyService.Buzz(trip, "ali", At(10)));
        Assert.Empty(trip.Jeopardy.Game.Buzzes);
    }

    [Fact]
    public void An_unknown_team_cannot_buzz()
    {
        var trip = Trip();
        OpenClue(trip, "c0-5");

        Assert.False(JeopardyService.Buzz(trip, "gatecrasher", At(10)));
    }

    // --------------------------------------------------------------- judging

    [Fact]
    public void A_right_answer_scores_the_value_and_wins_the_next_pick()
    {
        var trip = Trip();
        OpenClue(trip, "c0-10");
        JeopardyService.Buzz(trip, "ali", At(200));

        JeopardyService.JudgeCorrect(trip, T0);

        Assert.Equal(10, JeopardyService.ScoreFor(trip, "ali"));
        Assert.Equal("ali", trip.Jeopardy.Game.PickingTeamId);

        // The answer goes up before the board comes back, so the clue is still the one in play.
        Assert.Equal(JeopardyPhase.Revealed, trip.Jeopardy.Game.Phase);
        Assert.Equal("ali", trip.Jeopardy.Game.RevealedWinnerTeamId);
        Assert.Equal("c0-10", trip.Jeopardy.Game.CurrentClueId);
        Assert.DoesNotContain("c0-10", trip.Jeopardy.Game.UsedClueIds);

        JeopardyService.Continue(trip);

        Assert.Equal(JeopardyPhase.Board, trip.Jeopardy.Game.Phase);
        Assert.Contains("c0-10", trip.Jeopardy.Game.UsedClueIds);
        Assert.Null(trip.Jeopardy.Game.CurrentClueId);
    }

    /// <summary>
    /// The answer is the part of a quiz the room actually wants, and snapping straight back to
    /// the board skips it. Every route off a clue has to land here first.
    /// </summary>
    [Theory]
    [InlineData("correct")]
    [InlineData("everyone wrong")]
    [InlineData("nobody buzzed")]
    public void Every_way_a_clue_ends_shows_the_answer_first(string ending)
    {
        var trip = Trip();
        OpenClue(trip, "c0-10");

        switch (ending)
        {
            case "correct":
                JeopardyService.Buzz(trip, "ali", At(100));
                JeopardyService.JudgeCorrect(trip, T0);
                break;
            case "everyone wrong":
                foreach (var team in new[] { "ali", "jou", "kyle" })
                {
                    JeopardyService.Buzz(trip, team, At(100));
                    JeopardyService.JudgeWrong(trip, T0);
                }
                break;
            default:
                JeopardyService.NobodyGotIt(trip);
                break;
        }

        Assert.Equal(JeopardyPhase.Revealed, trip.Jeopardy.Game.Phase);
        Assert.Equal("Answer 0-2", JeopardyService.InPlay(trip)!.Value.Response);
        Assert.False(trip.Jeopardy.Game.BuzzersOpen);
    }

    /// <summary>Nobody scoring is a different screen from somebody scoring, so it is recorded.</summary>
    [Fact]
    public void An_unclaimed_clue_reveals_with_no_winner()
    {
        var trip = Trip();
        OpenClue(trip, "c0-10");

        JeopardyService.NobodyGotIt(trip);

        Assert.Null(trip.Jeopardy.Game.RevealedWinnerTeamId);
    }

    [Fact]
    public void A_wrong_answer_deducts_locks_that_team_out_and_reopens_for_everyone_else()
    {
        var trip = Trip();
        OpenClue(trip, "c0-10");
        JeopardyService.Buzz(trip, "ali", At(200));

        JeopardyService.JudgeWrong(trip, T0);

        Assert.Equal(-10, JeopardyService.ScoreFor(trip, "ali"));
        Assert.Contains("ali", trip.Jeopardy.Game.LockedOutTeamIds);
        Assert.True(trip.Jeopardy.Game.BuzzersOpen);
        Assert.Equal(JeopardyPhase.Clue, trip.Jeopardy.Game.Phase);
        Assert.False(JeopardyService.Buzz(trip, "ali", At(400)));      // locked out
        Assert.True(JeopardyService.Buzz(trip, "jou", At(450)));       // still in
    }

    [Fact]
    public void A_wrong_answer_does_not_move_the_pick()
    {
        var trip = Trip();
        trip.Jeopardy.Game.PickingTeamId = "jou";
        OpenClue(trip, "c0-10");
        JeopardyService.Buzz(trip, "ali", At(200));

        JeopardyService.JudgeWrong(trip, T0);

        Assert.Equal("jou", trip.Jeopardy.Game.PickingTeamId);
    }

    [Fact]
    public void When_every_team_has_been_wrong_the_clue_closes_itself()
    {
        var trip = Trip();
        OpenClue(trip, "c0-10");

        foreach (var team in new[] { "ali", "jou", "kyle" })
        {
            JeopardyService.Buzz(trip, team, At(100));
            JeopardyService.JudgeWrong(trip, T0);
        }

        JeopardyService.Continue(trip);

        Assert.Contains("c0-10", trip.Jeopardy.Game.UsedClueIds);
        Assert.False(trip.Jeopardy.Game.BuzzersOpen);
        Assert.Equal(JeopardyPhase.Board, trip.Jeopardy.Game.Phase);

        // Each of the three paid the ten points for guessing wrong.
        Assert.All(new[] { "ali", "jou", "kyle" }, t => Assert.Equal(-10, JeopardyService.ScoreFor(trip, t)));
    }

    [Fact]
    public void Nobody_getting_it_costs_nothing_and_leaves_the_pick_where_it_was()
    {
        var trip = Trip();
        trip.Jeopardy.Game.PickingTeamId = "jou";
        OpenClue(trip, "c0-10");

        JeopardyService.NobodyGotIt(trip);
        JeopardyService.Continue(trip);

        Assert.Empty(trip.Scores);
        Assert.Equal("jou", trip.Jeopardy.Game.PickingTeamId);
        Assert.Contains("c0-10", trip.Jeopardy.Game.UsedClueIds);
    }

    [Fact]
    public void A_used_clue_cannot_be_picked_again()
    {
        var trip = Trip();
        PlayClue(trip, "c0-5", "ali");

        OpenClue(trip, "c0-5");

        Assert.Null(trip.Jeopardy.Game.CurrentClueId);
    }

    /// <summary>
    /// Picking is done from phones now, so two people can tap at the same moment. The second tap
    /// must not replace the clue that is already on the wall.
    /// </summary>
    [Fact]
    public void A_second_pick_cannot_replace_the_clue_already_up()
    {
        var trip = Trip();
        OpenClue(trip, "c0-5");

        JeopardyService.PickClue(trip, "c0-10");

        Assert.Equal("c0-5", trip.Jeopardy.Game.CurrentClueId);
    }

    [Fact]
    public void Nothing_can_be_picked_during_the_opening_buzz_off()
    {
        var trip = Trip();
        JeopardyService.StartBuzzOff(trip, T0);

        JeopardyService.PickClue(trip, "c0-5");

        Assert.Null(trip.Jeopardy.Game.CurrentClueId);
    }

    // ------------------------------------------------------------ reveal pause

    [Fact]
    public void Picking_a_clue_puts_it_up_without_opening_the_buzzers()
    {
        var trip = Trip();
        trip.Jeopardy.Game.Phase = JeopardyPhase.Board;

        JeopardyService.PickClue(trip, "c0-5");

        Assert.Equal("c0-5", trip.Jeopardy.Game.CurrentClueId);
        Assert.Equal(JeopardyPhase.Clue, trip.Jeopardy.Game.Phase);
        Assert.False(trip.Jeopardy.Game.BuzzersOpen);
        Assert.False(JeopardyService.Buzz(trip, "ali", At(10)));
    }

    [Fact]
    public void Opening_the_buzzers_lets_the_room_ring_in_on_the_clue_that_was_picked()
    {
        var trip = Trip();
        trip.Jeopardy.Game.Phase = JeopardyPhase.Board;
        JeopardyService.PickClue(trip, "c0-5");

        JeopardyService.OpenBuzzers(trip, "c0-5", T0);

        Assert.True(trip.Jeopardy.Game.BuzzersOpen);
        Assert.Equal(T0, trip.Jeopardy.Game.BuzzOpenedAt);
        Assert.True(JeopardyService.Buzz(trip, "ali", At(10)));
    }

    /// <summary>
    /// The open is scheduled off the clue id at pick time. If the game has moved on by the time
    /// the pause elapses — a reset, a judged answer, anything — that stale open must not land.
    /// </summary>
    [Fact]
    public void A_stale_buzzer_open_does_nothing_once_the_clue_has_moved_on()
    {
        var trip = Trip();
        OpenClue(trip, "c0-5");
        JeopardyService.NobodyGotIt(trip);   // clue is Revealed now, not Clue

        JeopardyService.OpenBuzzers(trip, "c0-5", At(5000));

        Assert.False(trip.Jeopardy.Game.BuzzersOpen);
        Assert.Equal(JeopardyPhase.Revealed, trip.Jeopardy.Game.Phase);
    }

    [Fact]
    public void A_stale_buzzer_open_does_nothing_after_a_reset()
    {
        var trip = Trip();
        trip.Jeopardy.Game.Phase = JeopardyPhase.Board;
        JeopardyService.PickClue(trip, "c0-5");

        JeopardyService.Reset(trip, new Random(1));
        JeopardyService.OpenBuzzers(trip, "c0-5", At(5000));

        Assert.False(trip.Jeopardy.Game.BuzzersOpen);
        Assert.Equal(JeopardyPhase.NotStarted, trip.Jeopardy.Game.Phase);
    }

    [Fact]
    public void Starting_the_final_puts_it_up_without_opening_the_buzzers()
    {
        var trip = Trip();

        JeopardyService.StartFinal(trip);

        Assert.Equal(JeopardyPhase.Final, trip.Jeopardy.Game.Phase);
        Assert.Equal(JeopardyService.FinalClueId, trip.Jeopardy.Game.CurrentClueId);
        Assert.False(trip.Jeopardy.Game.BuzzersOpen);
        Assert.False(JeopardyService.Buzz(trip, "ali", At(10)));
    }

    [Fact]
    public void The_board_moves_to_the_final_titles_once_the_last_clue_is_played()
    {
        var trip = Trip(categories: 1, perCategory: 2);

        foreach (var id in new[] { "c0-5", "c0-10" })
            PlayClue(trip, id, "ali");

        Assert.True(JeopardyService.AllCluesUsed(trip));
        Assert.Equal(JeopardyPhase.FinalIntro, trip.Jeopardy.Game.Phase);
    }

    /// <summary>Testing shortcut: skips the whole board without touching what's already been earned.</summary>
    [Fact]
    public void Skipping_to_final_marks_every_real_clue_used_but_leaves_scores_alone()
    {
        var trip = Trip();
        JeopardyService.Reset(trip, new Random(1));
        trip.Scores.Add(new ScoreEntry { Id = "a", TeamId = "ali", GameId = "jeopardy", Points = 20 });

        JeopardyService.SkipToFinal(trip);

        Assert.Equal(JeopardyPhase.FinalIntro, trip.Jeopardy.Game.Phase);
        Assert.True(JeopardyService.AllCluesUsed(trip));
        Assert.Null(trip.Jeopardy.Game.CurrentClueId);
        Assert.False(trip.Jeopardy.Game.BuzzersOpen);
        Assert.Equal(20, JeopardyService.ScoreFor(trip, "ali"));
    }

    [Fact]
    public void Skipping_to_final_is_fully_undone_by_a_reset()
    {
        var trip = Trip();
        JeopardyService.SkipToFinal(trip);

        JeopardyService.Reset(trip, new Random(1));

        Assert.Empty(trip.Jeopardy.Game.UsedClueIds);
        Assert.Equal(JeopardyPhase.NotStarted, trip.Jeopardy.Game.Phase);
    }

    // ------------------------------------------------------------------ final

    /// <summary>
    /// The final is a clue like any other now — which is the whole design, so it is worth
    /// asserting that it really does go through the same buzz-judge-reveal machinery rather
    /// than a parallel copy of it that can drift.
    /// </summary>
    [Fact]
    public void The_final_is_played_exactly_like_a_clue()
    {
        var trip = Trip();
        OpenFinal(trip);

        Assert.Equal(JeopardyPhase.Final, trip.Jeopardy.Game.Phase);
        Assert.True(trip.Jeopardy.Game.BuzzersOpen);

        var inPlay = JeopardyService.InPlay(trip);
        Assert.NotNull(inPlay);
        Assert.True(inPlay!.Value.IsFinal);
        Assert.Equal(30, inPlay.Value.Value);

        // Buzzing settles it the same way, straight into the host's call.
        Assert.True(JeopardyService.Buzz(trip, "ali", At(300)));
        Assert.Equal(JeopardyPhase.Judging, trip.Jeopardy.Game.Phase);
    }

    /// <summary>
    /// Nobody rang in before time ran out: buzzers shut and the room gets a beat, but — unlike
    /// every other way a clue ends — the answer does NOT go up. The clue stays in play so the
    /// host can restart it.
    /// </summary>
    [Fact]
    public void The_final_timer_running_out_closes_buzzers_without_revealing()
    {
        var trip = Trip();
        OpenFinal(trip);

        JeopardyService.ExpireFinalTimer(trip, At(5000));

        Assert.False(trip.Jeopardy.Game.BuzzersOpen);
        Assert.True(trip.Jeopardy.Game.FinalTimerExpired);
        Assert.Equal(JeopardyPhase.Final, trip.Jeopardy.Game.Phase);
        Assert.Equal(JeopardyService.FinalClueId, trip.Jeopardy.Game.CurrentClueId);
    }

    [Fact]
    public void A_final_timer_that_fires_after_someone_already_buzzed_does_nothing()
    {
        var trip = Trip();
        OpenFinal(trip);
        JeopardyService.Buzz(trip, "ali", At(300));   // buzzers close, phase -> Judging

        JeopardyService.ExpireFinalTimer(trip, At(5000));

        Assert.False(trip.Jeopardy.Game.FinalTimerExpired);
        Assert.Equal(JeopardyPhase.Judging, trip.Jeopardy.Game.Phase);
    }

    [Fact]
    public void Restarting_the_final_timer_reopens_buzzers_for_another_run()
    {
        var trip = Trip();
        OpenFinal(trip);
        JeopardyService.ExpireFinalTimer(trip, At(5000));

        JeopardyService.RestartFinalTimer(trip, At(6000));

        Assert.False(trip.Jeopardy.Game.FinalTimerExpired);
        Assert.True(trip.Jeopardy.Game.BuzzersOpen);
        Assert.Equal(At(6000), trip.Jeopardy.Game.BuzzOpenedAt);
        Assert.True(JeopardyService.Buzz(trip, "ali", At(6300)));
    }

    /// <summary>Restarting only makes sense from the expired state — it's not a generic re-open.</summary>
    [Fact]
    public void Restarting_the_final_timer_does_nothing_unless_it_actually_expired()
    {
        var trip = Trip();
        OpenFinal(trip);

        JeopardyService.RestartFinalTimer(trip, At(6000));

        Assert.Equal(T0, trip.Jeopardy.Game.BuzzOpenedAt);   // OpenFinal's original open, untouched
    }

    [Fact]
    public void Winning_the_final_pays_its_value_and_ends_the_game()
    {
        var trip = Trip();
        OpenFinal(trip);
        JeopardyService.Buzz(trip, "ali", At(300));

        JeopardyService.JudgeCorrect(trip, T0);

        Assert.Equal(30, JeopardyService.ScoreFor(trip, "ali"));
        Assert.Equal(JeopardyPhase.Revealed, trip.Jeopardy.Game.Phase);

        JeopardyService.Continue(trip);

        Assert.Equal(JeopardyPhase.Finished, trip.Jeopardy.Game.Phase);
    }

    /// <summary>Winning the final wins the game, so there is no next pick to hand anyone.</summary>
    [Fact]
    public void Winning_the_final_does_not_hand_out_a_pick()
    {
        var trip = Trip();
        trip.Jeopardy.Game.PickingTeamId = "jou";
        OpenFinal(trip);
        JeopardyService.Buzz(trip, "ali", At(300));

        JeopardyService.JudgeCorrect(trip, T0);

        Assert.Equal("jou", trip.Jeopardy.Game.PickingTeamId);
    }

    [Fact]
    public void A_wrong_final_costs_the_value_and_lets_the_others_in()
    {
        var trip = Trip();
        OpenFinal(trip);
        JeopardyService.Buzz(trip, "ali", At(300));

        JeopardyService.JudgeWrong(trip, T0);

        Assert.Equal(-30, JeopardyService.ScoreFor(trip, "ali"));
        Assert.Equal(JeopardyPhase.Final, trip.Jeopardy.Game.Phase);
        Assert.True(trip.Jeopardy.Game.BuzzersOpen);
        Assert.False(JeopardyService.Buzz(trip, "ali", At(400)));      // locked out
        Assert.True(JeopardyService.Buzz(trip, "jou", At(450)));       // still in
    }

    [Fact]
    public void A_final_nobody_gets_still_ends_the_game()
    {
        var trip = Trip();
        OpenFinal(trip);

        foreach (var team in new[] { "ali", "jou", "kyle" })
        {
            JeopardyService.Buzz(trip, team, At(100));
            JeopardyService.JudgeWrong(trip, T0);
        }

        Assert.Equal(JeopardyPhase.Revealed, trip.Jeopardy.Game.Phase);
        Assert.Null(trip.Jeopardy.Game.RevealedWinnerTeamId);

        JeopardyService.Continue(trip);

        Assert.Equal(JeopardyPhase.Finished, trip.Jeopardy.Game.Phase);
    }

    /// <summary>The final is not in a category, so it must never end up in the used pile.</summary>
    [Fact]
    public void The_final_is_never_recorded_as_a_used_clue()
    {
        var trip = Trip();
        OpenFinal(trip);
        JeopardyService.Buzz(trip, "ali", At(300));
        JeopardyService.JudgeCorrect(trip, T0);
        JeopardyService.Continue(trip);

        Assert.DoesNotContain(JeopardyService.FinalClueId, trip.Jeopardy.Game.UsedClueIds);
    }

    [Fact]
    public void Continue_does_nothing_unless_an_answer_is_up()
    {
        var trip = Trip();
        OpenClue(trip, "c0-5");

        JeopardyService.Continue(trip);

        Assert.Equal(JeopardyPhase.Clue, trip.Jeopardy.Game.Phase);
        Assert.Equal("c0-5", trip.Jeopardy.Game.CurrentClueId);
    }

    // ------------------------------------------------------------------ codes

    [Fact]
    public void The_door_code_is_recognised_regardless_of_case()
    {
        var trip = Trip();
        JeopardyService.Reset(trip, new Random(1));

        Assert.True(JeopardyService.IsPartyCode(trip, trip.Jeopardy.Game.PartyCode.ToLowerInvariant()));
        Assert.False(JeopardyService.IsPartyCode(trip, "ZZZZ"));
        Assert.False(JeopardyService.IsPartyCode(trip, ""));
    }

    /// <summary>
    /// The one collision that must be impossible rather than unlikely: the code on the wall coming
    /// out equal to the host's, which would hand the room the answer sheet.
    /// </summary>
    [Fact]
    public void The_door_recognises_itself_and_nothing_else()
    {
        var trip = Trip();

        for (var seed = 0; seed < 200; seed++)
        {
            JeopardyService.Reset(trip, new Random(seed));

            Assert.True(JeopardyService.IsPartyCode(trip, trip.Jeopardy.Game.PartyCode));
            Assert.True(JeopardyService.IsPartyCode(trip, trip.Jeopardy.Game.PartyCode.ToLowerInvariant()));
            Assert.False(JeopardyService.IsPartyCode(trip, "ZZZZ"));
        }
    }

    [Fact]
    public void Codes_avoid_characters_that_get_misread_aloud()
    {
        var trip = Trip();
        for (var seed = 0; seed < 40; seed++)
        {
            JeopardyService.Reset(trip, new Random(seed));
            Assert.DoesNotContain(trip.Jeopardy.Game.PartyCode, c => "O0I1S5B8".Contains(c));
        }
    }

    /// <summary>
    /// The headless door the testing rail uses. It has to get the board startable rather than
    /// crowd one team, so it deals from a team nobody has joined for before it deals a second
    /// phone to a team that already has one.
    /// </summary>
    [Fact]
    public void The_door_seats_a_team_that_is_missing_before_one_that_is_already_in()
    {
        var trip = Trip();
        JeopardyService.Reset(trip, new Random(1));

        // Two people per team, so "deal from a team nobody has joined for yet" is a rule with
        // something to choose between rather than a list of three.
        foreach (var team in trip.Teams.ToList())
        {
            trip.Roster.Add(new RosterPerson { Id = $"{team.Id}-1", Name = $"{team.Name} one", TeamId = team.Id });
            trip.Roster.Add(new RosterPerson { Id = $"{team.Id}-2", Name = $"{team.Name} two", TeamId = team.Id });
        }

        var teams = new HashSet<string>();

        for (var i = 0; i < trip.Teams.Count; i++)
        {
            var personId = JeopardyService.SeatNextPlayer(trip, new Random(i));

            Assert.NotNull(personId);
            var person = trip.Roster.First(p => p.Id == personId);
            Assert.True(teams.Add(person.TeamId), $"{person.TeamId} was seated twice before every team was in");
        }

        Assert.Equal(trip.Teams.Count, trip.Jeopardy.Game.JoinedTeamIds.Count);
    }
}
