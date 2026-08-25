using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>
/// Every way the itinerary can change, kept out of the Blazor component so it can be unit tested
/// without a browser. Each method mutates the TripData it is handed — callers run them inside
/// ITripStore.MutateAsync, which owns the locking and persistence.
///
/// Two rules hold throughout: an item always has a time (there is no unscheduled state), and
/// anything that changes an item bumps its Version so concurrent editors can be detected.
/// </summary>
public static class ItineraryService
{
    public const int MinDuration = 15;
    public const int MaxDuration = 12 * 60;

    /// <summary>Earliest and latest a card may start: 6am, through to 6am the following morning.</summary>
    public const int EarliestStart = TimeText.DayAnchor;
    public const int LatestStart = TimeText.DayAnchor + TimeText.Day;

    // ------------------------------------------------------------------ days

    public static ItineraryDay AddDay(TripData trip, string name = "New day", string date = "")
    {
        var day = new ItineraryDay { Id = Ids.New("day"), Day = name, Date = date };
        trip.Itinerary.Add(day);
        return day;
    }

    public static void RemoveDay(TripData trip, string dayId) =>
        trip.Itinerary.RemoveAll(d => d.Id == dayId);

    // ----------------------------------------------------------------- items

    public static ItineraryItem? AddItem(TripData trip, string dayId, int? startMinutes = null)
    {
        var day = FindDay(trip, dayId);
        if (day is null) return null;

        var item = new ItineraryItem
        {
            Id = Ids.New("item"),
            StartMinutes = ClampStart(startMinutes ?? ItineraryItem.DefaultStartMinutes),
            DurationMinutes = 60,
            Title = "New item",
            Tag = ItineraryTag.Logistics
        };

        day.Items.Add(item);
        SortDay(day);
        return item;
    }

    /// <summary>
    /// Creates the item a draft describes, once that draft has been saved for the first time.
    ///
    /// The editor used to call <see cref="AddItem(TripData, string, int?)"/> the moment somebody
    /// pressed the plus, which put "New item" on the schedule before they had typed anything —
    /// and left it there when they cancelled. Nothing reaches the trip until this runs.
    /// </summary>
    public static SaveOutcome CreateItem(TripData trip, ItemEdit edit)
    {
        var day = FindDay(trip, edit.DayId);
        if (day is null) return SaveOutcome.Missing;

        day.Items.Add(new ItineraryItem
        {
            Id = edit.ItemId,
            Title = edit.Title,
            Notes = edit.Notes,
            Tag = edit.Tag,
            StartMinutes = ClampStart(edit.StartMinutes),
            DurationMinutes = Math.Clamp(edit.DurationMinutes, MinDuration, MaxDuration)
        });

        SortDay(day);
        return SaveOutcome.Saved;
    }

    public static void RemoveItem(TripData trip, string itemId)
    {
        foreach (var day in trip.Itinerary)
            day.Items.RemoveAll(i => i.Id == itemId);
    }

    // ---------------------------------------------------------------- saving

    /// <summary>
    /// Commit a whole editor session at once.
    ///
    /// Returns <see cref="SaveOutcome.Conflict"/> without writing anything if the item moved on
    /// since the form was opened — that is the entire point of the version stamp. Pass
    /// <paramref name="force"/> once the person has looked at the difference and chosen to win.
    /// </summary>
    public static SaveOutcome ApplyEdit(TripData trip, ItemEdit edit, bool force = false)
    {
        var (day, item) = Locate(trip, edit.ItemId);
        if (day is null || item is null) return SaveOutcome.Missing;
        if (!force && item.Version != edit.BaseVersion) return SaveOutcome.Conflict;

        item.Title = edit.Title;
        item.Notes = edit.Notes;
        item.Tag = edit.Tag;
        item.StartMinutes = ClampStart(edit.StartMinutes);
        item.DurationMinutes = Math.Clamp(edit.DurationMinutes, MinDuration, MaxDuration);
        Touch(item);

        var target = FindDay(trip, edit.DayId) ?? day;
        if (target.Id != day.Id)
        {
            day.Items.Remove(item);
            target.Items.Add(item);
            SortDay(day);
        }

        SortDay(target);
        return SaveOutcome.Saved;
    }

