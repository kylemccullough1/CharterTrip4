using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

public class TeamServiceTests
{
    private static TripData Trip()
    {
        var trip = new TripData
        {
            Teams =
            [
                new Team { Id = "jou",  Name = "Team Jou",  Lead = "JouJou" },
                new Team { Id = "ali",  Name = "Team Ali",  Lead = "Ali Hussain" },
                new Team { Id = "kyle", Name = "Team Kyle", Lead = "Kyle McCullough" },
                new Team { Id = "em",   Name = "Team Em",   Lead = "Emily Ea" }
            ]
        };

        void Add(string name, string teamId) =>
            trip.Roster.Add(new RosterPerson { Id = $"p-{name.ToLowerInvariant().Replace(' ', '-')}", Name = name, TeamId = teamId });

        Add("JouJou", "jou"); Add("Zach Montebon", "jou"); Add("Brandon Pham", "jou");
        Add("Ali Hussain", "ali"); Add("Cat Xiong", "ali");
        Add("Kyle McCullough", "kyle"); Add("Evie Fox", "kyle");
        Add("Emily Ea", "em"); Add("Dillon Lam", "em");
        return trip;
    }

    private static List<string> Names(TripData trip, string teamId) =>
        TeamService.Rosters(trip).Single(r => r.Team.Id == teamId).Members.Select(m => m.Name).ToList();

    [Fact]
    public void Rosters_come_back_in_stored_team_order()
    {
        Assert.Equal(["jou", "ali", "kyle", "em"], TeamService.Rosters(Trip()).Select(r => r.Team.Id));
    }

    [Fact]
    public void The_lead_is_listed_first_and_the_rest_alphabetically()
    {
        Assert.Equal(["JouJou", "Brandon Pham", "Zach Montebon"], Names(Trip(), "jou"));
    }

    [Fact]
    public void Moving_someone_takes_them_off_their_old_team()
    {
        var trip = Trip();
        var zach = trip.Roster.Single(p => p.Name == "Zach Montebon");

        TeamService.MovePerson(trip, zach.Id, "em");

        Assert.DoesNotContain("Zach Montebon", Names(trip, "jou"));
        Assert.Contains("Zach Montebon", Names(trip, "em"));
    }

    [Fact]
    public void A_person_can_only_ever_be_on_one_team()
    {
        var trip = Trip();
        var cat = trip.Roster.Single(p => p.Name == "Cat Xiong");

        TeamService.MovePerson(trip, cat.Id, "kyle");
        TeamService.MovePerson(trip, cat.Id, "em");

        var appearances = TeamService.Rosters(trip).Count(r => r.Members.Any(m => m.Id == cat.Id));
        Assert.Equal(1, appearances);
        Assert.Contains("Cat Xiong", Names(trip, "em"));
    }

    [Fact]
    public void Moving_to_a_team_that_does_not_exist_is_ignored()
    {
        var trip = Trip();
        var cat = trip.Roster.Single(p => p.Name == "Cat Xiong");

        TeamService.MovePerson(trip, cat.Id, "nonsense");

        Assert.Equal("ali", cat.TeamId);
    }

    [Fact]
    public void Someone_can_be_taken_off_a_team_and_lands_in_unassigned()
    {
        var trip = Trip();
        var cat = trip.Roster.Single(p => p.Name == "Cat Xiong");

        TeamService.MovePerson(trip, cat.Id, null);

        Assert.DoesNotContain("Cat Xiong", Names(trip, "ali"));
        Assert.Contains(TeamService.Unassigned(trip), p => p.Name == "Cat Xiong");
    }

    [Fact]
    public void Someone_pointing_at_a_deleted_team_shows_as_unassigned_rather_than_vanishing()
    {
        var trip = Trip();
        trip.Roster.Add(new RosterPerson { Id = "p-ghost", Name = "Ghost", TeamId = "deleted-team" });

        Assert.Contains(TeamService.Unassigned(trip), p => p.Name == "Ghost");
    }

    [Fact]
    public void Everyone_on_the_roster_appears_exactly_once_across_teams_and_unassigned()
    {
        var trip = Trip();
        TeamService.MovePerson(trip, trip.Roster[1].Id, null);

        var shown = TeamService.Rosters(trip).SelectMany(r => r.Members)
            .Concat(TeamService.Unassigned(trip))
            .Select(p => p.Id)
            .ToList();

        Assert.Equal(trip.Roster.Count, shown.Count);
        Assert.Equal(trip.Roster.Count, shown.Distinct().Count());
    }

