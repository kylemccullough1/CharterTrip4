using System.Net;

namespace CharterTrip.Web.Services;

/// <summary>
/// Generates the Art Deco placeholder slides as inline SVG data URIs, so the carousel looks
/// deliberate before anyone has uploaded a real photo — and needs no image files or network.
/// </summary>
public static class DecoArt
{
    private static readonly (string Base, string Ink, string Edge)[] Palettes =
    [
        ("#1b2438", "#e8c661", "#3a2f14"),   // midnight & gold
        ("#123027", "#7fd9b5", "#0d3a2c"),   // emerald
        ("#331d24", "#e88f98", "#4a2028"),   // ruby
        ("#182742", "#8fb6f0", "#1d3763")    // sapphire
    ];

    /// <summary>
    /// The same slide, drawn in one team's colour.
    ///
    /// The hero is the first thing anyone sees, and having it glow in the colour of whoever is
    /// winning does more for a weekend-long contest than four fixed palettes ever did. The base
    /// and edge are derived from the one colour rather than picked, so any team colour works —
    /// including whatever somebody types into the colour box on the Teams page.
    /// </summary>
    public static string SlideDataUri(string teamColor)
    {
        var (r, g, b) = ParseHex(teamColor);

        // The ink is the colour itself; the ground is the same hue taken down almost to black,
        // which is what keeps the gold rule and the white text readable over the top of it.
        var ink = Hex(r, g, b);
        var baseColor = Hex(Scale(r, .13), Scale(g, .13), Scale(b, .16));
        var edge = Hex(Scale(r, .26), Scale(g, .26), Scale(b, .30));

        // Unique per colour so two slides on the page cannot share gradient ids.
        return Render(baseColor, ink, edge, $"t{Math.Abs(teamColor.GetHashCode()):x}");
    }

    public static string SlideDataUri(int hue)
    {
        var (baseColor, ink, edge) = Palettes[Math.Abs(hue) % Palettes.Length];
        return Render(baseColor, ink, edge, $"d{Math.Abs(hue) % Palettes.Length}");
    }

    /// <summary>
    /// A colour part way between two others, so the hero can drift from one team's colour to the
    /// next instead of cutting between them.
    /// </summary>
    public static string Mix(string from, string to, double amount)
    {
        var (r1, g1, b1) = ParseHex(from);
        var (r2, g2, b2) = ParseHex(to);
        var t = Math.Clamp(amount, 0, 1);

        return Hex(
            (int)Math.Round(r1 + (r2 - r1) * t),
            (int)Math.Round(g1 + (g2 - g1) * t),
            (int)Math.Round(b1 + (b2 - b1) * t));
    }

    private static (int R, int G, int B) ParseHex(string color)
    {
        var hex = color.TrimStart('#');
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(c => new string(c, 2)));

        return hex.Length == 6
               && int.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r)
               && int.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g)
               && int.TryParse(hex[4..], System.Globalization.NumberStyles.HexNumber, null, out var b)
            ? (r, g, b)
            : (212, 175, 55);            // the house gold, for anything unparseable
    }

    private static int Scale(int channel, double by) => (int)Math.Clamp(channel * by, 0, 255);

    private static string Hex(int r, int g, int b) => $"#{r:x2}{g:x2}{b:x2}";

    private static string Render(string baseColor, string ink, string edge, string id)
    {

        var svg =
            $"""
             <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1200 700" preserveAspectRatio="xMidYMid slice">
             <defs>
               <linearGradient id="g{id}" x1="0" y1="0" x2="1" y2="1">
                 <stop offset="0" stop-color="{baseColor}"/><stop offset="1" stop-color="{edge}"/>
               </linearGradient>
               <radialGradient id="r{id}" cx="50%" cy="45%" r="62%">
                 <stop offset="0" stop-color="{ink}" stop-opacity=".22"/>
                 <stop offset="1" stop-color="{ink}" stop-opacity="0"/>
               </radialGradient>
               <pattern id="p{id}" width="60" height="104" patternUnits="userSpaceOnUse">
                 <path d="M30 2 L58 52 L30 102 L2 52 Z" fill="none" stroke="{ink}" stroke-opacity=".22" stroke-width="2"/>
               </pattern>
             </defs>
             <rect width="1200" height="700" fill="url(#g{id})"/>
             <rect width="1200" height="700" fill="url(#p{id})"/>
             <rect width="1200" height="700" fill="url(#r{id})"/>
             <g fill="none" stroke="{ink}" stroke-opacity=".7" stroke-width="3">
               <circle cx="600" cy="350" r="230"/><circle cx="600" cy="350" r="188"/>
               <path d="M600 70 L880 350 L600 630 L320 350 Z"/>
             </g>
             <g stroke="{ink}" stroke-opacity=".45" stroke-width="2">
               <path d="M0 350 H320 M880 350 H1200 M600 70 V0 M600 630 V700"/>
             </g>
             <path d="M600 240 L690 350 L600 460 L510 350 Z" fill="{ink}" fill-opacity=".85"/>
             </svg>
             """;

        // Collapse the pretty-printing, then escape. Parentheses must be encoded or an
        // unquoted css url(...) terminates early — a bug worth only hitting once.
        var compact = string.Join(' ', svg.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                          .Select(line => line.Trim()));

        var encoded = WebUtility.UrlEncode(compact)!
            .Replace("+", "%20")
            .Replace("(", "%28")
            .Replace(")", "%29");

        return $"data:image/svg+xml;charset=utf-8,{encoded}";
    }
}
