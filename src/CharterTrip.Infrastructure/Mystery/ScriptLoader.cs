using System.Reflection;
using System.Text.Json;
using CharterTrip.Core.Mystery.Script;

namespace CharterTrip.Infrastructure.Mystery;

/// <summary>
/// Reads the Braun Manor content out of the assembly and assembles one immutable
/// <see cref="MysteryScript"/>.
///
/// Embedded resources rather than loose files, for the same reason the seed is embedded: if the app
/// is running, the content is there. A murder mystery that starts up fine and then cannot find its
/// characters halfway through the evening is the failure mode this rules out.
///
/// Nine files were promoted to data/braun-manor/; seven of them are loaded here. main_screen.json
/// and player_phone.json are deliberately not: they specify what the two screens should look like,
/// which is instruction for whoever builds them, not data the running game consumes.
/// </summary>
public static class ScriptLoader
{
    private const string Prefix = "CharterTrip.Infrastructure.Mystery.BraunManor.";

    public static MysteryScript Load()
    {
        var script = new MysteryScript
        {
            Characters = Read<CharacterFile>("characters.json").Characters,
            Zones = Read<ScriptZoneBook>("zones.json"),
            Factions = Read<ScriptFactionBook>("factions.json"),
            Rounds = Read<ScriptRoundBook>("rounds.json"),
            StoryBeats = Read<ScriptStoryBeats>("story_beats.json"),
            Prompts = Read<ScriptPromptBook>("prompts.json"),
            Ghosts = Read<ScriptGhostBook>("ghosts_npcs.json")
        };

        // Fail here rather than while dealing a game. A content file edited by hand on the day
        // should break startup, loudly, with the reason — not produce a subtly wrong evening.
        var problems = script.Validate();
        if (problems.Count > 0)
            throw new InvalidOperationException(
                "Braun Manor content is not coherent:" + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(p => "  - " + p)));

        return script;
    }

    public static string ReadRawJson(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = Prefix + fileName;

        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"Embedded content '{resource}' is missing. Available: " +
                string.Join(", ", assembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static T Read<T>(string fileName)
    {
        var json = ReadRawJson(fileName);

        try
        {
            return JsonSerializer.Deserialize<T>(json, MysteryJson.Options)
                ?? throw new InvalidOperationException($"'{fileName}' deserialized to null.");
        }
        catch (JsonException ex)
        {
            // The default message names a path but not the file, which is unhelpful when seven
            // files share one loader.
            throw new InvalidOperationException($"'{fileName}' is not valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>characters.json wraps its array in an object; nothing else needs a wrapper type.</summary>
    private sealed record CharacterFile
    {
        public IReadOnlyList<ScriptCharacter> Characters { get; init; } = [];
    }
}
