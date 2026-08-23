using System.Text.Json;
using CharterTrip.Core.Models;
using CharterTrip.Core.Services;
using CharterTrip.Infrastructure.Storage;

// Refresh data/trip.seed.json from a live trip.json.
//
// The seed is what the app falls back to when it starts with nothing. Left alone it goes stale,
// and a data loss then costs every edit made since the first commit. Run this whenever the real
// trip has moved on — see docs/DEPLOY.md for pulling trip.json off App Service.
//
//   dotnet run --project tools/CharterTrip.SeedRefresh -- <path-to-trip.json> [output]
//
// Play state is stripped on the way through; see SeedPreparation for what that means. Nothing is
// written until the file parses, so a bad input leaves the existing seed alone.

if (args.Length is 0 or > 2 || args[0] is "-h" or "--help")
{
    Console.Error.WriteLine("usage: seed-refresh <path-to-trip.json> [output-path]");
    Console.Error.WriteLine("       output defaults to data/trip.seed.json at the repo root");
    return 2;
}

var input = Path.GetFullPath(args[0]);
if (!File.Exists(input))
{
    Console.Error.WriteLine($"No such file: {input}");
    return 1;
}

var output = args.Length == 2 ? Path.GetFullPath(args[1]) : DefaultSeedPath();
if (output is null)
{
    Console.Error.WriteLine("Could not find data/trip.seed.json above the current directory. Pass the output path.");
    return 1;
}

TripData trip;
try
{
    trip = JsonSerializer.Deserialize<TripData>(File.ReadAllText(input), TripJson.Options)
           ?? throw new InvalidOperationException("the file deserialized to null");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not read {input} as a trip: {ex.Message}");
    return 1;
}

var revision = trip.Revision;
var capturedAt = trip.UpdatedUtc;

SeedPreparation.Prepare(trip, capturedAt);

File.WriteAllText(output, JsonSerializer.Serialize(trip, TripJson.Options) + Environment.NewLine);

Console.WriteLine($"Wrote {output}");
Console.WriteLine($"  from revision {revision}, last edited {capturedAt:yyyy-MM-dd HH:mm} UTC");
Console.WriteLine($"  {trip.Roster.Count} people, {trip.Itinerary.Sum(d => d.Items.Count)} itinerary items, " +
                  $"{trip.Jeopardy.Categories.Sum(c => c.Clues.Count)} clues");
Console.WriteLine();
Console.WriteLine("Now run the tests before committing — they check the seed's invariants:");
Console.WriteLine("  dotnet test tests/CharterTrip.Tests");
return 0;

// Walk up for the repo's data folder so the tool works from anywhere in the tree.
static string? DefaultSeedPath()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "data", "trip.seed.json");
        if (File.Exists(candidate)) return candidate;
    }

    return null;
}
