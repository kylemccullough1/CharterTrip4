using CharterTrip.Core.Abstractions;

namespace CharterTrip.Tests;

public class JsonTripStoreTests
{
    [Fact]
    public async Task Writes_a_seeded_file_on_first_run()
    {
        await using var fx = new StoreFixture();

        Assert.True(File.Exists(fx.TripFilePath));
        Assert.Equal(26, fx.Store.Current.Roster.Count);
    }

    [Fact]
    public async Task Mutation_survives_a_restart()
    {
        await using var fx = new StoreFixture();

        await fx.Store.MutateAsync(
            t => t.Itinerary[0].Items[0].Title = "Check-in, but louder",
            TripArea.Itinerary);
        await fx.Store.FlushAsync();

        var reloaded = await fx.RestartAsync();
        Assert.Equal("Check-in, but louder", reloaded.Current.Itinerary[0].Items[0].Title);
    }

    [Fact]
    public async Task Revision_increments_and_timestamp_moves()
    {
        await using var fx = new StoreFixture();
        var before = fx.Store.Current.Revision;

        await fx.Store.MutateAsync(t => t.Trip.Tagline = "changed", TripArea.Trip);

        Assert.Equal(before + 1, fx.Store.Current.Revision);
    }

    [Fact]
    public async Task Changed_event_reports_the_area()
    {
        await using var fx = new StoreFixture();

        var seen = new List<TripChanged>();
        fx.Store.Changed += c => { seen.Add(c); return Task.CompletedTask; };

        await fx.Store.MutateAsync(t => t.Trip.Tagline = "x", TripArea.Trip);

        var change = Assert.Single(seen);
        Assert.Equal(TripArea.Trip, change.Area);
        Assert.True(change.Affects(TripArea.Trip));
        Assert.False(change.Affects(TripArea.Budget));
        Assert.True(change.Affects(TripArea.All));
    }

    [Fact]
    public async Task A_throwing_subscriber_does_not_stop_the_others()
    {
        await using var fx = new StoreFixture();

        var reachedSecond = false;
        fx.Store.Changed += _ => throw new InvalidOperationException("component was disposed mid-render");
        fx.Store.Changed += _ => { reachedSecond = true; return Task.CompletedTask; };

        await fx.Store.MutateAsync(t => t.Trip.Tagline = "x", TripArea.Trip);

        Assert.True(reachedSecond);
    }

    [Fact]
    public async Task Concurrent_mutations_do_not_lose_updates()
    {
        await using var fx = new StoreFixture(debounceMs: 5);

        const int writers = 40;
        await Task.WhenAll(Enumerable.Range(0, writers).Select(i =>
            fx.Store.MutateAsync(t => t.Scores.Add(new Core.Models.ScoreEntry
            {
                Id = $"s{i}", TeamId = "jou", GameId = "jeopardy", Points = 1
            }), TripArea.Scores)));

        Assert.Equal(writers, fx.Store.Current.Scores.Count);
        Assert.Equal(writers, fx.Store.Current.Revision);

        await fx.Store.FlushAsync();
        var reloaded = await fx.RestartAsync();
        Assert.Equal(writers, reloaded.Current.Scores.Count);
    }

    [Fact]
    public async Task Debounce_coalesces_a_burst_but_flush_forces_it_out()
    {
        await using var fx = new StoreFixture(debounceMs: 10_000);

        await fx.Store.MutateAsync(t => t.Trip.Tagline = "typed one character at a time", TripArea.Trip);

        // The debounce window is still open, so disk should still hold the seed value.
        var onDiskBefore = await File.ReadAllTextAsync(fx.TripFilePath);
        Assert.DoesNotContain("typed one character at a time", onDiskBefore);

        await fx.Store.FlushAsync();

        var onDiskAfter = await File.ReadAllTextAsync(fx.TripFilePath);
        Assert.Contains("typed one character at a time", onDiskAfter);
    }

    [Fact]
    public async Task Dispose_flushes_pending_work()
    {
        await using var fx = new StoreFixture(debounceMs: 10_000);

        await fx.Store.MutateAsync(t => t.Trip.Tagline = "saved on shutdown", TripArea.Trip);
        var reloaded = await fx.RestartAsync();   // disposes, which must flush

        Assert.Equal("saved on shutdown", reloaded.Current.Trip.Tagline);
    }

    [Fact]
    public async Task Leaves_no_temp_file_behind()
    {
        await using var fx = new StoreFixture();

        await fx.Store.MutateAsync(t => t.Trip.Tagline = "x", TripArea.Trip);
        await fx.Store.FlushAsync();

        Assert.Empty(Directory.GetFiles(fx.DataRoot, "*.tmp"));
    }

    [Fact]
    public async Task An_unreadable_file_is_quarantined_and_the_seed_is_used()
    {
        await using var fx = new StoreFixture();
        await fx.Store.DisposeAsync();

        await File.WriteAllTextAsync(fx.TripFilePath, "{ this is not json at all ");

        var recovered = await fx.RestartAsync();

        Assert.Equal(26, recovered.Current.Roster.Count);                       // fell back to the seed
        Assert.NotEmpty(Directory.GetFiles(fx.DataRoot, "*.unreadable-*"));     // kept the evidence
    }
}
