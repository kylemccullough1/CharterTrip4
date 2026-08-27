using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>A colour the site can be painted in: the hex it was given, and the same as a triplet.</summary>
public readonly record struct Accent(string Hex, string Rgb);

/// <summary>
/// Which colour the site wears.
///
/// Everything gold reads from a handful of custom properties, so handing those the leading team's
/// colour repaints the whole site. That means a team colour — typed by hand on the teams page —
/// ends up inside a stylesheet, which is why parsing it is here, tested, and refuses anything
/// that is not plainly a hex colour rather than trusting what it is handed.
/// </summary>
public static class AccentPalette
{
    /// <summary>
    /// The team to paint the site in, or null to leave it gold.
    ///
    /// Null while nobody has scored and while the top two are level: there is no leader to be, and
    /// picking whoever happens to be stored first would announce a lead that does not exist.
    /// </summary>
    public static Accent? Leader(TripData trip)
    {
        var standings = TripSummary.Standings(trip);

        if (standings.Count == 0 || standings[0].Total <= 0) return null;
        if (standings.Count > 1 && standings[0].Total == standings[1].Total) return null;

        return Parse(standings[0].Team.Color);
    }

    /// <summary>
    /// A hex colour, or null. Accepts <c>#abc</c> and <c>#aabbcc</c> and nothing else — no named
    /// colours, no rgb(), nothing that could close a declaration and open something of its own.
    /// </summary>
    public static Accent? Parse(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;

        var hex = color.Trim();
        if (hex.Length is not (4 or 7) || hex[0] != '#') return null;

        var digits = hex[1..];
        if (!digits.All(Uri.IsHexDigit)) return null;

        // #abc means #aabbcc, and the triplet has to be the expanded one.
        if (digits.Length == 3)
            digits = string.Concat(digits.Select(d => new string(d, 2)));

        var r = Convert.ToInt32(digits[..2], 16);
        var g = Convert.ToInt32(digits[2..4], 16);
        var b = Convert.ToInt32(digits[4..6], 16);

        return new Accent(hex, $"{r}, {g}, {b}");
    }
}
