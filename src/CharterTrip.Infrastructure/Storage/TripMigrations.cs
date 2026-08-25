using CharterTrip.Core.Models;
using CharterTrip.Core.Services;
using CharterTrip.Infrastructure.Seed;

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
    public const int CurrentVersion = 15;

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
        if (trip.SchemaVersion < 8) changed |= ToV8_ShortNames(trip);
        if (trip.SchemaVersion < 9) changed |= ToV9_JeopardyBoard(trip);
        if (trip.SchemaVersion < 10) changed |= ToV10_FinalIsAClue(trip);
        if (trip.SchemaVersion < 11) changed |= ToV11_GuestGuide(trip);
        if (trip.SchemaVersion < 12) changed |= ToV12_CheckInIsTwo(trip);
        if (trip.SchemaVersion < 13) changed |= ToV13_MenuBoard(trip);
        if (trip.SchemaVersion < 14) changed |= ToV14_GroupedEssentials(trip);

        // v15 removed the drive-times list. There is no step for it: the property simply stopped
        // existing on the model, so an older file's copy is ignored on load and gone on the next
        // save, and the cities themselves were never in that list alone — they are on the rows,
        // which is where the dropdown reads them from now.

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

    /// <summary>
    /// The roster arrived as full names but the group knows each other by first names, so the
    /// short forms are what belong on the teams board. Renaming was done in the UI and then the
    /// UI was taken away, which left the deployed copy stuck on the long names — a migration is
    /// the only route to it now.
    ///
    /// Keyed on the person id rather than the old name, so it does not depend on guessing what a
    /// record currently says, and re-running it changes nothing.
    /// </summary>
    private static readonly Dictionary<string, string> ShortNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["p-ali-hussain"] = "Ali",
        ["p-ana-torres"] = "Ana",
        ["p-austin-nguyen"] = "Austin",
        ["p-ben"] = "Ben",
        ["p-brandon-pham"] = "Brandon",
        ["p-cat-xiong"] = "Cat",
        ["p-dillon-lam"] = "Dillon",
        ["p-emily-ea"] = "Emily",
        ["p-esther-niang"] = "Esther",
        ["p-evie-fox"] = "Evie",
        ["p-hao-dinh"] = "Hao",
        ["p-jnguyen"] = "JNguyen",
        ["p-joujou"] = "JouJou",
        ["p-justin-brown"] = "JB",
        ["p-keila-vanessa"] = "Keila",
        ["p-kenny-duong"] = "Kenny",
        ["p-kyle-mccullough"] = "Kyle",
        ["p-kylie-jacelynn"] = "Kylie",
        ["p-maria-riri"] = "Riri",
        ["p-maria-saba"] = "Saba",
        ["p-marilyn-elizondo"] = "Marilyn",
        ["p-may"] = "May",
        ["p-michael-lor"] = "Michael",
        ["p-sage-hermes"] = "Sage",
        ["p-zach-montebon"] = "Zach",
    };

    private static bool ToV8_ShortNames(TripData trip)
    {
        var changed = false;

        foreach (var person in trip.Roster)
        {
            if (!ShortNames.TryGetValue(person.Id, out var name) || person.Name == name) continue;

            // A team records its lead by name, so that reference has to move in step or the
            // team loses track of who leads it and the badge disappears.
            foreach (var team in trip.Teams.Where(t => string.Equals(t.Lead, person.Name, StringComparison.OrdinalIgnoreCase)))
                team.Lead = name;

            person.Name = name;
            changed = true;
        }

        var jou = trip.Teams.FirstOrDefault(t => t.Id == "jou");
        if (jou is not null && jou.Name == "Team Jou")
        {
            jou.Name = "Team JouJou";
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// The Jeopardy board was reshaped: categories now own an ordered list of clues rather than
    /// the board holding a flat list keyed by category and dollar value, values run 5-25 in trip
    /// points instead of 400-2000 in dollars, and there is live game state alongside the content.
    ///
    /// The old shape cannot be salvaged into the new one — the clue text itself was rewritten in
    /// the host's editor — so this takes the board from the seed wholesale. Safe because nothing
    /// on the deployed copy has ever been edited through the app: there was no Jeopardy page.
    /// </summary>
    private static bool ToV9_JeopardyBoard(TripData trip)
    {
        if (trip.Jeopardy.Categories.Count > 0 && trip.Jeopardy.Categories[0].Clues.Count > 0)
            return false;

        trip.Jeopardy = SeedLoader.Load().Jeopardy;
        return true;
    }

    /// <summary>
    /// Final Jeopardy used to be its own thing: every team wrote an answer, the host revealed
    /// them together and marked each one. It is now played exactly like any other clue — buzz
    /// in, answer aloud, wrong costs you the points — so <c>Final</c> means something different
    /// than it did. It used to mean "everyone is writing"; it now means "the clue is up and the
    /// buzzers are live", which is a state that needs a clue id to go with it.
    ///
    /// A game saved mid-old-final would therefore come back as a final with nothing in play and
    /// render an empty screen. Sending it to the titles instead costs whoever is playing one
    /// button press and loses nothing, since the old written answers have nowhere to go anyway.
    ///
    /// The abandoned fields — finalAnswers, finalCorrectTeamIds, finalRevealed — need no work
    /// here. They are simply no longer on the model, and the reader ignores what it cannot map.
    /// </summary>
    private static bool ToV10_FinalIsAClue(TripData trip)
    {
        var game = trip.Jeopardy.Game;
        if (game.Phase != JeopardyPhase.Final || game.CurrentClueId is not null) return false;

        game.Phase = JeopardyPhase.FinalIntro;
        game.BuzzersOpen = false;
        game.Buzzes.Clear();
        game.LockedOutTeamIds.Clear();
        return true;
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

    /// <summary>
    /// The printed guest itinerary — essentials, menu, packing list, what each car brings —
    /// moved onto the site.
    ///
    /// Taken from the seed wholesale, the same way the board was at v9, so the content has one
    /// home rather than being written out twice and drifting. Only ever fills an empty guide, so
    /// a trip that has already had one edited is left alone.
    /// </summary>
    private static bool ToV11_GuestGuide(TripData trip)
    {
        if (trip.Guide.Essentials.Count > 0 || trip.Guide.Packing.Count > 0) return false;

        trip.Guide = SeedLoader.Load().Guide;
        return true;
    }

    /// <summary>
    /// The travel sheet — who leaves from where, when, and in whose car — plus the check-in time
    /// it settles.
    ///
    /// Check-in is 2:00 PM. Again.
    ///
    /// v5 set it to 2:00 PM off the travel sheet, which is the booking. v11 moved it to 4:00 PM
    /// on the strength of the guest handbook, which says the house is ours from 4:00 — but that
    /// is when guests are being told to turn up, not when the property opens. The booking wins,
    /// and this puts every copy of it back: the venue field, the essentials row, and the item on
    /// the Friday schedule.
    ///
    /// Every change is guarded on finding the value v11 wrote, so a time chosen deliberately
    /// since then is left alone.
    /// </summary>
    private static bool ToV12_CheckInIsTwo(TripData trip)
    {
        var changed = false;

        // Rows are keyed to the roster, so an empty plan is filled from the seed and one that
        // has been worked on is left completely alone.
        if (trip.Travel.Rows.Count == 0 && string.IsNullOrWhiteSpace(trip.Travel.Destination))
        {
            trip.Travel = SeedLoader.Load().Travel;
            changed = true;
        }

        if (trip.Venue.CheckIn is "Friday 4:00 PM")
        {
            trip.Venue.CheckIn = "Friday 2:00 PM";
            changed = true;
        }

        var row = trip.Guide.Essentials.FirstOrDefault(f => f.Label == "Check-in");
        if (row is not null && row.Value.Contains("4:00 PM", StringComparison.OrdinalIgnoreCase))
        {
            row.Value = "Friday, August 28. The house is ours from 2:00 PM.";
            changed = true;
        }

        // By id, not by title. Matching on "Check-in" would reach into any trip that happens to
        // have an item so named — this is about one specific item on one specific Friday.
        // 960 minutes is 4pm; 840 is 2pm.
        var arrival = trip.Itinerary.SelectMany(d => d.Items).FirstOrDefault(i => i.Id == "item-f1");
        if (arrival is not null && arrival.StartMinutesOrNull == 960)
        {
            arrival.StartMinutes = 840;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// The menu grew up: from a flat when/what list into a board — one card per day with
    /// breakfast, lunch and dinner slots the committee can edit and drag between, plus the
    /// all-weekend staples as their own cards.
    ///
    /// Filled from the seed when empty, like every other guide section. The old flat list is not
    /// converted because parsing "Saturday lunch" back out of a label is exactly the kind of
    /// cleverness that breaks the first time somebody writes "Sat lunch" — and the flat list has
    /// only existed since v11, unedited, matching the seed it came from.
    /// </summary>
    private static bool ToV13_MenuBoard(TripData trip)
    {
        if (trip.Guide.MenuDays.Count > 0 || trip.Guide.Staples.Count > 0) return false;

        var seed = SeedLoader.Load().Guide;
        trip.Guide.MenuDays = seed.MenuDays;
        trip.Guide.Staples = seed.Staples;
        return true;
    }

    /// <summary>
    /// The essentials grow headings: Logistics, Financial, and the house. A flat list of seven
    /// unrelated rows was hard to scan, and grouping them is the whole point of the redesign.
    ///
    /// The two money rows are also renamed to say what they are — "Covered" alone reads as a
    /// state rather than a question — and Payment joins them, blank, because only the committee
    /// knows what belongs there.
    ///
    /// Bails out the moment any fact already carries a group, so a set of headings somebody has
    /// since rearranged is never re-sorted underneath them.
    /// </summary>
    private static bool ToV14_GroupedEssentials(TripData trip)
    {
        var facts = trip.Guide.Essentials;
        if (facts.Count == 0) return false;
        if (facts.Any(f => !string.IsNullOrWhiteSpace(f.Group))) return false;

        foreach (var fact in facts)
        {
            fact.Group = fact.Label switch
            {
                "Where" or "Check-in" or "Check-out" => "Logistics",
                "Covered" or "Not covered" => "Financial",
                "Bedrooms" or "Bathrooms" => "The house",
                // Anything added by hand since v11 goes under Logistics rather than into a
                // heading of its own, which would look like the migration invented a section.
                _ => "Logistics"
            };
        }

        Rename("Covered", "What's Covered");
        Rename("Not covered", "What's Not Covered");

        if (!facts.Any(f => f.Label.Equals("Payment", StringComparison.OrdinalIgnoreCase)))
        {
            var last = facts.FindLastIndex(f => f.Group == "Financial");
            var payment = new GuideFact { Group = "Financial", Label = "Payment", Value = "" };

            if (last >= 0) facts.Insert(last + 1, payment);
            else facts.Add(payment);
        }

        return true;

        void Rename(string from, string to)
        {
            if (facts.FirstOrDefault(f => f.Label == from) is { } fact) fact.Label = to;
        }
    }
}
