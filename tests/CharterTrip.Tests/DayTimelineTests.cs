using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

public class DayTimelineTests
{
    private static readonly TimelineSettings Settings = new()
    {
        PxPerHour = 64,
        CollapseThresholdMinutes = 180,
        CollapsedBandPixels = 38,
        MinTrackPixels = 180
    };

    private static ItineraryDay Day(params (int Start, int Duration)[] items) => new()
    {
        Id = "d",
        Items = items
            .Select((x, n) => new ItineraryItem
            {
                Id = $"i{n}",
                StartMinutes = x.Start,
                DurationMinutes = x.Duration,
                Title = $"Item {n}"
            })
            .ToList()
    };

    // ---------------------------------------------------------------- window

    [Fact]
    public void Window_floors_the_start_to_an_hour_but_ends_on_the_last_event()
    {
        // Long enough that the four-hour minimum window does not come into it.
        var timeline = DayTimeline.Build(Day((10 * 60 + 30, 60), (15 * 60, 45)), Settings);

        Assert.Equal(10 * 60, timeline.StartMinutes);           // floored, so the label lines up
        Assert.Equal(15 * 60 + 45, timeline.EndMinutes);        // exactly where the day finishes
    }

    [Fact]
    public void No_empty_track_is_left_under_the_last_card()
    {
        // Whatever the day looks like, the grid stops where the last thing stops — otherwise the
        // blank space beneath the final card changes from day to day.
        foreach (var day in new[]
                 {
                     Day((10 * 60, 60)),                                  // ends on the hour
                     Day((10 * 60, 45)),                                  // ends mid-hour
                     Day((10 * 60, 60), (14 * 60, 20)),                   // ends on an odd minute
                     Day((23 * 60, 105))                                  // runs past midnight
                 })
        {
            var timeline = DayTimeline.Build(day, Settings);
            var last = day.Items.Max(i => i.EndMinutes);

            // Either the day ends at the last event, or the four-hour minimum stretched it.
            var stretched = timeline.TotalPixels >= Settings.MinTrackPixels
                            && timeline.EndMinutes > last;
            Assert.True(timeline.EndMinutes == last || stretched,
                $"window ended at {timeline.EndMinutes}, last event at {last}");
        }
    }

    [Fact]
    public void A_very_short_day_is_stretched_to_stay_legible()
    {
        var timeline = DayTimeline.Build(Day((12 * 60, 30)), Settings);

        Assert.True(timeline.TotalPixels >= Settings.MinTrackPixels);
    }

    [Fact]
    public void A_day_that_is_already_tall_enough_is_not_padded()
    {
        // Three and a half hours clears the floor comfortably, so the grid should stop dead
        // on the last event rather than gaining blank track.
        var day = Day((9 * 60, 60), (10 * 60, 60), (11 * 60, 30), (11 * 60 + 30, 30), (12 * 60, 30));
        var timeline = DayTimeline.Build(day, Settings);

        Assert.Equal(12 * 60 + 30, timeline.EndMinutes);
    }

    [Fact]
    public void An_empty_day_still_gets_a_sensible_window()
    {
        var timeline = DayTimeline.Build(Day(), Settings);

        Assert.True(timeline.EndMinutes > timeline.StartMinutes);
        Assert.True(timeline.TotalPixels > 0);
    }

    [Fact]
    public void An_explicit_window_on_the_day_overrides_the_fit()
    {
        var day = Day((12 * 60, 60));
        day.WindowStartMinutes = 8 * 60;
        day.WindowEndMinutes = 22 * 60;

        var timeline = DayTimeline.Build(day, Settings);

        Assert.Equal(8 * 60, timeline.StartMinutes);
        Assert.Equal(22 * 60, timeline.EndMinutes);
    }

    // -------------------------------------------------------------- collapse

    [Fact]
    public void A_long_empty_stretch_collapses()
    {
        // 10-11am, then nothing until 4pm: a five-hour hole.
        var timeline = DayTimeline.Build(Day((10 * 60, 60), (16 * 60, 60)), Settings);

        var collapsed = timeline.Bands.Where(b => b.Kind == BandKind.Collapsed).ToList();
        var band = Assert.Single(collapsed);
        Assert.Equal(11 * 60, band.StartMinutes);
        Assert.Equal(16 * 60, band.EndMinutes);
        Assert.Equal(Settings.CollapsedBandPixels, band.HeightPixels);
    }

