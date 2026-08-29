using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>A team with the people currently on it.</summary>
public sealed record TeamRoster(Team Team, IReadOnlyList<RosterPerson> Members)
{
    /// <summary>Everyone on the team, lead included.</summary>
    public int Count => Members.Count;

    /// <summary>
    /// Everyone except the lead. The lead runs the team rather than playing for it, so this is
    /// the number that matters when checking the sides are fair.
    /// </summary>
    public int PlayerCount => Members.Count(p => !TeamService.IsLead(Team, p));
}

/// <summary>
/// Team membership lives on the person, not the team — a RosterPerson carries a TeamId and the
/// teams themselves hold no member list. One place to change when somebody swaps sides, and no
/// way for the two halves to disagree.
/// </summary>
public static class TeamService
{
    /// <summary>Teams in stored order — JAKE — each with its members, the lead first.</summary>
    public static IReadOnlyList<TeamRoster> Rosters(TripData trip) =>
        trip.Teams
            .Select(team => new TeamRoster(team, Members(trip, team)))
            .ToList();

    private static List<RosterPerson> Members(TripData trip, Team team) =>
        trip.Roster
            .Where(p => p.TeamId == team.Id)
            .OrderByDescending(p => IsLead(team, p))
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// A team lead is fixed. The team is named after them and they run it, so they cannot be
    /// renamed or moved to another side. Enforced here rather than only hidden in the markup,
    /// so a future page cannot break the structure by accident.
    /// </summary>
    public static bool IsLocked(TripData trip, RosterPerson person) =>
        trip.Teams.Any(team => IsLead(team, person));

    /// <summary>The team lead, matched by name since that is how the roster records it.</summary>
    public static bool IsLead(Team team, RosterPerson person) =>
        !string.IsNullOrWhiteSpace(team.Lead) &&
        string.Equals(team.Lead, person.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Anyone whose TeamId is blank or points at a team that no longer exists. They would
    /// otherwise vanish from the page entirely, which is a bad way to lose a person.
    /// </summary>
    public static IReadOnlyList<RosterPerson> Unassigned(TripData trip)
    {
        var ids = trip.Teams.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return trip.Roster
            .Where(p => string.IsNullOrWhiteSpace(p.TeamId) || !ids.Contains(p.TeamId))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Move somebody onto a team, or pass null/empty to take them off one.</summary>
    public static void MovePerson(TripData trip, string personId, string? teamId)
    {
        var person = FindPerson(trip, personId);
        if (person is null || IsLocked(trip, person)) return;

        if (string.IsNullOrWhiteSpace(teamId))
        {
            person.TeamId = "";
            return;
        }

        if (trip.Teams.Any(t => t.Id == teamId)) person.TeamId = teamId;
    }


    public static void RenameTeam(TripData trip, string teamId, string name)
    {
        var team = FindTeam(trip, teamId);
        if (team is null) return;

        var trimmed = name.Trim();
        if (trimmed.Length > 0) team.Name = trimmed;
    }

    /// <summary>
    /// Repaints a team, and refuses anything that is not one of <see cref="AccentPalette.Swatches"/>.
    ///
    /// Narrow on purpose. A team's colour is not only a dot on this page — the leading team's colour
    /// is written into the site's stylesheet by <see cref="AccentPalette.Leader"/>, so the set of
    /// things it can be is the set of things somebody picked deliberately, not whatever arrives.
    /// </summary>
    public static void RecolorTeam(TripData trip, string teamId, string color)
    {
        // Any hex colour, not only the palette's ten: the swatches are the quick picks and the
        // colour wheel beside them is the rest. Still parsed, never trusted — the leading team's
        // colour is written into the stylesheet, so nothing that is not plainly a colour gets in.
        var team = FindTeam(trip, teamId);
        if (team is null || AccentPalette.Canonical(color) is not { } hex) return;

        team.Color = hex;
    }

    public static RosterPerson? FindPerson(TripData trip, string personId) =>
        trip.Roster.FirstOrDefault(p => p.Id == personId);

    public static Team? FindTeam(TripData trip, string teamId) =>
        trip.Teams.FirstOrDefault(t => t.Id == teamId);

}
