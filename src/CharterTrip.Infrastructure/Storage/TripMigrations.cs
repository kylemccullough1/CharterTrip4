using CharterTrip.Core.Models;
using CharterTrip.Core.Services;
using CharterTrip.Core.Words;
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
    public const int CurrentVersion = 31;

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
        if (trip.SchemaVersion < 20) changed |= ToV20_BeeDealsItsOwnWords(trip);
        if (trip.SchemaVersion < 21) changed |= ToV21_BeeDrawsAtADifficulty(trip);
        if (trip.SchemaVersion < 22) changed |= ToV22_PartyGames(trip);
        if (trip.SchemaVersion < 23) changed |= ToV23_SketchCharactersHavePictures(trip);
        if (trip.SchemaVersion < 24) changed |= ToV24_FourBeersTakesTheRound(trip);
        if (trip.SchemaVersion < 25) changed |= ToV25_TheStackIsWorkedOut(trip);
        if (trip.SchemaVersion < 26) changed |= ToV26_NoNewlywedGame(trip);
        if (trip.SchemaVersion < 27) changed |= ToV27_EverybodyHasAJoinToken(trip);
        if (trip.SchemaVersion < 28) changed |= ToV28_BraunManorReplacesWestEgg(trip);
        if (trip.SchemaVersion < 29) changed |= ToV29_StoryMode(trip);
        if (trip.SchemaVersion < 30) changed |= ToV30_OneJeopardyDoor(trip);
        if (trip.SchemaVersion < 31) changed |= ToV31_OneDoorPerGame(trip);

        // v19 carried the seed's hand-written word list across to a file that predated the
        // bee. No step any more: v20 deals the list instead of shipping one, so there is
        // nothing in the seed left for that step to copy and it would run to no effect.

        // The four steps from v22 arrived on a branch of their own, where they were numbered
        // from 19 and the bee did not exist. They were renumbered onto the end of the ladder
        // rather than left where they were, because a file the bee had already stamped 21 would
        // have skipped every one of them and come back with no party games in it. Each guards on
        // what it finds rather than on the number it carries, so moving them costs nothing.

        // The last three arrived the same way and were renumbered for the same reason. The
        // mystery was built on a branch of its own, where it numbered its steps 19, 20 and 21 —
        // the same three numbers the bee and its difficulty dial were taking here, meaning
        // different things. Two ladders cannot both be right, so the one the deployed file has
        // already climbed wins: this file is stamped 26 out there, and a step that renumbered
        // itself under it would be skipped by every copy that matters. The mystery's three go on
        // the end instead, and none of them cares what number it is called by.

        // v18 gave a carpool's ETA a day to go with the time. No step, same as the two
        // before it: the field defaults to empty and an older file simply has not said.

        // v17 gave a travel row what its owner is bringing. No step: the field defaults to
        // empty and there was nothing to convert. It is its own version rather than being
        // folded into v16 because the stamp records the shape of a file, and one number
        // standing for two different shapes is the confusion the stamp exists to prevent.

        // v16 gave a carpool facts of its own — a name, a departure time, an ETA. No step
        // either: the cars list simply defaults to empty, and there was nothing in an older
        // file to convert, because until now nobody could write any of it down.

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
    /// The bee stopped keeping a hand-written word list and started dealing one out of the
    /// embedded Scripps bank. Three things change shape at once, and all of them for the same
    /// reason: the list is no longer something a person authored.
    ///
    /// The words go. A v19 file holds forty-five words with hints, written into the seed by hand;
    /// the new <c>BeeWord</c> has no hint to put them in and carries a tier key it cannot invent,
    /// so keeping them would produce a deck that renders as blanks in the host's difficulty
    /// readout. They are cleared instead, and the next Start deals two hundred real ones. Nobody
    /// loses authored work — the bee has never been played, and the seed is where those forty-five
    /// came from anyway.
    ///
    /// The deck settings arrive with their defaults, and scoring moves from one award at the end
    /// to five points a word as it happens, so any half-played v19 game is sent back to the title
    /// card: its Survivors/TeamCursor rotation has no counterpart in a fixed shuffled row, and
    /// resuming it would mean inventing an order nobody stood in.
    /// </summary>
    private static bool ToV20_BeeDealsItsOwnWords(TripData trip)
    {
        var bee = trip.SpellingBee;
        var seedTrip = SeedLoader.Load();
        var seed = seedTrip.SpellingBee;

        bee.Words.Clear();
        bee.Game = new BeeGame();

        // The deck settings this step used to install — tier weights and a deck target — are not
        // on the model any more; v21 replaced them with one difficulty dial and sets it itself.
        if (bee.PointsPerWord <= 0) bee.PointsPerWord = seed.PointsPerWord;

        // The old game awarded once, at the end. Those entries are for a rule that no longer
        // exists, so they leave with it rather than sitting in the standings unexplained.
        trip.Scores.RemoveAll(s => s.GameId == SpellingBeeService.GameId);

        // Rules text describes the game, and the game changed. Taken wholesale because these
        // are the committee's words in the seed, not something edited through the app.
        if (trip.Games.FirstOrDefault(g => g.Id == SpellingBeeService.GameId) is { } game &&
            seedTrip.Games.FirstOrDefault(g => g.Id == SpellingBeeService.GameId) is { } fresh)
        {
            game.Rules = fresh.Rules;
            game.Blurb = fresh.Blurb;
        }

        return true;
    }

    /// <summary>
    /// The bee stopped dealing a deck up front and started drawing a word per turn at a
    /// difficulty the host moves while the room plays.
    ///
    /// Three fields go with the deck: the tier weights and the deck target, which described a
    /// hand nobody deals any more, and the word cursor, which pointed into it. What replaces
    /// them is one tier key — where the dial starts — and a matching one on the game for where
    /// the host has moved it to.
    ///
    /// A v20 <c>Words</c> list is a dealt deck: two hundred words nobody has read yet. Under the
    /// new model that list means the opposite — every word already spent — so keeping it would
    /// retire two hundred words on the first turn. It goes, and any bee mid-flight goes back to
    /// the title card with it, which costs the room one tap on Start.
    /// </summary>
    private static bool ToV21_BeeDrawsAtADifficulty(TripData trip)
    {
        var bee = trip.SpellingBee;

        bee.Words.Clear();
        bee.Game = new BeeGame();

        if (!WordBank.IsTier(bee.DifficultyKey)) bee.DifficultyKey = WordDeck.DefaultDifficulty;
        bee.Game.DifficultyKey = bee.DifficultyKey;

        // Banked word by word, so what is in the standings is a tally of a game that was played
        // under rules that have not changed. It stays.
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

    /// <summary>
    /// The four games played on their feet got screens of their own, which means point values,
    /// round counts and Police Sketch's character list now have to live somewhere rather than
    /// being written on the back of the budget sheet.
    ///
    /// Taken from the seed, like the board at v9 and the guide at v11, so the numbers have one
    /// home. Only ever fills a set that has never been played or edited — a point value someone
    /// has since changed is left alone.
    /// </summary>
    private static bool ToV22_PartyGames(TripData trip)
    {
        var party = trip.Party;
        var untouched =
            party.Sketch.PointValue == 0 &&
            party.NoodleCup.PointValue == 0 && party.BeerRun.PointValue == 0;

        if (!untouched) return false;

        trip.Party = SeedLoader.Load().Party;
        return true;
    }

    /// <summary>
    /// A Police Sketch character used to be a name and nothing else. It is now a name and a
    /// picture of them, so whoever is describing one has something to describe.
    ///
    /// The list moved from <c>prompts</c> to <c>characters</c> rather than changing shape under
    /// the same key, which is what makes this safe to load: a v22 file's array of bare strings is
    /// simply a key the model no longer has, ignored on read and gone on the next write, instead
    /// of a type mismatch that would fail the whole document before any migration could run.
    ///
    /// Refilled from the seed, like the board at v9 — nobody has had a chance to edit this list
    /// yet, and the names in it are the ones off the budget sheet either way.
    /// </summary>
    private static bool ToV23_SketchCharactersHavePictures(TripData trip)
    {
        if (trip.Party.Sketch.Characters.Count > 0) return false;

        trip.Party.Sketch.Characters = SeedLoader.Load().Party.Sketch.Characters;
        trip.Party.Sketch.UsedCharacters.Clear();
        trip.Party.Sketch.CurrentCharacter = null;
        return true;
    }

    /// <summary>
    /// The beer run is a race to a number rather than a share-out: four beers back to your corner
    /// takes the round, and no corner can be credited with more than that because the run stops
    /// the moment somebody gets there. Before this there was no such number, so the only way a
    /// round could end was by running the stack out — the cups' rule, which was never this one's.
    ///
    /// The cups still get none: nothing wins their round early, it simply runs out.
    /// </summary>
    private static bool ToV24_FourBeersTakesTheRound(TripData trip)
    {
        if (trip.Party.BeerRun.TakeToWin is not null) return false;

        trip.Party.BeerRun.TakeToWin = SeedLoader.Load().Party.BeerRun.TakeToWin;
        return true;
    }

    /// <summary>
    /// How many beers are in a round was never a free choice, and the shape before this one
    /// offered it as one.
    ///
    /// To be sure a corner reaches four, the stack has to be more than every corner can hold on
    /// three — at least 3 x teams + 1. For only one corner to reach four, every other corner has
    /// to be able to stop on three — at most 4 + 3 x (teams - 1). Those are the same number:
    /// thirteen, across four corners, and nothing else. So it is worked out rather than stored.
    ///
    /// A host who had set anything smaller had a round that could dead-end. Seven beers going
    /// three, three and one leaves nobody on four and nothing left to give them.
    ///
    /// Nothing to convert — the property is gone from the model, so the key is ignored on read.
    /// What this earns is the version bump, which flushes it out of the file rather than leaving
    /// it there until somebody happens to make an edit. Same as v4.
    /// </summary>
    private static bool ToV25_TheStackIsWorkedOut(TripData trip) => true;

    /// <summary>
    /// Nobody is playing the Newlywed Game. The seed dropped it when the weekend was cut to six
    /// games, but the seed only ever reaches a file that has none of its own — so a copy saved
    /// before that still lists it on the games page, behind a card that opens on a game with no
    /// screen of its own, and still holds three quarters of an hour for it on Friday night.
    ///
    /// The card goes by id, which is what the menu and the score log key on, rather than by the
    /// name, which a host is free to have retitled. The Friday slot goes by its title instead:
    /// it is an ordinary itinerary item that has been dragged and renumbered before now, so the
    /// id it happens to carry is the weaker of the two identities.
    ///
    /// Points already awarded to it are left where they are. The log is append-only and a team's
    /// total is the sum of its entries, so deleting them would move the standings — taking a game
    /// off the page is not the same as taking back what a team won at it, and in practice there
    /// is nothing here to take: the game never had a scoring screen to award from.
    /// </summary>
    private static bool ToV26_NoNewlywedGame(TripData trip)
    {
        var changed = trip.Games.RemoveAll(g => g.Id == "newlywed") > 0;

        foreach (var day in trip.Itinerary)
        {
            if (day.Items.RemoveAll(i =>
                    i.Title.Contains("Newlywed", StringComparison.OrdinalIgnoreCase)) > 0)
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

    /// <summary>
    /// v27 gives everybody on the roster their own join token.
    ///
    /// <c>RosterPerson.JoinToken</c> has existed unused since phase 2 was first sketched. It is
    /// what turns "authenticated" from a synonym for "is an admin" into an actual identity: the
    /// murder mystery needs twenty-one people to be twenty-one different people on twenty-one
    /// phones, and a shared committee password cannot express that.
    ///
    /// A numbered step rather than minting on demand, because these end up printed on name tags.
    /// Something that gets printed should be decided once, written down, and then stable —
    /// generating a token the first time somebody happens to load a page means the badge in your
    /// hand and the token in the file can disagree.
    ///
    /// Idempotent, and existing tokens are never reissued.
    /// </summary>
    private static bool ToV27_EverybodyHasAJoinToken(TripData trip) => JoinCodes.EnsureTokens(trip);

    /// <summary>
    /// v28 replaces Murder at West Egg Manor with Murder at Braun Manor.
    ///
    /// The old game was 26 characters, a mastermind and five conspirators, with clue cards the host
    /// typed in and released by round. Braun Manor shares none of that structure: 21 characters, six
    /// factions, three killers drawn per guilt slot, and every word of it composed from embedded
    /// content. So <c>MysteryState</c> was replaced rather than extended, and the old properties
    /// simply stop existing — <see cref="TripMigrations"/> already establishes that a removed
    /// property is ignored on load and gone on the next save.
    ///
    /// This is nonetheless a numbered step rather than a silent model change, for two reasons. The
    /// stamp records the shape of a file, and a document half-way between two different games is
    /// unrecoverable on the one screen — the reveal — where being wrong is worst. And the titles
    /// need rewriting, which no amount of default-value handling would do on its own.
    ///
    /// Guarded on finding the old text, so a title someone has since chosen deliberately survives.
    /// </summary>
    private static bool ToV28_BraunManorReplacesWestEgg(TripData trip)
    {
        const string old = "Murder at West Egg Manor";
        const string now = "Murder at Braun Manor";

        // The mystery reset this step used to do now belongs to the step below, which replaced
        // the model again. Resetting here as well would be a second implementation of the same
        // decision, and it can no longer be expressed in terms of the current shape anyway.
        var changed = false;

        // The name appears in three places, all of them read by guests before the night.
        foreach (var slide in trip.Slides.Where(s => s.Caption == old))
        {
            slide.Caption = now;
            changed = true;
        }

        foreach (var item in trip.Itinerary
                     .SelectMany(d => d.Items)
                     .Where(i => i.Title.Contains(old, StringComparison.Ordinal)))
        {
            item.Title = item.Title.Replace(old, now, StringComparison.Ordinal);
            changed = true;
        }

        foreach (var game in trip.Games.Where(g => g.Name.Contains(old, StringComparison.Ordinal)))
        {
            game.Name = game.Name.Replace(old, now, StringComparison.Ordinal);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// v29 rebuilds the murder mystery as one written story rather than a generator.
    ///
    /// The generated game dealt a different evening every time: a seeded dealer placed everybody, drew three
    /// killers by guilt slot, picked red herrings and laid out clues, and a compiler wrote every
    /// sentence from templates plus a guilty-or-innocent reading per character. This one is played
    /// once, so all of that has gone and the story is written by hand instead — which also means it
    /// can live here, in the trip, and be edited on the site like everything else.
    ///
    /// Nothing carries over. <c>CurrentRoundIndex</c> has no phase, the deal has no story, and the
    /// prose the old model referenced by id was never in the document to begin with. So the section
    /// is reset and reseeded from the embedded content on first use.
    ///
    /// The load-order half of this lives in <c>LegacyJsonShapes.DropPreV21Mystery</c>, which has to
    /// run first: an enum name from the generated game that no longer exists would quarantine the
    /// whole trip file before this method ever sees a <see cref="TripData"/>. That name keeps the
    /// number the mystery branch gave it, because it describes the shape of the file it reads —
    /// the pre-story mystery — rather than a rung on this ladder.
    /// </summary>
    /// <summary>
    /// Jeopardy stopped handing out a code per team and started handing out one code to the room.
    ///
    /// A code used to <em>be</em> a team: whoever typed Team Ali's four characters was Team Ali's
    /// buzzer. Now it is only a door, and which buzzer you get is decided by the name you tap on
    /// the far side of it — read off your own roster row, so there is no way to ask for a team
    /// that is not yours.
    ///
    /// <c>BuzzerCodes</c> is simply gone from the model, so the reader drops it on load and the
    /// next save writes a file without it. All this has to do is make sure there is a door to
    /// knock on, which matters for a file loaded by something that never opens the board.
    /// </summary>
    private static bool ToV30_OneJeopardyDoor(TripData trip)
    {
        // Deliberately not a reset. The scores are a tally of a game played under rules that have
        // not changed, and JoinedTeamIds still means what it always meant.
        return JeopardyService.EnsureCodes(trip, Random.Shared);
    }

    /// <summary>
    /// v30 took Jeopardy from a code per team down to one code and a host's. This takes all three
    /// games down to one code flat.
    ///
    /// What the second code bought was that a stray phone could not reach the answer sheet, the
    /// word list, or the guilty list — and it bought it with four characters that had to be kept
    /// off the wall they were printed for. That job now belongs to the committee's password: the
    /// host option is offered on the far side of the one door, to a browser already signed in as
    /// an organizer. A secret worth keeping is worth keeping behind a password.
    ///
    /// <c>HostCode</c> is simply gone from all three models, so the reader drops it on load and the
    /// next save writes a file without it. What this has to do is make sure each game still has a
    /// door to knock on, which matters for a file loaded by something that never opens a wall.
    ///
    /// Not a reset, for the same reason v30 was not: the scores, the joins and the cast are a
    /// record of a night played under rules that have not changed.
    /// </summary>
    private static bool ToV31_OneDoorPerGame(TripData trip)
    {
        var changed = JeopardyService.EnsureCodes(trip, Random.Shared);
        changed |= SpellingBeeService.EnsureCodes(trip, Random.Shared);

        // The mystery's door is only opened when somebody creates the evening — an empty party
        // code means no game, and OpenDoors would deal a cast for one nobody asked for.
        return changed;
    }

    private static bool ToV29_StoryMode(TripData trip)
    {
        var changed = trip.Mystery.Phase != MysteryPhase.Lobby
                      || trip.Mystery.Play.Cast.Count > 0
                      || trip.Mystery.Story.Characters.Count > 0;

        trip.Mystery = new MysteryState();
        return changed;
    }
}