    [Fact]
    public void A_short_gap_stays_at_full_scale()
    {
        // Two hours is under the three-hour threshold, so it renders as real empty space.
        var timeline = DayTimeline.Build(Day((10 * 60, 60), (13 * 60, 60)), Settings);

        Assert.All(timeline.Bands, b => Assert.Equal(BandKind.Expanded, b.Kind));
        Assert.Equal(4 * 64, timeline.TotalPixels);       // 10:00-14:00 at full scale
    }

    [Fact]
    public void Expanding_a_gap_restores_it_to_full_scale()
    {
        var day = Day((10 * 60, 60), (16 * 60, 60));

        var collapsed = DayTimeline.Build(day, Settings);
        var key = collapsed.Bands.Single(b => b.Kind == BandKind.Collapsed).Key;

        var expanded = DayTimeline.Build(day, Settings, new HashSet<string> { key });

        Assert.All(expanded.Bands, b => Assert.Equal(BandKind.Expanded, b.Kind));
        Assert.True(expanded.TotalPixels > collapsed.TotalPixels);
        Assert.Equal(7 * 64, expanded.TotalPixels);       // 10:00-17:00
    }

    [Fact]
    public void Overlapping_items_do_not_create_a_phantom_gap()
    {
        // Two things at once, then a long hole. Only one collapsed band should appear.
        var timeline = DayTimeline.Build(Day((10 * 60, 120), (10 * 60 + 30, 60), (18 * 60, 60)), Settings);

        Assert.Single(timeline.Bands, b => b.Kind == BandKind.Collapsed);
    }

    // ------------------------------------------------------------ conversion

    [Fact]
    public void Pixels_and_minutes_round_trip_inside_expanded_bands()
    {
        var timeline = DayTimeline.Build(Day((10 * 60, 60), (13 * 60, 60)), Settings);

        for (var m = timeline.StartMinutes; m <= timeline.EndMinutes; m += 15)
            Assert.Equal(m, timeline.ToMinutes(timeline.ToPixels(m)));
    }

    [Fact]
    public void Conversion_is_monotonic_even_across_a_collapsed_band()
    {
        var timeline = DayTimeline.Build(Day((10 * 60, 60), (16 * 60, 60)), Settings);

        var previous = -1d;
        for (var m = timeline.StartMinutes; m <= timeline.EndMinutes; m += 5)
        {
            var y = timeline.ToPixels(m);
            Assert.True(y >= previous, $"pixels went backwards at {m}");
            previous = y;
        }
    }

    [Fact]
    public void Times_outside_the_window_clamp_to_its_edges()
    {
        var timeline = DayTimeline.Build(Day((10 * 60, 60), (13 * 60, 60)), Settings);

        Assert.Equal(0, timeline.ToPixels(3 * 60));
        Assert.Equal(timeline.TotalPixels, timeline.ToPixels(23 * 60));
    }

    [Fact]
    public void HeightOf_sizes_a_card_by_its_duration()
    {
        var timeline = DayTimeline.Build(Day((10 * 60, 90)), Settings);

        Assert.Equal(1.5 * 64, timeline.HeightOf(10 * 60, 90));
    }

    [Fact]
    public void HourMarks_skip_hours_hidden_inside_a_collapsed_band()
    {
        var timeline = DayTimeline.Build(Day((10 * 60, 60), (16 * 60, 60)), Settings);
        var marks = timeline.HourMarks().ToList();

        Assert.Equal([10 * 60, 16 * 60], marks);

        // 11:00 is where the collapsed band begins — it draws its own "5 hours free" label,
        // so no hour tick belongs there, and 13:00 is buried inside it entirely.
        Assert.DoesNotContain(11 * 60, marks);
        Assert.DoesNotContain(13 * 60, marks);
    }

    [Fact]
    public void Midnight_and_beyond_are_handled_as_the_tail_of_the_night()
    {
        // 11pm to 1am: minutes 1380 -> 1500, i.e. past the 1440 boundary.
        var timeline = DayTimeline.Build(Day((23 * 60, 120)), Settings);

        Assert.Equal(23 * 60, timeline.StartMinutes);

        // Two hours is under the legibility floor, so the window is stretched past the event.
        Assert.True(timeline.EndMinutes > 25 * 60);
        Assert.True(timeline.TotalPixels >= Settings.MinTrackPixels);
    }
}
