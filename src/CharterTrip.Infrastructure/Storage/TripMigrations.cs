using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Infrastructure.Storage;

/// <summary>
/// Brings an older trip.json up to the current model on load.
///
/// The alternative — editing the deployed JSON by hand — works exactly once and teaches you
/// nothing. This runs on every load, is idempotent, and is the pattern every future model change
/// should follow. Anything it cannot interpret is preserved rather than discarded.
/// </summary>
public static class TripMigrations
{
    public const int CurrentVersion = 7;

    /// <summary>Returns true if anything changed, so the caller knows to persist.</summary>
    public static bool Apply(TripData trip)
    {
        var changed = false;

        if (trip.SchemaVersion < 2) changed |= ToV2_StructuredItineraryTimes(trip);
        if (trip.SchemaVersion < 3) changed |= ToV3_AlwaysScheduledAndVersioned(trip);
        if (trip.SchemaVersion < 4) changed |= ToV4_LogisticsRemoved(trip);
        if (trip.SchemaVersion < 5) changed |= ToV5_VenueCorrections(trip);
        if (trip.SchemaVersion < 6) changed |= ToV6_ChecklistGoneAndRosterTrimmed(trip);
        if (trip.SchemaVersion < 7) changed |= ToV7_CountdownAndTagline(trip);

        if (trip.SchemaVersion != CurrentVersion)
        {
            trip.SchemaVersion = CurrentVersion;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// v1 stored itinerary times as free text ("4:00 PM") with no duration. v2 stores structured
    /// minutes plus a length, so the planner can place and size a card.
    ///
    /// Times that parse become real start times. Times that don't ("after dinner", "TBD") are not
    /// thrown away — the item becomes unscheduled and the original text is kept as a note, so it
    /// shows up in the tray rather than vanishing.
    /// </summary>
    private static bool ToV2_StructuredItineraryTimes(TripData trip)
    {
        var changed = false;

        foreach (var day in trip.Itinerary)
        {
            foreach (var item in day.Items)
            {
                if (item.StartMinutesOrNull is not null || string.IsNullOrWhiteSpace(item.LegacyTime))
                {
                    if (ClearLegacy(item)) changed = true;
                    continue;
                }

                var minutes = TimeText.ToMinutes(item.LegacyTime);
                if (minutes == TimeText.Unparseable)
                    item.LegacyTimeNote = item.LegacyTime!.Trim();
                else
                    item.StartMinutes = ItineraryService.ClampStart(minutes);

                ClearLegacy(item);
                changed = true;
            }

            if (InferDurations(day)) changed = true;
            ItineraryService.SortDay(day);
        }

        return changed;
    }

    /// <summary>
    /// v2 allowed an item to have no time at all, which existed only to hold times the v1
    /// parser could not read. That turned into a whole "unscheduled tray" concept in the UI for
    /// the sake of a case that never actually occurs in the data, so v3 removes it: every item
    /// has a time. Anything that lacked one lands at midday with its original wording preserved
    /// in the notes rather than dropped.
    ///
    /// v3 also introduces per-item Version stamps, used to detect two people editing at once.
    /// </summary>
    private static bool ToV3_AlwaysScheduledAndVersioned(TripData trip)
    {
        var changed = false;

        foreach (var item in trip.Itinerary.SelectMany(d => d.Items))
        {
            if (item.StartMinutesOrNull is null)
            {
                item.StartMinutes = ItineraryItem.DefaultStartMinutes;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(item.LegacyTimeNote))
            {
                var note = item.LegacyTimeNote!.Trim();
                item.Notes = string.IsNullOrWhiteSpace(item.Notes) ? note : $"{note} — {item.Notes}";
                changed = true;
            }

            if (item.LegacyTimeNote is not null)
            {
                item.LegacyTimeNote = null;
                changed = true;
            }

            if (item.Version < 1)
            {
                item.Version = 1;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// v4 removed the committee bookkeeping — budget, shopping, payment tracking, shirt sizes,
    /// per-person pricing and the treasurer's Venmo handle. That is all tracked off-site now.
    ///
    /// There is nothing to rewrite here: the properties are gone from the model, so those keys
    /// are ignored on read and simply stop being written. What this does earn is the version
    /// bump, which forces the store to rewrite trip.json on the next load instead of leaving the
    /// dead keys — including the Venmo handle — sitting in the file until someone happens to
    /// make an edit. It also triggers the usual pre-migration archive.
    /// </summary>
    private static bool ToV4_LogisticsRemoved(TripData trip) => true;

    /// <summary>
    /// Corrections to the venue details. These are stored values with no editing screen behind
    /// them, so a migration is the only way to reach the copy running on Azure.
    ///
    /// Each change is conditional on finding the old text, which keeps this idempotent and means
    /// it would leave a hand-edited value alone rather than stamping over it.
    /// </summary>
    private static bool ToV5_VenueCorrections(TripData trip)
    {
        var changed = false;
        var venue = trip.Venue;

        // Check-in is 2pm, matching the itinerary rather than the original booking note.
        if (venue.CheckIn.Contains("4:00 PM", StringComparison.OrdinalIgnoreCase))
        {
            venue.CheckIn = "Friday 2:00 PM";
            changed = true;
        }

        // Drop the "(pushed back - thanks Kyle)" aside from the checkout time.
        var aside = venue.CheckOut.IndexOf('(');
        if (aside > 0)
        {
            venue.CheckOut = venue.CheckOut[..aside].TrimEnd();
            changed = true;
        }

        for (var i = 0; i < venue.Outside.Count; i++)
        {
            if (!venue.Outside[i].StartsWith("Swimming pool", StringComparison.OrdinalIgnoreCase)) continue;
            if (venue.Outside[i] == "Swimming pool") continue;

            venue.Outside[i] = "Swimming pool";
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// v6 drops the packing checklist, which is tracked off-site like the rest of the logistics,
    /// and takes Leon Kien off the roster — he is no longer coming, which brings the headcount
    /// to 25.
    ///
    /// The checklist key disappears on its own once the property is gone from the model. The
    /// roster change does need doing here, since it is the only route to the copy on Azure.
    /// </summary>
    private static bool ToV6_ChecklistGoneAndRosterTrimmed(TripData trip)
    {
        var removed = trip.Roster.RemoveAll(p =>
            string.Equals(p.Id, "p-leon-kien", StringComparison.OrdinalIgnoreCase));

        // True regardless, so the version bump forces the checklist key out of the file rather
        // than leaving it there until someone happens to make an edit.
        return true || removed > 0;
    }

    /// <summary>
    /// The home page counts down to Trip.StartsAt, which was still 4pm — the original check-in
    /// time. v5 corrected the words on the venue page but not the timestamp behind the counter,
    /// so the countdown was quietly running two hours late.
    ///
    /// Also clears the tagline, which no longer has anywhere to appear.
    /// </summary>
    private static bool ToV7_CountdownAndTagline(TripData trip)
    {
        var changed = false;
        var startsAt = trip.Trip.StartsAt;

        if (startsAt != default && startsAt.Hour == 16)
        {
            trip.Trip.StartsAt = new DateTimeOffset(
                startsAt.Year, startsAt.Month, startsAt.Day, 14, 0, 0, startsAt.Offset);
            changed = true;
        }

        return changed;
    }

    private static bool ClearLegacy(ItineraryItem item)
    {
        if (item.LegacyTime is null) return false;
        item.LegacyTime = null;
        return true;
    }

    /// <summary>
    /// v1 had no durations. Rather than making everything a flat hour, run each item up to the
    /// next one — that reproduces the schedule people actually had in mind — but cap it so a long
    /// evening gap doesn't turn dinner into a four-hour block.
    /// </summary>
    private static bool InferDurations(ItineraryDay day)
    {
        const int min = 30, max = 180, fallback = 60;

        var scheduled = day.Items
            .Where(i => i.StartMinutesOrNull is not null)
            .OrderBy(i => i.StartMinutes)
            .ToList();

        var changed = false;

        for (var i = 0; i < scheduled.Count; i++)
        {
            var item = scheduled[i];
            if (item.DurationMinutes > 0 && item.DurationMinutes != 60) continue;

            var next = i + 1 < scheduled.Count ? scheduled[i + 1] : null;
            var inferred = next is null
                ? fallback
                : Math.Clamp(next.StartMinutes - item.StartMinutes, min, max);

            if (item.DurationMinutes == inferred) continue;

            item.DurationMinutes = inferred;
            changed = true;
        }

        return changed;
    }
}
