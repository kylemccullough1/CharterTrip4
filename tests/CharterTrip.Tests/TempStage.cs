using System.Text.Json;
using CharterTrip.Core.Models;
using CharterTrip.Core.Mystery;
using CharterTrip.Infrastructure.Mystery;
using CharterTrip.Infrastructure.Seed;
using CharterTrip.Infrastructure.Storage;

namespace CharterTrip.Tests;

public class TempStage
{
    [Fact]
    public void Write()
    {
        var path = @"C:\Users\kylem\OneDrive\Documents\Coding\CharterTrip4\feature\murder-mystery\src\CharterTrip.Web\App_Data\trip.json";
        var script = ScriptLoader.Load();
        var trip = SeedLoader.Load();
        TripMigrations.Apply(trip);
        MysteryService.DealGame(trip, script, 1234);
        MysteryService.Start(trip);

        // A few guests already in, so the screens have something on them.
        var rng = new Random(5);
        foreach (var p in MysteryService.Unclaimed(trip).Take(14).ToList())
            MysteryService.ClaimCharacter(trip, p.Id, rng);

        var admins = MysteryService.Organizers(trip);
        MysteryService.ClaimNpc(trip, script, admins[0].Id, "braun");
        MysteryService.ClaimNpc(trip, script, admins[1].Id, "bertram");

        MysteryService.GoToRound(trip, script, 2);
        var clues = trip.Mystery.Clues.Take(3).ToList();
        foreach (var c in clues)
            MysteryService.RecordClueFound(trip, c, trip.Mystery.Deal!.Cast[0].CharacterId, DateTimeOffset.UtcNow);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(trip, TripJson.Options));

        var guest = trip.Mystery.Deal!.Cast.First(c => c.PersonId is not null);
        File.WriteAllText(@"C:\Users\kylem\AppData\Local\Temp\claude\demo.txt",
            $"{trip.Mystery.PartyCode}|{trip.Mystery.HostCode}|" +
            $"{trip.Roster.First(p => p.Role == TripRole.Admin).JoinToken}|" +
            $"{trip.Roster.First(p => p.Id == guest.PersonId).JoinToken}");
    }
}
