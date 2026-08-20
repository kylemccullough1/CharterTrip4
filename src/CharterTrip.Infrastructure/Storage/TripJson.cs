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
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
