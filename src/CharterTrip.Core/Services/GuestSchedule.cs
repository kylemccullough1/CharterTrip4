using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>
/// The schedule as a guest should see it.
///
/// A run of game items next to each other becomes one block called "Games". The point is not
/// tidiness — it is that half the fun of the weekend is nobody knowing what they are about to
/// play, and a schedule that lists "Jeopardy, 8:00 PM" gives that away days in advance. The
/// committee's own view is untouched; they need the real names to run the thing.
/// </summary>
public static class GuestSchedule
{
    public const string GamesTitle = "Games";

    /// <summary>
    /// The items of a day, with adjacent games merged. Never mutates the day — merged runs are
    /// fresh objects that exist only for this render.
    /// </summary>
    public static List<ItineraryItem> Screen(IEnumerable<ItineraryItem> items)
    {
        // Time order, because "next to each other" is a fact about the schedule rather than
        // about the order somebody happened to add things in.
        var ordered = items.OrderBy(i => i.StartMinutes).ThenBy(i => i.DurationMinutes).ToList();
        var screened = new List<ItineraryItem>(ordered.Count);

        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Tag != ItineraryTag.Game)
            {
                screened.Add(ordered[i]);
                continue;
            }

            // Swallow the whole run. Anything not a game ends it, which is what keeps a game
            // either side of dinner as two blocks rather than one four-hour one.
            var start = i;
            while (i + 1 < ordered.Count && ordered[i + 1].Tag == ItineraryTag.Game) i++;

            screened.Add(Merge(ordered.GetRange(start, i - start + 1)));
        }

        return screened;
    }

    private static ItineraryItem Merge(List<ItineraryItem> run)
    {
        var first = run[0];

        // One game on its own still gets the anonymous title — otherwise the single-game case
        // is the one that gives the surprise away.
        if (run.Count == 1) return Anonymised(first, first.DurationMinutes);

        var endsAt = run.Max(i => i.StartMinutes + i.DurationMinutes);
        return Anonymised(first, endsAt - first.StartMinutes);
    }

    /// <summary>
    /// A copy carrying only what a guest may know: when it is, and that it is games. The id is
    /// the first item's, so a re-render keys the block to the same thing.
    /// </summary>
    private static ItineraryItem Anonymised(ItineraryItem first, int durationMinutes) => new()
    {
        Id = first.Id,
        Title = GamesTitle,
        Notes = "",
        Tag = ItineraryTag.Game,
        StartMinutes = first.StartMinutes,
        DurationMinutes = durationMinutes,
        Version = first.Version
    };
}
