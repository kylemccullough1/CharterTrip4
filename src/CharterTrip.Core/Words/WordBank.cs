using System.Text.Json;

namespace CharterTrip.Core.Words;

/// <summary>One difficulty tier of the Scripps word bank.</summary>
/// <param name="Key">The key in word-bank.json. Persisted on every dealt word, so it must not change.</param>
/// <param name="Label">What the host's phone calls it.</param>
public sealed record WordTier(string Key, string Label);

/// <summary>
/// Three thousand eight hundred and fifty words from the Scripps National Spelling Bee's
/// <em>Words of the Champions</em> (2020), split into six tiers.
///
/// Compiled into the assembly as an embedded resource rather than seeded into trip.json. The
/// bank is source, not state: nobody edits it during the weekend, it is the same on every
/// machine, and putting fifty kilobytes of vocabulary into a document that is rewritten on
/// every keystroke would cost the thing that makes trip.json work, which is reading it in a
/// diff. What lands in the trip is the dealt deck — a couple of hundred words — and nothing else.
/// </summary>
public static class WordBank
{
    private const string ResourceName = "CharterTrip.Core.Words.word-bank.json";

    /// <summary>
    /// Easiest first. The order is load-bearing: a dealt deck is laid out in tier order, so this
    /// list <em>is</em> the difficulty curve the room experiences.
    /// </summary>
    public static readonly IReadOnlyList<WordTier> Tiers =
    [
        new("easy", "Easy"),
        new("easyModerate", "Easy–Moderate"),
        new("moderate", "Moderate"),
        new("moderatelyDifficult", "Moderately Difficult"),
        new("difficult", "Difficult"),
        new("expert", "Expert")
    ];

    // Read once, on first use, and never again — the file is embedded and cannot change under us.
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<BankEntry>>> Bank = new(Read);

    /// <summary>The words in one tier, or an empty list if the key is not a tier.</summary>
    public static IReadOnlyList<BankEntry> Pool(string tierKey) =>
        Bank.Value.TryGetValue(tierKey, out var pool) ? pool : [];

    /// <summary>How many words in this tier can answer a speller who asks. Reported by the editor.</summary>
    public static int WithHelp(string tierKey) =>
        Pool(tierKey).Count(e => e.HasHelp);

    public static string LabelFor(string tierKey) =>
        Tiers.FirstOrDefault(t => t.Key == tierKey)?.Label ?? tierKey;

    public static bool IsTier(string tierKey) => Tiers.Any(t => t.Key == tierKey);

    /// <summary>Every word in the bank. Used by the tests that guard the file's shape.</summary>
    public static int TotalWords => Bank.Value.Sum(kv => kv.Value.Count);

    private static IReadOnlyDictionary<string, IReadOnlyList<BankEntry>> Read()
    {
        var assembly = typeof(WordBank).Assembly;

        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded word bank '{ResourceName}' is missing. Available: " +
                string.Join(", ", assembly.GetManifestResourceNames()));

        var raw = JsonSerializer.Deserialize<Dictionary<string, List<BankEntry>>>(stream, ReadOptions)
            ?? throw new InvalidOperationException("The word bank deserialized to null.");

        // Indexed by the tier list rather than by whatever the file happens to contain, so a
        // typo'd key in the JSON surfaces as an empty tier at deal time instead of a phantom one.
        return Tiers.ToDictionary(
            t => t.Key,
            t => (IReadOnlyList<BankEntry>)(raw.TryGetValue(t.Key, out var words) ? words : []),
            StringComparer.Ordinal);
    }

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new BankEntryConverter() }
    };
}

/// <summary>
/// One word and whatever a speller is allowed to ask about it.
/// </summary>
public sealed record BankEntry(string Word, string Definition = "", string PartOfSpeech = "", string Sentence = "")
{
    public bool HasHelp =>
        !string.IsNullOrWhiteSpace(Definition) ||
        !string.IsNullOrWhiteSpace(PartOfSpeech) ||
        !string.IsNullOrWhiteSpace(Sentence);
}

/// <summary>
/// Reads a tier entry written either as a bare string or as an object.
///
/// Both shapes are supported on purpose: the bank started as plain word lists and the enrichment
/// tool rewrites entries in place as it works through them, so a half-enriched file is the normal
/// state of that file rather than a corrupt one.
/// </summary>
internal sealed class BankEntryConverter : System.Text.Json.Serialization.JsonConverter<BankEntry>
{
    public override BankEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new BankEntry(reader.GetString() ?? "");

        using var doc = JsonDocument.ParseValue(ref reader);
        var el = doc.RootElement;

        return new BankEntry(
            Text(el, "word"),
            Text(el, "definition"),
            Text(el, "partOfSpeech"),
            Text(el, "sentence"));

        static string Text(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }

    public override void Write(Utf8JsonWriter writer, BankEntry value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("word", value.Word);
        if (!string.IsNullOrWhiteSpace(value.Definition)) writer.WriteString("definition", value.Definition);
        if (!string.IsNullOrWhiteSpace(value.PartOfSpeech)) writer.WriteString("partOfSpeech", value.PartOfSpeech);
        if (!string.IsNullOrWhiteSpace(value.Sentence)) writer.WriteString("sentence", value.Sentence);
        writer.WriteEndObject();
    }
}
