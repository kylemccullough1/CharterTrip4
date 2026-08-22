using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>
/// Read-only calculations over the trip: the countdown and the scoreboard. Pure functions in
/// Core rather than helpers in the web project, so they can be unit tested without a browser —
/// same reason ItineraryService and DayTimeline live here.
/// </summary>
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

    /// <summary>
    /// Teams by points, highest first. Ties keep the order the teams are stored in — which is
    /// JAKE: Jou, Ali, Kyle, Em. Sorting ties alphabetically instead turned that into
    /// Ali, Em, Jou, Kyle every time the board was level, which is most of the weekend.
    /// </summary>
    public static IReadOnlyList<(Team Team, int Total)> Standings(TripData trip) =>
        trip.Teams
            .Select((t, order) => (Team: t, Total: TeamTotal(trip, t.Id), Order: order))
            .OrderByDescending(x => x.Total)
            .ThenBy(x => x.Order)
            .Select(x => (x.Team, x.Total))
            .ToList();
}
