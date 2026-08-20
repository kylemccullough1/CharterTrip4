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

    public static string SlideDataUri(int hue)
    {
        var (baseColor, ink, edge) = Palettes[Math.Abs(hue) % Palettes.Length];
        var id = $"d{Math.Abs(hue) % Palettes.Length}";

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
