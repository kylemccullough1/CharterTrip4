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

    private static void OpenClue(TripData trip, string clueId) =>
        JeopardyService.PickClue(trip, clueId, T0);

    // ------------------------------------------------------------------ setup

    [Fact]
    public void Reset_gives_every_team_a_code_and_the_host_one_too()
    {
        var trip = Trip();
        JeopardyService.Reset(trip, new Random(1));

        var game = trip.Jeopardy.Game;
        Assert.Equal(3, game.BuzzerCodes.Count);
        Assert.All(game.BuzzerCodes.Values, c => Assert.Equal(4, c.Length));
        Assert.Equal(4, game.HostCode.Length);
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
        var before = trip.Jeopardy.Game.BuzzerCodes["jou"];

        JeopardyService.Reset(trip, new Random(9));

        Assert.NotEqual(before, trip.Jeopardy.Game.BuzzerCodes["jou"]);
        Assert.Null(JeopardyService.TeamForCode(trip, before));
    }

    [Fact]
    public void The_buzz_off_hands_the_first_pick_to_the_fastest_team()
    {
        var trip = Trip();
        JeopardyService.Reset(trip, new Random(1));
        JeopardyService.StartBuzzOff(trip, T0);

        JeopardyService.Buzz(trip, "kyle", At(420));
        JeopardyService.Buzz(trip, "jou", At(180));      // arrives later but is not the fastest
        JeopardyService.SettleBuzzOff(trip);

        Assert.Equal("kyle", trip.Jeopardy.Game.PickingTeamId);
        Assert.False(trip.Jeopardy.Game.BuzzersOpen);
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
        Assert.Contains("c0-10", trip.Jeopardy.Game.UsedClueIds);
        Assert.Equal(JeopardyPhase.Board, trip.Jeopardy.Game.Phase);
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

        Assert.Empty(trip.Scores);
        Assert.Equal("jou", trip.Jeopardy.Game.PickingTeamId);
        Assert.Contains("c0-10", trip.Jeopardy.Game.UsedClueIds);
    }

    [Fact]
    public void A_used_clue_cannot_be_picked_again()
    {
        var trip = Trip();
        OpenClue(trip, "c0-5");
        JeopardyService.Buzz(trip, "ali", At(100));
        JeopardyService.JudgeCorrect(trip, T0);

        OpenClue(trip, "c0-5");

        Assert.Null(trip.Jeopardy.Game.CurrentClueId);
    }

    [Fact]
    public void The_board_moves_to_the_final_once_the_last_clue_is_played()
    {
        var trip = Trip(categories: 1, perCategory: 2);

        foreach (var id in new[] { "c0-5", "c0-10" })
        {
            OpenClue(trip, id);
            JeopardyService.Buzz(trip, "ali", At(100));
            JeopardyService.JudgeCorrect(trip, T0);
        }

        Assert.True(JeopardyService.AllCluesUsed(trip));
        Assert.Equal(JeopardyPhase.Final, trip.Jeopardy.Game.Phase);
    }

    // ------------------------------------------------------------------ final

    [Fact]
    public void Final_answers_are_recorded_per_team_and_can_be_changed_until_it_closes()
    {
        var trip = Trip();
        JeopardyService.StartFinal(trip);

        JeopardyService.SubmitFinalAnswer(trip, "jou", "  first go  ");
        JeopardyService.SubmitFinalAnswer(trip, "jou", "second go");

        Assert.Equal("second go", trip.Jeopardy.Game.FinalAnswers["jou"]);
    }

    [Fact]
    public void Final_answers_are_ignored_outside_the_final()
    {
        var trip = Trip();
        JeopardyService.SubmitFinalAnswer(trip, "jou", "too early");

        Assert.Empty(trip.Jeopardy.Game.FinalAnswers);
    }

    [Fact]
    public void Marking_the_final_pays_a_flat_thirty_and_only_once()
    {
        var trip = Trip();
        JeopardyService.StartFinal(trip);

        JeopardyService.MarkFinal(trip, "jou", correct: true, T0);
        JeopardyService.MarkFinal(trip, "jou", correct: true, T0);

        Assert.Equal(30, JeopardyService.ScoreFor(trip, "jou"));
    }

    [Fact]
    public void Unmarking_the_final_takes_the_points_back()
    {
        var trip = Trip();
        JeopardyService.StartFinal(trip);
        JeopardyService.MarkFinal(trip, "jou", correct: true, T0);

        JeopardyService.MarkFinal(trip, "jou", correct: false, T0);

        Assert.Equal(0, JeopardyService.ScoreFor(trip, "jou"));
        Assert.DoesNotContain("jou", trip.Jeopardy.Game.FinalCorrectTeamIds);
    }

    [Fact]
    public void A_wrong_final_costs_nothing()
    {
        var trip = Trip();
        JeopardyService.StartFinal(trip);
        JeopardyService.MarkFinal(trip, "jou", correct: false, T0);

        Assert.Equal(0, JeopardyService.ScoreFor(trip, "jou"));
    }

    // ------------------------------------------------------------------ codes

    [Fact]
    public void A_code_resolves_to_its_team_regardless_of_case()
    {
        var trip = Trip();
        JeopardyService.Reset(trip, new Random(1));
        var code = trip.Jeopardy.Game.BuzzerCodes["ali"];

        Assert.Equal("ali", JeopardyService.TeamForCode(trip, code.ToLowerInvariant()));
        Assert.Null(JeopardyService.TeamForCode(trip, "ZZZZ"));
    }

    [Fact]
    public void The_host_code_is_not_one_of_the_team_codes()
    {
        var trip = Trip();
        JeopardyService.Reset(trip, new Random(7));

        Assert.True(JeopardyService.IsHostCode(trip, trip.Jeopardy.Game.HostCode));
        Assert.DoesNotContain(trip.Jeopardy.Game.HostCode, trip.Jeopardy.Game.BuzzerCodes.Values);
    }

    [Fact]
    public void Codes_avoid_characters_that_get_misread_aloud()
    {
        var trip = Trip();
        for (var seed = 0; seed < 40; seed++)
        {
            JeopardyService.Reset(trip, new Random(seed));
            foreach (var code in trip.Jeopardy.Game.BuzzerCodes.Values.Append(trip.Jeopardy.Game.HostCode))
                Assert.DoesNotContain(code, c => "O0I1S5B8".Contains(c));
        }
    }
}
