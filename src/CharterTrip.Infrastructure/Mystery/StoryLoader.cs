using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CharterTrip.Core.Models;

namespace CharterTrip.Infrastructure.Mystery;

/// <summary>
/// Reads the starting story out of the assembly.
///
/// Embedded rather than loose, for the same reason the seed is: if the app is running, the content
/// is there. But unlike the old script, this is a <em>starting point</em> and not the live copy —
/// <see cref="SeedInto"/> writes it into the trip once, and from then on the story is ordinary
/// editable state that lives in trip.json and is changed on the site.
///
/// Which is why nothing here validates. The old loader threw at startup listing everything wrong,
/// because a hand-edited content file had to break the app rather than produce a subtly wrong
/// evening. A story that is edited through a form cannot arrive malformed, and one that arrives
/// half-written is the normal state of affairs for weeks — the Content gaps panel is where that
/// gets reported now, not an exception during boot.
/// </summary>
public static class StoryLoader
{
    private const string Prefix = "CharterTrip.Infrastructure.Mystery.BraunManor.";

    /// <summary>
    /// snake_case, and separate from <c>TripJson</c> on purpose: these files are written by hand
    /// and read on their own terms, where trip.json is camelCased and written by the app.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static MysteryStory Load() => new()
    {
        Characters = Read<CharacterFile>("characters.json").Characters,
        Zones = Read<ZoneFile>("zones.json").Zones,
        Factions = Read<FactionFile>("factions.json").Factions,
        Clues = Read<ClueFile>("clues.json").Clues,
        Slides = Read<SlideFile>("slides.json").Slides,
        Objectives = Read<ObjectiveFile>("objectives.json").Objectives,
        Beefs = Read<BeefFile>("beefs.json").Beefs,
        Beats = Read<MysteryBeats>("beats.json"),
        Seeded = true
    };

    /// <summary>
    /// Put the starting story into a trip that has none.
    ///
    /// Guarded on <see cref="MysteryStory.Seeded"/> rather than on the story being empty, so that
    /// deleting the last character on the editor does not quietly restore twenty-five of them on
    /// the next page load.
    /// </summary>
    public static bool SeedInto(TripData trip)
    {
        if (trip.Mystery.Story.Seeded) return false;

        trip.Mystery.Story = Load();
        return true;
    }

    private static T Read<T>(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = Prefix + fileName;

        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"Embedded content '{resource}' is missing. Available: " +
                string.Join(", ", assembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options)
                ?? throw new InvalidOperationException($"'{fileName}' deserialized to null.");
        }
        catch (JsonException ex)
        {
            // The default message names a path but not the file, which is unhelpful when eight
            // files share one loader.
            throw new InvalidOperationException($"'{fileName}' is not valid JSON: {ex.Message}", ex);
        }
    }

    // Each file wraps its array in an object, which keeps room for file-level fields later without
    // a breaking change. beats.json is the exception — it is already an object.
    private sealed record CharacterFile { public List<MysteryCharacter> Characters { get; init; } = []; }
    private sealed record ZoneFile { public List<MysteryZone> Zones { get; init; } = []; }
    private sealed record FactionFile { public List<MysteryFaction> Factions { get; init; } = []; }
    private sealed record ClueFile { public List<MysteryClueCard> Clues { get; init; } = []; }
    private sealed record SlideFile { public List<MysterySlide> Slides { get; init; } = []; }
    private sealed record ObjectiveFile { public List<MysteryObjectiveTemplate> Objectives { get; init; } = []; }
    private sealed record BeefFile { public List<MysteryBeef> Beefs { get; init; } = []; }
}
