using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CharterTrip.Infrastructure.Mystery;

/// <summary>
/// Serializer settings for the Braun Manor content files.
///
/// Separate from <see cref="Storage.TripJson"/> for one reason: these files are snake_cased and
/// trip.json is camelCased. They were authored by hand as design documents rather than written by
/// this app, so the app reads them on their terms rather than reformatting 110 KB of prose to suit
/// a naming policy.
///
/// Read-only, so there is no indentation or null-handling to agree on — only how a property name
/// in JSON finds a property in C#.
/// </summary>
public static class MysteryJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,

        // The files carry comments in a couple of places and trailing commas are easy to leave
        // behind when editing prose by hand. Tolerating both means a content fix on the day does
        // not have to be syntactically perfect to load.
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,

        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
