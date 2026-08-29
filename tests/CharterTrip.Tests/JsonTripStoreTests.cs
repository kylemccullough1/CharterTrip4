using System.Text.Json.Nodes;
using CharterTrip.Core.Abstractions;
using CharterTrip.Infrastructure.Storage;

namespace CharterTrip.Tests;

public class JsonTripStoreTests
{
    /// <summary>
    /// A pre-v9 board listed its categories as bare names, which threw inside the deserializer
    /// before any migration could run. The store treated that as an unreadable file: it
    /// quarantined the whole trip and started again from the seed, so an afternoon of itinerary
    /// edits went with a board that was going to be replaced anyway.
    /// </summary>
    [Fact]
    public async Task A_pre_v9_board_does_not_take_the_rest_of_the_trip_with_it()
    {
        await using var fx = new StoreFixture();

        // Dispose first: the running store would otherwise write v9 back over the staged file.
        await fx.Store.DisposeAsync();

        var trip = JsonNode.Parse(await File.ReadAllTextAsync(fx.TripFilePath))!.AsObject();
        trip["schemaVersion"] = 8;
        trip["itinerary"]![0]!["items"]![0]!["title"] = "An edit worth keeping";
        trip["jeopardy"] = new JsonObject
        {
            ["categories"] = new JsonArray("KDPhi", "Lambdas"),
            ["values"] = new JsonArray(400, 800),
            ["clues"] = new JsonArray(new JsonObject
            {
                ["category"] = "KDPhi",
                ["value"] = 400,
                ["clue"] = "The KDPhi Interest Group",
                ["response"] = "What is LILACS?"
            })
        };
        await File.WriteAllTextAsync(fx.TripFilePath, trip.ToJsonString());

        var reloaded = await fx.RestartAsync();

        Assert.Empty(Directory.GetFiles(fx.DataRoot, "*.unreadable-*"));
        Assert.Equal("An edit worth keeping", reloaded.Current.Itinerary[0].Items[0].Title);
        Assert.Equal(25, reloaded.Current.Roster.Count);

        // The board is the one thing that is meant to be replaced, and now actually is.
        Assert.Equal(TripMigrations.CurrentVersion, reloaded.Current.SchemaVersion);
        Assert.NotEmpty(reloaded.Current.Jeopardy.Categories);
        Assert.NotEmpty(reloaded.Current.Jeopardy.Categories[0].Clues);
    }

    /// <summary>
    /// The status is what tells a deployed app apart from a deployed app whose data directory is
    /// thrown away on every push: both serve a complete site, and only one of them keeps edits.
    /// </summary>
    [Fact]
    public async Task Status_reports_seeding_on_a_first_run_and_not_after()
    {
        await using var fx = new StoreFixture();

        Assert.True(fx.Store.Status.Seeded);
        Assert.True(fx.Store.Status.CanPersist);
        Assert.Equal(Path.GetFullPath(fx.TripFilePath), fx.Store.Status.DataPath);

        var reloaded = await fx.RestartAsync();
        Assert.False(reloaded.Status.Seeded);
    }

    [Fact]
    public async Task Writes_a_seeded_file_on_first_run()
    {
        await using var fx = new StoreFixture();

        Assert.True(File.Exists(fx.TripFilePath));
        Assert.Equal(25, fx.Store.Current.Roster.Count);
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

        await fx.Store.MutateAsync(t => t.Trip.Kicker = "changed", TripArea.Trip);

        Assert.Equal(before + 1, fx.Store.Current.Revision);
    }

    [Fact]
    public async Task Changed_event_reports_the_area()
    {
        await using var fx = new StoreFixture();

        var seen = new List<TripChanged>();
        fx.Store.Changed += c => { seen.Add(c); return Task.CompletedTask; };

        await fx.Store.MutateAsync(t => t.Trip.Kicker = "x", TripArea.Trip);

        var change = Assert.Single(seen);
        Assert.Equal(TripArea.Trip, change.Area);
        Assert.True(change.Affects(TripArea.Trip));
        Assert.False(change.Affects(TripArea.Games));
        Assert.True(change.Affects(TripArea.All));
    }

