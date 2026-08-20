using System.Reflection;
using System.Text.Json;
using CharterTrip.Core.Models;
using CharterTrip.Infrastructure.Storage;

namespace CharterTrip.Infrastructure.Seed;

/// <summary>
/// The starting dataset, compiled into the assembly as an embedded resource rather than
/// shipped as a loose file. That means there is no "where did the seed go?" problem on Azure —
/// if the app is running, the seed is there.
/// </summary>
public static class SeedLoader
{
    private const string ResourceName = "CharterTrip.Infrastructure.Seed.trip.seed.json";

    public static string ReadRawJson()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded seed '{ResourceName}' is missing. Available: " +
                string.Join(", ", assembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static TripData Load() =>
        JsonSerializer.Deserialize<TripData>(ReadRawJson(), TripJson.Options)
        ?? throw new InvalidOperationException("Embedded seed deserialized to null.");
}
