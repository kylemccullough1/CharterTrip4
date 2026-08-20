using CharterTrip.Core.Models;

namespace CharterTrip.Web.Services;

/// <summary>Small read-only calculations the Home page needs. Kept out of the markup.</summary>
public static class TripSummary
{
    public readonly record struct Countdown(string Headline, string Detail);

    public static Countdown ToStart(TripInfo trip, DateTimeOffset now)
    {
        if (now >= trip.EndsAt) return new("That's a wrap", "See you at the 5th annual");
        if (now >= trip.StartsAt) return new("Happening now", "Go outside");

        var remaining = trip.StartsAt - now;
        var days = (int)remaining.TotalDays;
        var hours = remaining.Hours;

        return days > 0
            ? new($"{days} {(days == 1 ? "day" : "days")}", $"{hours} hrs to check-in")
            : new($"{hours} hrs", "until check-in");
    }

    public static int TeamTotal(TripData trip, string teamId) =>
        trip.Scores.Where(s => s.TeamId == teamId).Sum(s => s.Points);

    public static IReadOnlyList<(Team Team, int Total)> Standings(TripData trip) =>
        trip.Teams
            .Select(t => (Team: t, Total: TeamTotal(trip, t.Id)))
            .OrderByDescending(x => x.Total)
            .ThenBy(x => x.Team.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