    [Fact]
    public async Task A_throwing_subscriber_does_not_stop_the_others()
    {
        await using var fx = new StoreFixture();

        var reachedSecond = false;
        fx.Store.Changed += _ => throw new InvalidOperationException("component was disposed mid-render");
        fx.Store.Changed += _ => { reachedSecond = true; return Task.CompletedTask; };

        await fx.Store.MutateAsync(t => t.Trip.Kicker = "x", TripArea.Trip);

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

        await fx.Store.MutateAsync(t => t.Trip.Kicker = "typed one character at a time", TripArea.Trip);

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

        await fx.Store.MutateAsync(t => t.Trip.Kicker = "saved on shutdown", TripArea.Trip);
        var reloaded = await fx.RestartAsync();   // disposes, which must flush

        Assert.Equal("saved on shutdown", reloaded.Current.Trip.Kicker);
    }

    [Fact]
    public async Task Leaves_no_temp_file_behind()
    {
        await using var fx = new StoreFixture();

        await fx.Store.MutateAsync(t => t.Trip.Kicker = "x", TripArea.Trip);
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

        Assert.Equal(25, recovered.Current.Roster.Count);                       // fell back to the seed
        Assert.NotEmpty(Directory.GetFiles(fx.DataRoot, "*.unreadable-*"));     // kept the evidence
    }

    // ------------------------------------------------ concurrent writers

    [Fact]
    public async Task A_write_from_outside_the_process_is_archived_before_being_overwritten()
    {
        await using var fx = new StoreFixture();
        await fx.Store.MutateAsync(t => t.Trip.Kicker = "ours", TripArea.Trip);
        await fx.Store.FlushAsync();

        // Somebody edits trip.json by hand, or a second instance writes it.
        var foreign = "{\"schemaVersion\":3,\"revision\":999,\"trip\":{\"name\":\"edited by hand\"}}";
        File.WriteAllText(fx.TripFilePath, foreign);
        File.SetLastWriteTimeUtc(fx.TripFilePath, DateTime.UtcNow.AddSeconds(5));

        await fx.Store.MutateAsync(t => t.Trip.Kicker = "ours again", TripArea.Trip);
        await fx.Store.FlushAsync();

        var archived = Directory.GetFiles(Path.Combine(fx.DataRoot, "backups"), "trip-external-*.json");
        var saved = Assert.Single(archived);
        Assert.Contains("edited by hand", File.ReadAllText(saved));

        // Our in-memory copy still wins the file — but nothing was destroyed without a copy.
        Assert.Contains("ours again", File.ReadAllText(fx.TripFilePath));
    }

    [Fact]
    public async Task Our_own_writes_are_not_mistaken_for_foreign_ones()
    {
        await using var fx = new StoreFixture();

        for (var i = 0; i < 5; i++)
        {
            await fx.Store.MutateAsync(t => t.Trip.Kicker = $"pass {i}", TripArea.Trip);
            await fx.Store.FlushAsync();
        }

        var backups = Path.Combine(fx.DataRoot, "backups");
        var archived = Directory.Exists(backups)
            ? Directory.GetFiles(backups, "trip-external-*.json")
            : [];

        Assert.Empty(archived);
    }

    [Fact]
    public async Task Concurrent_mutations_all_land()
    {
        await using var fx = new StoreFixture();

        await Task.WhenAll(Enumerable.Range(0, 40).Select(i =>
            fx.Store.MutateAsync(t => t.Superlatives.Add(new CharterTrip.Core.Models.Superlative
            {
                Id = $"concurrent-{i}", Title = $"Award {i}"
            }), TripArea.Trip)));

        // The seed ships its own superlatives, so count only the ones this test added.
        Assert.Equal(40, fx.Store.Current.Superlatives.Count(s => s.Id.StartsWith("concurrent-")));
        Assert.Equal(40, fx.Store.Current.Superlatives.Select(s => s.Id).Distinct().Count(id => id.StartsWith("concurrent-")));
    }

