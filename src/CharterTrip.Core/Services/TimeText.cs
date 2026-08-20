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

    [GeneratedRegex(@"^\s*(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ap>[APap])\.?\s*[Mm]\.?\s*$")]
    private static partial Regex ClockPattern();
}
