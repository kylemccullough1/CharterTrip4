using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CharterTrip.Core.Models;

namespace CharterTrip.Infrastructure.Storage;

/// <summary>
/// One shared set of serializer settings so the file written at runtime and the seed file
/// checked into git look identical. Indented and camelCased on purpose: trip.json is meant
/// to be readable in a diff and editable by hand in an emergency.
/// </summary>
public static class TripJson
{
    public static readonly JsonSerializerOptions Options = Create();

    /// <summary>
    /// The same shape, but nothing omitted and nothing indented. Used only by <see cref="Clone"/>,
    /// where the JSON is a courier rather than a document anybody reads. Skipping nulls is right
    /// for a file meant to diff well and wrong for a copy: a property that is deliberately null
    /// would be left out of the JSON and come back as whatever its initialiser says instead, so
    /// the copy would differ from the original in exactly the places nobody would think to check.
    /// </summary>
    private static readonly JsonSerializerOptions CloneOptions = CreateForCloning();

    /// <summary>
    /// A deep copy of the trip that shares no object with the original.
    ///
    /// Round-tripped through JSON rather than given a copy constructor per model: there are some
    /// forty models, every one of them is a plain property bag, and a hand-written clone that
    /// missed a list added later would fail by quietly *sharing* that list — which is the worst
    /// way for a sandbox to fail, because it looks like it works. The serializer already knows
    /// the whole graph and is the one thing guaranteed to be kept current when a model gains a
    /// field, since the file on disk depends on it. A trip is well under a hundred kilobytes.
    /// </summary>
    public static TripData Clone(TripData source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var json = JsonSerializer.Serialize(source, CloneOptions);
        return JsonSerializer.Deserialize<TripData>(json, CloneOptions)
               ?? throw new InvalidOperationException("Cloning the trip produced nothing.");
    }

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

    private static JsonSerializerOptions CreateForCloning() =>
        new(Create())
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };
}
