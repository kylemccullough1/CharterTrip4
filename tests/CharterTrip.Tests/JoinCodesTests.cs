using CharterTrip.Core.Models;
using CharterTrip.Core.Services;
using CharterTrip.Infrastructure.Seed;

namespace CharterTrip.Tests;

/// <summary>
/// The one front door. Every code on this trip — a person's own link, a Jeopardy buzzer code, the
/// host code — resolves here, so this is the test that stops two games disagreeing about who is
/// holding the phone.
/// </summary>
public class JoinCodesTests
{
    private static TripData Trip()
    {
        var trip = SeedLoader.Load();
        JoinCodes.EnsureTokens(trip);
        JeopardyService.EnsureCodes(trip, new Random(1));
        return trip;
    }

    [Fact]
    public void Everybody_gets_a_token_and_they_are_all_different()
    {
        var trip = SeedLoader.Load();

        Assert.True(JoinCodes.EnsureTokens(trip));

        Assert.All(trip.Roster, p => Assert.False(string.IsNullOrWhiteSpace(p.JoinToken)));
        Assert.Equal(
            trip.Roster.Count,
            trip.Roster.Select(p => p.JoinToken!).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Tokens_are_long_enough_to_not_be_guessed_and_avoid_lookalike_characters()
    {
        var trip = Trip();

        Assert.All(trip.Roster, p =>
        {
            // Ten characters. A buzzer code is four, which is fine for something shown on a wall
            // and reset between games; a join token is the whole proof of somebody's identity and
            // lasts the weekend.
            Assert.Equal(10, p.JoinToken!.Length);

            // No O/0, I/1, S/5, B/8 — these get read off a name tag by somebody holding a drink.
            Assert.DoesNotContain(p.JoinToken, c => "O0I1S5B8".Contains(c));
            Assert.All(p.JoinToken, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
        });
    }

    [Fact]
    public void Running_again_reissues_nothing()
    {
        var trip = Trip();
        var before = trip.Roster.ToDictionary(p => p.Id, p => p.JoinToken);

        // Idempotent, which matters because this runs as a migration on every load — and because
        // a reissued token is a name tag that has stopped working.
        Assert.False(JoinCodes.EnsureTokens(trip));
        Assert.All(trip.Roster, p => Assert.Equal(before[p.Id], p.JoinToken));
    }

    [Fact]
    public void A_new_person_gets_a_token_without_disturbing_anybody_else()
    {
        var trip = Trip();
        var before = trip.Roster.ToDictionary(p => p.Id, p => p.JoinToken);

        trip.Roster.Add(new RosterPerson { Id = "p-late", Name = "Late Addition", TeamId = trip.Teams[0].Id });

        Assert.True(JoinCodes.EnsureTokens(trip));
        Assert.False(string.IsNullOrWhiteSpace(trip.Roster.Single(p => p.Id == "p-late").JoinToken));
        Assert.All(before, kv => Assert.Equal(kv.Value, trip.Roster.Single(p => p.Id == kv.Key).JoinToken));
    }

    [Fact]
    public void A_persons_token_resolves_to_them_and_carries_their_team()
    {
        var trip = Trip();
        var person = trip.Roster[3];

        var match = JoinCodes.Resolve(trip, person.JoinToken);

        Assert.Equal(CodeKind.Person, match.Kind);
        Assert.Equal(person.Id, match.PersonId);

        // The team comes along, so somebody signed in as themselves can use the buzzer without
        // also typing the team's code off the wall.
        Assert.Equal(person.TeamId, match.TeamId);
    }

    [Fact]
    public void Case_spaces_and_dashes_do_not_matter()
    {
        var trip = Trip();
        var person = trip.Roster[0];
        var token = person.JoinToken!;

        // Somebody copying a code off a card adds punctuation, and a phone keyboard may not
        // capitalise. None of that should be the reason they cannot get in.
        foreach (var typed in new[]
                 {
                     token.ToLowerInvariant(),
                     $" {token} ",
                     $"{token[..5]}-{token[5..]}",
                     $"{token[..3]} {token[3..]}"
                 })
        {
            Assert.Equal(person.Id, JoinCodes.Resolve(trip, typed).PersonId);
        }
    }

    [Fact]
    public void A_buzzer_code_resolves_to_its_team_and_the_host_code_to_the_host()
    {
        var trip = Trip();
        var teamId = trip.Teams[1].Id;

        var team = JoinCodes.Resolve(trip, trip.Jeopardy.Game.BuzzerCodes[teamId]);
        Assert.Equal(CodeKind.BuzzerTeam, team.Kind);
        Assert.Equal(teamId, team.TeamId);
        Assert.Null(team.PersonId);

        var host = JoinCodes.Resolve(trip, trip.Jeopardy.Game.HostCode);
        Assert.Equal(CodeKind.BuzzerHost, host.Kind);
        Assert.Null(host.PersonId);
        Assert.Null(host.TeamId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("----")]
    [InlineData("ZZZZ")]
    [InlineData("NOTAREALCODE")]
    public void Anything_else_resolves_to_nothing(string? code)
    {
        var match = JoinCodes.Resolve(Trip(), code);

        Assert.False(match.Found);
        Assert.Equal(CodeKind.Unknown, match.Kind);
        Assert.Null(match.PersonId);
        Assert.Null(match.TeamId);
    }

    [Fact]
    public void A_person_wins_over_a_buzzer_code_that_happens_to_collide()
    {
        var trip = Trip();
        var teamId = trip.Teams[0].Id;
        var collision = trip.Jeopardy.Game.BuzzerCodes[teamId];

        // Contrived — a 4-character buzzer code cannot equal a 10-character token by accident.
        // The point is the precedence rule: being yourself outranks being a seat at a table,
        // because every game can derive the seat from the person and not the other way round.
        trip.Roster[0].JoinToken = collision;

        var match = JoinCodes.Resolve(trip, collision);

        Assert.Equal(CodeKind.Person, match.Kind);
        Assert.Equal(trip.Roster[0].Id, match.PersonId);
    }

    [Fact]
    public void A_person_with_no_token_is_not_a_way_in()
    {
        var trip = Trip();
        trip.Roster[0].JoinToken = null;
        trip.Roster[1].JoinToken = "";

        // An empty token must never match an empty or whitespace code — otherwise anybody typing
        // nothing signs in as whoever happens to be missing a token.
        Assert.False(JoinCodes.Resolve(trip, null).Found);
        Assert.False(JoinCodes.Resolve(trip, "").Found);
        Assert.False(JoinCodes.Resolve(trip, "  ").Found);
    }

    [Fact]
    public void The_committee_are_still_the_four_admins_and_the_rest_are_not()
    {
        var trip = Trip();

        // Admin comes from the roster now, not from the act of signing in. This is the fact that
        // makes it safe to hand twenty-one people a link: theirs does not make them an admin.
        Assert.Equal(4, trip.Roster.Count(p => p.Role == TripRole.Admin));
        Assert.Equal(21, trip.Roster.Count(p => p.Role != TripRole.Admin));
    }

    [Fact]
    public void The_path_for_a_person_is_their_login_link()
    {
        var trip = Trip();
        var person = trip.Roster[0];

        Assert.Equal($"/login/{person.JoinToken}", JoinCodes.PathFor(person));
    }
}
