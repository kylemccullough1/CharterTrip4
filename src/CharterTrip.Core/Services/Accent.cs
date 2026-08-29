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
    /// The colours anything on the site is allowed to be painted: the four the teams started as —
    /// gold, emerald, ruby, sapphire, the same four named in the stylesheet's :root — and then six
    /// more in the same jewel key, muted the same amount so none of them shouts next to the others
    /// on a dark panel.
    ///
    /// One list rather than one per feature. The carpools picked from these first, and teams pick
    /// from them now, so a carpool and a team that look the same colour are the same colour.
    /// </summary>
    public static readonly IReadOnlyList<string> Swatches =
    [
        "#d4af37", "#2e9e7e", "#c94f5a", "#4a7fd6", "#b07cc6",
        "#d98c3f", "#4fb0a5", "#c96f9b", "#8fb339", "#e0736d"
    ];

    /// <summary>
    /// The entry in <see cref="Swatches"/> that a colour is, or null if it is not one of them.
    ///
    /// Matched as colours rather than as text, so <c>#D4AF37</c> and <c>#d4af37</c> are the one
    /// colour they plainly are — and it hands back the list's own spelling rather than the caller's,
    /// so what gets stored is a palette entry exactly and comparing two of them is comparing strings.
    /// </summary>
    public static string? MatchSwatch(string? color)
    {
        if (Parse(color) is not { } given) return null;

        return Swatches.FirstOrDefault(s => Parse(s)!.Value.Rgb == given.Rgb);
    }

    /// <summary>Whether a colour is one of <see cref="Swatches"/>.</summary>
    public static bool IsSwatch(string? color) => MatchSwatch(color) is not null;

    /// <summary>
    /// A colour in the one spelling the site stores: lowercase, six digits, leading hash — or null
    /// if it is not a hex colour at all. <c>#ABC</c> becomes <c>#aabbcc</c>. This is what a team
    /// colour picked off a colour wheel is written down as, so two teams that chose the same
    /// colour by different routes compare equal as text.
    /// </summary>
    public static string? Canonical(string? color)
    {
        if (Parse(color) is not { } accent) return null;

        var parts = accent.Rgb.Split(", ").Select(int.Parse).ToArray();
        return $"#{parts[0]:x2}{parts[1]:x2}{parts[2]:x2}";
    }

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
