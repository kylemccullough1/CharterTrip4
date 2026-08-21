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
        MinWindowMinutes = 240
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
    public void Window_fits_the_scheduled_items_rounded_out_to_whole_hours()
    {
        var timeline = DayTimeline.Build(Day((10 * 60 + 30, 60), (13 * 60, 45)), Settings);

        Assert.Equal(10 * 60, timeline.StartMinutes);       // floored
        Assert.Equal(14 * 60, timeline.EndMinutes);         // 13:45 ceilinged
    }

    [Fact]
    public void Window_never_collapses_below_the_minimum()
    {
        var timeline = DayTimeline.Build(Day((12 * 60, 30)), Settings);

        Assert.Equal(Settings.MinWindowMinutes, timeline.EndMinutes - timeline.StartMinutes);
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

        // The event itself only runs to 1am, but a two-hour day is stretched to the
        // four-hour minimum, so the window runs to 3am rather than stopping at 1am.
        Assert.Equal(27 * 60, timeline.EndMinutes);
        Assert.Equal(4 * 64, timeline.TotalPixels);
    }
}
