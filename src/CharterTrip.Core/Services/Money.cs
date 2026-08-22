using System.Globalization;

namespace CharterTrip.Core.Services;

/// <summary>
/// Parsing and formatting for the money people actually type: "$12.50", "1,299", " 4.47 ".
/// Invariant culture throughout so the same string means the same number everywhere.
/// </summary>
public static class Money
{
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var cleaned = text.Trim().Replace("$", "").Replace(",", "").Replace(" ", "");
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Parse, or fall back — used by inputs where a bad keystroke shouldn't wipe the cell.</summary>
    public static decimal ParseOr(string? text, decimal fallback) =>
        TryParse(text, out var value) ? value : fallback;

    public static string Format(decimal value) =>
        value.ToString("C2", CultureInfo.GetCultureInfo("en-US"));

    /// <summary>No currency symbol — for input fields, where the symbol is decoration.</summary>
    public static string Plain(decimal value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
