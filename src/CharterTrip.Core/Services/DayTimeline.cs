using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

public enum BandKind { Expanded, Collapsed }

/// <summary>A stretch of the day, either drawn to scale or squashed into a thin strip.</summary>
public sealed record TimelineBand(
    BandKind Kind,
    int StartMinutes,
    int EndMinutes,
    double TopPixels,
    double HeightPixels)
{
    public int DurationMinutes => EndMinutes - StartMinutes;

    /// <summary>Stable identity for "the user expanded this gap", survives re-renders.</summary>
    public string Key => $"{StartMinutes}-{EndMinutes}";
}

public sealed record TimelineSettings
{
    public int PxPerHour { get; init; } = 64;

    /// <summary>Empty stretches at least this long collapse by default.</summary>
    public int CollapseThresholdMinutes { get; init; } = 180;

    public double CollapsedBandPixels { get; init; } = 38;

    /// <summary>Never render a day shorter than this, or a one-item day looks broken.</summary>
    public int MinWindowMinutes { get; init; } = 4 * 60;

    public static readonly TimelineSettings Default = new();
}

/// <summary>
/// Maps clock time to vertical pixels for one day of the planner.
///
/// This is not a straight multiplication, because long empty stretches collapse into thin bands.
/// Position therefore depends on the whole day's layout, which is exactly why it lives in one
/// tested class instead of being scattered through the Razor markup — and why the drag code sends
/// pixels back here to be converted rather than doing the arithmetic in JavaScript.
/// </summary>
public sealed class DayTimeline
{
    private DayTimeline(int startMinutes, int endMinutes, IReadOnlyList<TimelineBand> bands, TimelineSettings settings)
    {
        StartMinutes = startMinutes;
        EndMinutes = endMinutes;
        Bands = bands;
        Settings = settings;
        TotalPixels = bands.Count == 0 ? 0 : bands[^1].TopPixels + bands[^1].HeightPixels;
    }

    public int StartMinutes { get; }
    public int EndMinutes { get; }
    public IReadOnlyList<TimelineBand> Bands { get; }
    public TimelineSettings Settings { get; }
    public double TotalPixels { get; }

    /// <summary>Fallback window for a day with nothing scheduled: 8am to 10pm.</summary>
    private const int DefaultWindowStart = 8 * 60;
    private const int DefaultWindowEnd = 22 * 60;

    public static DayTimeline Build(
        ItineraryDay day,
        TimelineSettings? settings = null,
        IReadOnlySet<string>? expandedGaps = null)
    {
        settings ??= TimelineSettings.Default;
        expandedGaps ??= new HashSet<string>();

        var scheduled = day.Items
            .Where(i => i.IsScheduled)
            .OrderBy(i => i.StartMinutes!.Value)
            .ToList();

        var (windowStart, windowEnd) = Window(day, scheduled, settings);
        var bands = BuildBands(scheduled, windowStart, windowEnd, settings, expandedGaps);

        return new DayTimeline(windowStart, windowEnd, bands, settings);
    }

    private static (int Start, int End) Window(ItineraryDay day, List<ItineraryItem> scheduled, TimelineSettings settings)
    {
        int start, end;

        if (scheduled.Count == 0)
        {
            start = day.WindowStartMinutes ?? DefaultWindowStart;
            end = day.WindowEndMinutes ?? DefaultWindowEnd;
        }
        else
        {
            start = day.WindowStartMinutes ?? FloorToHour(scheduled.Min(i => i.StartMinutes!.Value));
            end = day.WindowEndMinutes ?? CeilingToHour(scheduled.Max(i => i.EndMinutes));
        }

        if (end - start < settings.MinWindowMinutes)
            end = start + settings.MinWindowMinutes;

        return (start, end);
    }

