using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>
/// Every way the itinerary can change, kept out of the Blazor component so it can be unit tested
/// without a browser. Each method mutates the TripData it is handed — callers run them inside
/// ITripStore.MutateAsync, which owns the locking and persistence.
///
/// Since v2 an item's position on the page comes from its start time, not its index in the list,
/// so "move" means "change the time" rather than "reorder". The list is still kept sorted, purely
/// so trip.json reads in chronological order.
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
            StartMinutes = startMinutes is null ? null : ClampStart(startMinutes.Value),
            DurationMinutes = 60,
            Title = "New item",
            Tag = ItineraryTag.Logistics
        };

        day.Items.Add(item);
        SortDay(day);
        return item;
    }

    public static void RemoveItem(TripData trip, string itemId)
    {
        foreach (var day in trip.Itinerary)
            day.Items.RemoveAll(i => i.Id == itemId);
    }

    // ------------------------------------------------------------------ time

    public static void SetStart(TripData trip, string itemId, int startMinutes)
    {
        var (day, item) = Locate(trip, itemId);
        if (day is null || item is null) return;

        item.StartMinutes = ClampStart(startMinutes);
        item.TimeNote = null;
        SortDay(day);
    }

    /// <summary>Shift the start by a delta, snapped to the step. The ↑ ↓ buttons use this.</summary>
    public static void NudgeStart(TripData trip, string itemId, int deltaMinutes, int step = 15)
    {
        var (day, item) = Locate(trip, itemId);
        if (day is null || item is null || !item.IsScheduled) return;

        item.StartMinutes = ClampStart(TimeText.Snap(item.StartMinutes!.Value + deltaMinutes, step));
        SortDay(day);
    }

    public static void SetDuration(TripData trip, string itemId, int durationMinutes)
    {
        var (_, item) = Locate(trip, itemId);
        if (item is null) return;

        item.DurationMinutes = Math.Clamp(durationMinutes, MinDuration, MaxDuration);
    }

    public static void NudgeDuration(TripData trip, string itemId, int deltaMinutes)
    {
        var (_, item) = Locate(trip, itemId);
        if (item is null) return;

        item.DurationMinutes = Math.Clamp(item.DurationMinutes + deltaMinutes, MinDuration, MaxDuration);
    }

    /// <summary>Take an item off the grid and put it back in the unscheduled tray.</summary>
    public static void Unschedule(TripData trip, string itemId)
    {
        var (_, item) = Locate(trip, itemId);
        if (item is null) return;

        item.StartMinutes = null;
    }

    // ------------------------------------------------------------------ move

    /// <summary>
    /// Move an item to another day. Passing a start time also reschedules it; leaving it null
    /// keeps the same time of day, which is what the « » buttons want.
    /// </summary>
    public static void MoveToDay(TripData trip, string itemId, string targetDayId, int? startMinutes = null)
    {
        var (day, item) = Locate(trip, itemId);
        var target = FindDay(trip, targetDayId);
        if (day is null || item is null || target is null) return;

        if (startMinutes is not null)
        {
            item.StartMinutes = ClampStart(startMinutes.Value);
            item.TimeNote = null;
        }

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
        if (target is null) return;

        MoveToDay(trip, itemId, target.Id);
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
        (first.TimeNote, second.TimeNote) = (second.TimeNote, first.TimeNote);

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

    /// <summary>Keeps trip.json in chronological order. Unscheduled items sink to the bottom.</summary>
    public static void SortDay(ItineraryDay day) =>
        day.Items = day.Items
            .OrderBy(i => i.StartMinutes ?? int.MaxValue)
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