    [Fact]
    public void Renaming_a_team_trims_and_refuses_a_blank()
    {
        var trip = Trip();

        TeamService.RenameTeam(trip, "jou", "  The Jouggernauts  ");
        Assert.Equal("The Jouggernauts", TeamService.FindTeam(trip, "jou")!.Name);

        TeamService.RenameTeam(trip, "jou", "   ");
        Assert.Equal("The Jouggernauts", TeamService.FindTeam(trip, "jou")!.Name);
    }

    [Fact]
    public void Recolouring_a_team_takes_a_palette_colour()
    {
        var trip = Trip();

        TeamService.RecolorTeam(trip, "jou", "#b07cc6");
        Assert.Equal("#b07cc6", TeamService.FindTeam(trip, "jou")!.Color);
    }

    /// <summary>Off the palette is fine now — there is a colour wheel — as long as it is a colour.</summary>
    [Theory]
    [InlineData("#123456", "#123456")]
    [InlineData("#ABC", "#aabbcc")]
    [InlineData("  #7F00ff ", "#7f00ff")]
    public void Recolouring_a_team_takes_any_hex_colour_in_the_one_spelling(string given, string stored)
    {
        var trip = Trip();

        TeamService.RecolorTeam(trip, "jou", given);

        Assert.Equal(stored, TeamService.FindTeam(trip, "jou")!.Color);
    }

    /// <summary>
    /// The leading team's colour is written into the site's stylesheet, so anything that is not
    /// plainly a hex colour must not reach the field at all.
    /// </summary>
    [Theory]
    [InlineData("red")]
    [InlineData("#c94f5a; background: url(evil)")] // a colour with a tail on it
    [InlineData("#12345")]
    [InlineData("")]
    public void Recolouring_a_team_refuses_anything_that_is_not_a_colour(string given)
    {
        var trip = Trip();
        var before = TeamService.FindTeam(trip, "jou")!.Color;

        TeamService.RecolorTeam(trip, "jou", given);

        Assert.Equal(before, TeamService.FindTeam(trip, "jou")!.Color);
    }

    [Fact]
    public void Recolouring_stores_the_palettes_spelling_not_the_callers()
    {
        var trip = Trip();

        TeamService.RecolorTeam(trip, "jou", "#D98C3F");

        Assert.Equal("#d98c3f", TeamService.FindTeam(trip, "jou")!.Color);
    }

    [Fact]
    public void Recolouring_a_team_that_is_not_there_changes_nothing()
    {
        var trip = Trip();

        TeamService.RecolorTeam(trip, "nobody", "#4fb0a5");

        Assert.DoesNotContain(trip.Teams, t => t.Color == "#4fb0a5");
    }

    [Fact]
    public void The_lead_does_not_count_as_a_player()
    {
        var jou = TeamService.Rosters(Trip()).Single(r => r.Team.Id == "jou");

        Assert.Equal(3, jou.Count);        // JouJou, Brandon, Zach
        Assert.Equal(2, jou.PlayerCount);  // the lead runs the team rather than playing for it
    }

    // -------------------------------------------------------------- leads

    [Fact]
    public void A_lead_cannot_be_moved_to_another_team()
    {
        // The team is named after its lead and they run it, so they are fixed to it.
        var trip = Trip();
        var jouJou = trip.Roster.Single(p => p.Name == "JouJou");

        TeamService.MovePerson(trip, jouJou.Id, "em");

        Assert.Equal("jou", jouJou.TeamId);
        Assert.Contains("JouJou", Names(trip, "jou"));
    }

    [Fact]
    public void A_lead_cannot_be_taken_off_a_team_either()
    {
        var trip = Trip();
        var ali = trip.Roster.Single(p => p.Name == "Ali Hussain");

        TeamService.MovePerson(trip, ali.Id, null);

        Assert.Equal("ali", ali.TeamId);
        Assert.DoesNotContain(TeamService.Unassigned(trip), p => p.Name == "Ali Hussain");
    }

    [Fact]
    public void Everyone_else_still_moves_freely()
    {
        var trip = Trip();
        var zach = trip.Roster.Single(p => p.Name == "Zach Montebon");

        Assert.False(TeamService.IsLocked(trip, zach));
        TeamService.MovePerson(trip, zach.Id, "kyle");
        Assert.Equal("kyle", zach.TeamId);
    }

    [Fact]
    public void IsLocked_only_applies_to_leads()
    {
        var trip = Trip();

        Assert.True(TeamService.IsLocked(trip, trip.Roster.Single(p => p.Name == "Kyle McCullough")));
        Assert.False(TeamService.IsLocked(trip, trip.Roster.Single(p => p.Name == "Evie Fox")));
    }
}