    /// <summary>
    /// Walk the window, splitting it into stretches that contain something and stretches that
    /// don't. Empty stretches past the threshold collapse, unless the user has opened them.
    /// </summary>
    private static List<TimelineBand> BuildBands(
        List<ItineraryItem> scheduled,
        int windowStart,
        int windowEnd,
        TimelineSettings settings,
        IReadOnlySet<string> expandedGaps)
    {
        var busy = MergeBusyIntervals(scheduled, windowStart, windowEnd);

        // Turn the busy list into an alternating busy/free sequence covering the whole window.
        var segments = new List<(int Start, int End, bool Free)>();
        var cursor = windowStart;

        foreach (var (start, end) in busy)
        {
            if (start > cursor) segments.Add((cursor, start, true));
            segments.Add((start, end, false));
            cursor = end;
        }
        if (cursor < windowEnd) segments.Add((cursor, windowEnd, true));
        if (segments.Count == 0) segments.Add((windowStart, windowEnd, true));

        var bands = new List<TimelineBand>();
        var top = 0d;

        foreach (var (start, end, free) in segments)
        {
            var duration = end - start;
            var collapses = free
                && duration >= settings.CollapseThresholdMinutes
                && !expandedGaps.Contains($"{start}-{end}");

            var height = collapses
                ? settings.CollapsedBandPixels
                : duration * settings.PxPerHour / 60d;

            bands.Add(new TimelineBand(
                collapses ? BandKind.Collapsed : BandKind.Expanded,
                start, end, top, height));

            top += height;
        }

        return bands;
    }

    /// <summary>Union of the times something is happening, clipped to the window.</summary>
    private static List<(int Start, int End)> MergeBusyIntervals(List<ItineraryItem> scheduled, int windowStart, int windowEnd)
    {
        var merged = new List<(int Start, int End)>();

        foreach (var item in scheduled)
        {
            var start = Math.Max(windowStart, item.StartMinutes!.Value);
            var end = Math.Min(windowEnd, item.EndMinutes);
            if (end <= start) continue;

            if (merged.Count > 0 && start <= merged[^1].End)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, end));
            else
                merged.Add((start, end));
        }

        return merged;
    }

    // ------------------------------------------------------------ conversion

    public double ToPixels(int minutes)
    {
        if (Bands.Count == 0) return 0;

        var m = Math.Clamp(minutes, StartMinutes, EndMinutes);

        foreach (var band in Bands)
        {
            if (m < band.EndMinutes || band == Bands[^1])
            {
                var progress = band.DurationMinutes == 0
                    ? 0d
                    : (m - band.StartMinutes) / (double)band.DurationMinutes;
                return band.TopPixels + progress * band.HeightPixels;
            }
        }

        return TotalPixels;
    }

    public int ToMinutes(double pixels)
    {
        if (Bands.Count == 0) return StartMinutes;

        var y = Math.Clamp(pixels, 0, TotalPixels);

        foreach (var band in Bands)
        {
            if (y < band.TopPixels + band.HeightPixels || band == Bands[^1])
            {
                var progress = band.HeightPixels == 0
                    ? 0d
                    : (y - band.TopPixels) / band.HeightPixels;
                return (int)Math.Round(band.StartMinutes + progress * band.DurationMinutes);
            }
        }

        return EndMinutes;
    }

    /// <summary>How tall a duration is, measured from a given start. Used to size the cards.</summary>
    public double HeightOf(int startMinutes, int durationMinutes) =>
        Math.Max(0, ToPixels(startMinutes + durationMinutes) - ToPixels(startMinutes));

    /// <summary>Hour boundaries that fall inside expanded bands — the labels for the time gutter.</summary>
    public IEnumerable<int> HourMarks()
    {
        foreach (var band in Bands.Where(b => b.Kind == BandKind.Expanded))
        {
            var first = CeilingToHour(band.StartMinutes);
            for (var m = first; m < band.EndMinutes; m += 60)
                yield return m;
        }
    }

    private static int FloorToHour(int minutes) => minutes / 60 * 60;

    private static int CeilingToHour(int minutes) =>
        minutes % 60 == 0 ? minutes : (minutes / 60 + 1) * 60;
}
