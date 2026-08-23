using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CharterTrip.Infrastructure.Storage;

/// <summary>
/// One shared set of serializer settings so the file written at runtime and the seed file
/// checked into git look identical. Indented and camelCased on purpose: trip.json is meant
/// to be readable in a diff and editable by hand in an emergency.
/// </summary>
public static class TripJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

            // Without this the default encoder escapes &, ' and every non-ASCII character, so
            // "catch & release" is stored as "catch \u0026 release". It is valid JSON and the app
            // never noticed, but it broke the one promise this class makes: the file on disk did
            // not look like the seed in git, and neither was pleasant to read in a diff. Safe
            // here because this JSON is only ever read back by the app — Blazor does its own
            // encoding on the way to the page.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
