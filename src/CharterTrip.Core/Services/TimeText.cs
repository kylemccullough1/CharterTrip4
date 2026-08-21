using System.Globalization;
using System.Text.RegularExpressions;

namespace CharterTrip.Core.Services;

/// <summary>
/// Itinerary times are free text because real plans contain "TBD" and "after dinner".
/// This parses the ones that look like clock times so a day can be sorted, and sorts
/// everything else to the bottom rather than throwing.
/// </summary>
public static partial class TimeText
{
    /// <summary>Anything unparseable sorts last.</summary>
    public const int Unparseable = int.MaxValue;

    /// <summary>
    /// Minutes past 6am, so that a 12:00 AM nightcap sorts to the END of Saturday
    /// rather than the start of it. "4:00 PM" =&gt; 960, "12:00 AM" =&gt; 1440.
    /// </summary>
    public static int ToMinutes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Unparseable;

        var m = ClockPattern().Match(text);
        if (!m.Success) return Unparseable;

        if (!int.TryParse(m.Groups["h"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour))
            return Unparseable;
        if (hour is < 1 or > 12) return Unparseable;

        var minute = 0;
        if (m.Groups["m"].Success &&
            !int.TryParse(m.Groups["m"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out minute))
            return Unparseable;
        if (minute is < 0 or > 59) return Unparseable;

        hour %= 12;
        if (m.Groups["ap"].Value.StartsWith('p') || m.Groups["ap"].Value.StartsWith('P'))
            hour += 12;

        var minutes = hour * 60 + minute;

        // Before 6am belongs to the tail of the previous night, not the head of the morning.
        if (minutes < 6 * 60) minutes += 24 * 60;

        return minutes;
    }

    /// <summary>Minutes in a day. Values above this belong to the small hours of the next morning.</summary>
    public const int Day = 24 * 60;

    /// <summary>The planner's day starts at 6am; anything earlier is the tail of the night before.</summary>
    public const int DayAnchor = 6 * 60;

    /// <summary>"4:00 PM". Handles values past midnight, so 1500 renders as "1:00 AM".</summary>
    public static string Format(int minutes)
    {
        var m = ((minutes % Day) + Day) % Day;
        var hour24 = m / 60;
        var minute = m % 60;
        var hour12 = hour24 % 12 == 0 ? 12 : hour24 % 12;
        var suffix = hour24 < 12 ? "AM" : "PM";
        return $"{hour12}:{minute:00} {suffix}";
    }

    /// <summary>"4 PM" on the hour, "4:30 PM" otherwise. For the hour gutter, where space is tight.</summary>
    public static string FormatShort(int minutes)
    {
        var m = ((minutes % Day) + Day) % Day;
        var hour24 = m / 60;
        var minute = m % 60;
        var hour12 = hour24 % 12 == 0 ? 12 : hour24 % 12;
        var suffix = hour24 < 12 ? "AM" : "PM";
        return minute == 0 ? $"{hour12} {suffix}" : $"{hour12}:{minute:00} {suffix}";
    }

    /// <summary>"4:00 PM – 5:30 PM"</summary>
    public static string FormatRange(int startMinutes, int durationMinutes) =>
        $"{Format(startMinutes)} \u2013 {Format(startMinutes + durationMinutes)}";

    /// <summary>Round to the nearest step, so a dragged card lands on a tidy time.</summary>
    public static int Snap(int minutes, int step = 15)
    {
        if (step <= 1) return minutes;
        return (int)Math.Round(minutes / (double)step, MidpointRounding.AwayFromZero) * step;
    }

    /// <summary>Human duration for a collapsed band: "4 hours", "90 minutes", "1 hour 15 min".</summary>
    public static string Humanize(int minutes)
    {
        if (minutes < 60) return $"{minutes} minutes";
        var hours = minutes / 60;
        var rest = minutes % 60;
        var hourPart = hours == 1 ? "1 hour" : $"{hours} hours";
        return rest == 0 ? hourPart : $"{hourPart} {rest} min";
    }

    [GeneratedRegex(@"^\s*(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ap>[APap])\.?\s*[Mm]\.?\s*$")]
    private static partial Regex ClockPattern();
}