    // ------------------------------------------------------------------ time

    public static void SetStart(TripData trip, string itemId, int startMinutes)
    {
        var (day, item) = Locate(trip, itemId);
        if (day is null || item is null) return;

        item.StartMinutes = ClampStart(startMinutes);
        Touch(item);
        SortDay(day);
    }

    public static void SetDuration(TripData trip, string itemId, int durationMinutes)
    {
        var (_, item) = Locate(trip, itemId);
        if (item is null) return;

        item.DurationMinutes = Math.Clamp(durationMinutes, MinDuration, MaxDuration);
        Touch(item);
    }

    // ------------------------------------------------------------------ move

    /// <summary>
    /// Move an item to another day. Passing a start time also reschedules it; leaving it null
    /// keeps the same time of day.
    /// </summary>
    public static void MoveToDay(TripData trip, string itemId, string targetDayId, int? startMinutes = null)
    {
        var (day, item) = Locate(trip, itemId);
        var target = FindDay(trip, targetDayId);
        if (day is null || item is null || target is null) return;

        if (startMinutes is not null) item.StartMinutes = ClampStart(startMinutes.Value);
        Touch(item);

        if (day.Id != target.Id)
        {
            day.Items.Remove(item);
            target.Items.Add(item);
        }

        SortDay(day);
        SortDay(target);
    }

    public static void MoveToAdjacentDay(TripData trip, string itemId, int direction)
    {
        var (day, item) = Locate(trip, itemId);
        if (day is null || item is null) return;

        var target = trip.Itinerary.ElementAtOrDefault(trip.Itinerary.IndexOf(day) + direction);
        if (target is not null) MoveToDay(trip, itemId, target.Id);
    }

    /// <summary>
    /// Two items trade places: each takes the other's day, start and length.
    ///
    /// Swapping the whole slot rather than just the start time is deliberate. If only the
    /// starts were exchanged, a 30-minute item landing in a 3-hour slot would leave the two
    /// overlapping, which is the pile-up this is meant to avoid.
    /// </summary>
    public static void SwapSlots(TripData trip, string firstId, string secondId)
    {
        if (firstId == secondId) return;

        var (firstDay, first) = Locate(trip, firstId);
        var (secondDay, second) = Locate(trip, secondId);
        if (firstDay is null || first is null || secondDay is null || second is null) return;

        (first.StartMinutes, second.StartMinutes) = (second.StartMinutes, first.StartMinutes);
        (first.DurationMinutes, second.DurationMinutes) = (second.DurationMinutes, first.DurationMinutes);
        Touch(first);
        Touch(second);

        if (firstDay.Id != secondDay.Id)
        {
            firstDay.Items.Remove(first);
            secondDay.Items.Remove(second);
            secondDay.Items.Add(first);
            firstDay.Items.Add(second);
        }

        SortDay(firstDay);
        SortDay(secondDay);
    }

    // --------------------------------------------------------------- helpers

    /// <summary>Record that this item changed, so open editors know their copy is stale.</summary>
    public static void Touch(ItineraryItem item) => item.Version++;

    /// <summary>Keeps trip.json in chronological order.</summary>
    public static void SortDay(ItineraryDay day) =>
        day.Items = day.Items
            .OrderBy(i => i.StartMinutes)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static void SortDayByTime(TripData trip, string dayId)
    {
        var day = FindDay(trip, dayId);
        if (day is not null) SortDay(day);
    }

    public static int ClampStart(int minutes) => Math.Clamp(minutes, EarliestStart, LatestStart);

    public static ItineraryDay? FindDay(TripData trip, string dayId) =>
        trip.Itinerary.FirstOrDefault(d => d.Id == dayId);

    public static (ItineraryDay? Day, ItineraryItem? Item) Locate(TripData trip, string itemId)
    {
        foreach (var day in trip.Itinerary)
        {
            var item = day.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is not null) return (day, item);
        }
        return (null, null);
    }
}
