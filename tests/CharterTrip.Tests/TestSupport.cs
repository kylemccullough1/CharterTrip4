using CharterTrip.Core.Abstractions;
using CharterTrip.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
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

    /// <summary>Everything the store logged, so a test can assert it stayed quiet.</summary>
    public RecordingLogger<JsonTripStore> Log { get; } = new();

    public StoreFixture(int debounceMs = 0)
    {
        DataRoot = Path.Combine(Path.GetTempPath(), "chartertrip-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(DataRoot);
        Options = new TripStoreOptions { DataRoot = DataRoot, DebounceMilliseconds = debounceMs };
        Store = Build();
    }

    private JsonTripStore Build() => new(
        Microsoft.Extensions.Options.Options.Create(Options),
        Log,
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

/// <summary>
/// An ILogger that keeps what it was told. Only the failures are interesting — a test that asserts
/// on informational logging would break every time somebody rewords a message.
/// </summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<string> _failures = [];

    /// <summary>Warnings and errors, newest last. Formatted, with the exception type appended.</summary>
    public IReadOnlyList<string> Failures
    {
        get { lock (_failures) return _failures.ToList(); }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel < LogLevel.Warning) return;

        var line = formatter(state, exception) + (exception is null ? "" : $" [{exception.GetType().Name}]");
        lock (_failures) _failures.Add(line);
    }
}
