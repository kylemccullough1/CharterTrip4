using CharterTrip.Core.Abstractions;
using CharterTrip.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CharterTrip.Tests;

public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

/// <summary>A JsonTripStore pointed at a throwaway directory that cleans itself up.</summary>
public sealed class StoreFixture : IAsyncDisposable
{
    public string DataRoot { get; }
    public TripStoreOptions Options { get; }
    public JsonTripStore Store { get; private set; }

    public StoreFixture(int debounceMs = 0)
    {
        DataRoot = Path.Combine(Path.GetTempPath(), "chartertrip-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(DataRoot);
        Options = new TripStoreOptions { DataRoot = DataRoot, DebounceMilliseconds = debounceMs };
        Store = Build();
    }

    private JsonTripStore Build() => new(
        Microsoft.Extensions.Options.Options.Create(Options),
        NullLogger<JsonTripStore>.Instance,
        new FixedClock(DateTimeOffset.UnixEpoch));

    /// <summary>Dispose the current store (flushing it) and construct a new one from the same folder.</summary>
    public async Task<JsonTripStore> RestartAsync()
    {
        await Store.DisposeAsync();
        Store = Build();
        return Store;
    }

    public string TripFilePath => Options.TripFilePath;

    public async ValueTask DisposeAsync()
    {
        await Store.DisposeAsync();
        try { Directory.Delete(DataRoot, recursive: true); } catch { /* best effort */ }
    }
}