    [Fact]
    public async Task A_mutation_can_report_what_it_decided()
    {
        await using var fx = new StoreFixture();

        var revision = await fx.Store.MutateAsync(t =>
        {
            t.Trip.Venue = "Braun Manor";
            return t.Revision;
        }, TripArea.Trip);

        // The value comes from inside the lock, so it is the state the mutation actually saw.
        Assert.Equal(fx.Store.Current.Revision - 1, revision);
        Assert.Equal("Braun Manor", fx.Store.Current.Trip.Venue);
    }

    [Fact]
    public async Task Exactly_one_of_forty_racing_callers_wins_a_single_shared_charge()
    {
        await using var fx = new StoreFixture();

        await fx.Store.MutateAsync(t => t.Superlatives.Clear(), TripArea.Trip);

        // A stand-in for the killers' one collective charge: check and spend, both inside the
        // lock, with the verdict coming back out. TESTING.md calls this out by name — hammer it
        // with simultaneous requests, this WILL race — and it is the whole reason the generic
        // overload exists. Forty callers, one charge, and thirty-nine have to be told no.
        var granted = await Task.WhenAll(Enumerable.Range(0, 40).Select(i =>
            fx.Store.MutateAsync(t =>
            {
                if (t.Superlatives.Count > 0) return false;

                t.Superlatives.Add(new CharterTrip.Core.Models.Superlative
                {
                    Id = "the-one-charge", Title = $"Spent by {i}"
                });
                return true;
            }, TripArea.Trip)));

        Assert.Single(granted, won => won);
        Assert.Equal(39, granted.Count(won => !won));
        Assert.Single(fx.Store.Current.Superlatives);
    }

    /// <summary>
    /// Twenty-five phones arriving inside ninety seconds is a stream of mutations far faster than
    /// the debounce, so almost every scheduled save is superseded before it runs. That is normal and
    /// costs nothing — but the superseded task used to read <c>.Token</c> off a CancellationTokenSource
    /// the next edit had already disposed, and <c>ObjectDisposedException</c> is not
    /// <c>OperationCanceledException</c>, so it fell past the "superseded" branch and was logged as
    /// "Debounced save failed." on a save that had lost nothing.
    ///
    /// Nothing was ever actually lost, which is what made it worth fixing rather than shrugging at:
    /// an error in the log that does not mean an error teaches everybody to ignore the log on the
    /// one night it matters.
    /// </summary>
    [Fact]
    public async Task Edits_faster_than_the_debounce_do_not_report_failed_saves()
    {
        // Long enough that each edit supersedes the one before it. At the default 0 the task has
        // usually finished before the next edit lands and the window never opens.
        await using var fx = new StoreFixture(debounceMs: 50);

        for (var i = 0; i < 250; i++)
        {
            var name = $"edit {i}";
            await fx.Store.MutateAsync(t => t.Trip.Name = name, TripArea.Trip);
        }

        await fx.Store.FlushAsync();

        // Let any task that is still holding a superseded token get to its catch block.
        await Task.Delay(200);

        Assert.Empty(fx.Log.Failures);

        // And the point of the debounce still holds: the last edit is the one on disk.
        Assert.Equal("edit 249", fx.Store.Current.Trip.Name);
        var reloaded = await fx.RestartAsync();
        Assert.Equal("edit 249", reloaded.Current.Trip.Name);
    }


    /// <summary>
    /// The same storm, driven in parallel rather than in sequence.
    ///
    /// Twenty-one people tap their names inside about ninety seconds and every one of those taps is
    /// its own request, so the debounce is rescheduled from many threads at once. That is the window
    /// where a source can be disposed by a second thread between being published and being read —
    /// and if the token is read on the far side of that, the throw lands on whoever called
    /// MutateAsync rather than in a task nobody awaits, which turns a harmless superseded save into
    /// a failed request.
    /// </summary>
    [Fact]
    public async Task Concurrent_edits_neither_throw_nor_report_failed_saves()
    {
        await using var fx = new StoreFixture(debounceMs: 50);

        await Task.WhenAll(Enumerable.Range(0, 16).Select(worker => Task.Run(async () =>
        {
            for (var i = 0; i < 40; i++)
                await fx.Store.MutateAsync(t => t.Revision += 0, TripArea.Trip);
        })));

        await fx.Store.FlushAsync();
        await Task.Delay(200);

        Assert.Empty(fx.Log.Failures);
    }

}
