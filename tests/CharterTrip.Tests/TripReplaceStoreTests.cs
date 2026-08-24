using CharterTrip.Core.Abstractions;
using CharterTrip.Core.Models;
using CharterTrip.Infrastructure.Seed;
using CharterTrip.Infrastructure.Storage;

namespace CharterTrip.Tests;

/// <summary>
/// ReplaceAsync is the store side of the import page: the one call that throws away the whole
/// document on purpose. What is tested here is mostly what it must not do quietly.
/// </summary>
public class TripReplaceStoreTests
{
    [Fact]
    public async Task Replacing_swaps_the_contents_and_bumps_the_revision()
    {
        await using var fx = new StoreFixture();
        await fx.Store.MutateAsync(t => t.Trip.Kicker = "the old trip", TripArea.Trip);
        var before = fx.Store.Current.Revision;

        await fx.Store.ReplaceAsync(Incoming());

        Assert.Equal("An imported weekend", fx.Store.Current.Trip.Kicker);
        Assert.Equal(before + 1, fx.Store.Current.Revision);
    }

    /// <summary>
    /// The uploaded file carries whatever revision the app that wrote it had reached — often a
    /// smaller number than the live site's. Taking it would make the revision go backwards and
    /// quietly ruin the only counter anyone trusts when an edit goes missing.
    /// </summary>
    [Fact]
    public async Task The_revision_never_goes_backwards()
    {
        await using var fx = new StoreFixture();
        for (var i = 0; i < 5; i++)
            await fx.Store.MutateAsync(t => t.Trip.Kicker = $"edit {i}", TripArea.Trip);

        var before = fx.Store.Current.Revision;
        var incoming = Incoming();
        incoming.Revision = 1;

        await fx.Store.ReplaceAsync(incoming);

        Assert.Equal(before + 1, fx.Store.Current.Revision);
    }

    /// <summary>
    /// Every open page is holding the object ITripStore.Current handed it. Replace the reference
    /// instead of its contents and those pages keep rendering — and keep editing — a document
    /// nothing saves any more, while the import looks like it worked.
    /// </summary>
    [Fact]
    public async Task Pages_holding_the_live_document_see_the_import()
    {
        await using var fx = new StoreFixture();
        var held = fx.Store.Current;

        await fx.Store.ReplaceAsync(Incoming());

        Assert.Same(held, fx.Store.Current);
        Assert.Equal("An imported weekend", held.Trip.Kicker);
    }

    /// <summary>Not on the debounce: an import that a crash could undo is not an import.</summary>
    [Fact]
    public async Task The_new_trip_is_on_disk_before_the_call_returns()
    {
        await using var fx = new StoreFixture(debounceMs: 60_000);

        await fx.Store.ReplaceAsync(Incoming());

        Assert.Contains("An imported weekend", await File.ReadAllTextAsync(fx.TripFilePath));
    }

    [Fact]
    public async Task The_imported_trip_survives_a_restart()
    {
        await using var fx = new StoreFixture();

        await fx.Store.ReplaceAsync(Incoming());
        var reloaded = await fx.RestartAsync();

        Assert.Equal("An imported weekend", reloaded.Current.Trip.Kicker);
    }

    /// <summary>
    /// The one operation with no undo. A copy of what it overwrote is the difference between a
    /// bad click and a lost weekend.
    /// </summary>
    [Fact]
    public async Task What_the_import_overwrote_is_kept()
    {
        await using var fx = new StoreFixture();
        await fx.Store.MutateAsync(t => t.Trip.Kicker = "worth keeping", TripArea.Trip);
        await fx.Store.FlushAsync();

        await fx.Store.ReplaceAsync(Incoming());

        var archived = Directory.GetFiles(Path.Combine(fx.DataRoot, "backups"), "replaced-*.json");
        Assert.Single(archived);
        Assert.Contains("worth keeping", await File.ReadAllTextAsync(archived[0]));
    }

    /// <summary>
    /// The rolling backup timer prunes trip-*.json down to the most recent few. The copy an
    /// import destroyed must not be able to age out of the folder on a timer, so it is named
    /// outside that pattern — this is the test that says so.
    /// </summary>
    [Fact]
    public async Task The_pre_import_copy_is_not_a_rolling_backup()
    {
        await using var fx = new StoreFixture();
        await fx.Store.FlushAsync();

        await fx.Store.ReplaceAsync(Incoming());

        var backups = Path.Combine(fx.DataRoot, "backups");
        Assert.Empty(Directory.GetFiles(backups, "trip-*.json"));
        Assert.Single(Directory.GetFiles(backups, "replaced-*.json"));
    }

    /// <summary>Everything re-renders, because everything may have changed.</summary>
    [Fact]
    public async Task Subscribers_are_told_the_whole_trip_changed()
    {
        await using var fx = new StoreFixture();

        TripChanged? seen = null;
        fx.Store.Changed += change => { seen = change; return Task.CompletedTask; };

        await fx.Store.ReplaceAsync(Incoming());

        Assert.NotNull(seen);
        Assert.Equal(TripArea.All, seen!.Value.Area);
        Assert.True(seen.Value.Affects(TripArea.Jeopardy));
        Assert.True(seen.Value.Affects(TripArea.Itinerary));
    }

    [Fact]
    public async Task Replacing_with_nothing_is_refused()
    {
        await using var fx = new StoreFixture();

        await Assert.ThrowsAsync<ArgumentNullException>(() => fx.Store.ReplaceAsync(null!));
    }

    /// <summary>A trip that is recognisably not the seed, so an assert cannot pass by accident.</summary>
    private static TripData Incoming()
    {
        var trip = SeedLoader.Load();
        TripMigrations.Apply(trip);
        trip.Trip.Kicker = "An imported weekend";
        return trip;
    }
}
