using CharterTrip.Core.Abstractions;
using CharterTrip.Core.Models;
using CharterTrip.Infrastructure.Storage;

namespace CharterTrip.Tests;

/// <summary>
/// Showcase mode is a promise made to the public — the committee credentials are printed in a
/// tutorial, so a write that reached the file would be a write anyone could make. It is also a
/// promise that the site still works, which is the half the first implementation broke by
/// refusing every edit.
///
/// These tests pin both halves at once: the edit lands, and the file never moves.
/// </summary>
public sealed class SandboxTripStoreTests
{
    private static SandboxTripStore Sandbox(JsonTripStore baseline) =>
        new(baseline, new FixedClock(DateTimeOffset.UnixEpoch), new RecordingLogger<SandboxTripStore>());

    [Fact]
    public async Task An_edit_really_happens_on_the_visitors_copy()
    {
        await using var fixture = new StoreFixture();
        var sandbox = Sandbox(fixture.Store);

        await sandbox.MutateAsync(t => t.Trip.Name = "Renamed by a visitor", TripArea.Trip);

        Assert.Equal("Renamed by a visitor", sandbox.Current.Trip.Name);
        Assert.Equal(1, sandbox.WriteCount);
    }

    [Fact]
    public async Task And_never_reaches_the_real_store_or_its_file()
    {
        await using var fixture = new StoreFixture();
        var before = await File.ReadAllTextAsync(fixture.TripFilePath);
        var originalName = fixture.Store.Current.Trip.Name;
        var sandbox = Sandbox(fixture.Store);

        await sandbox.MutateAsync(t => t.Trip.Name = "Renamed by a visitor", TripArea.Trip);
        await sandbox.FlushAsync();
        await fixture.Store.FlushAsync();

        Assert.Equal(originalName, fixture.Store.Current.Trip.Name);
        Assert.Equal(before, await File.ReadAllTextAsync(fixture.TripFilePath));
    }

    /// <summary>
    /// The refresh test. A reload is a new circuit, a new scope and therefore a new sandbox, so
    /// what a second sandbox sees is exactly what the next visitor sees — and it has to be the
    /// trip as it was before anybody touched it.
    /// </summary>
    [Fact]
    public async Task A_second_sandbox_starts_from_the_untouched_trip()
    {
        await using var fixture = new StoreFixture();
        var original = fixture.Store.Current.Trip.Name;

        var first = Sandbox(fixture.Store);
        await first.MutateAsync(t => t.Trip.Name = "Renamed by a visitor", TripArea.Trip);

        var second = Sandbox(fixture.Store);

        Assert.Equal(original, second.Current.Trip.Name);
        Assert.NotEqual(first.Current.Trip.Name, second.Current.Trip.Name);
    }

    /// <summary>
    /// The failure this is really guarding against is a shallow copy: the top-level object would
    /// be new, every assertion above would still pass, and a visitor adding a person to the roster
    /// would be adding them to everybody's roster including the one on disk.
    /// </summary>
    [Fact]
    public async Task The_copy_shares_no_list_with_the_original()
    {
        await using var fixture = new StoreFixture();
        var teamsBefore = fixture.Store.Current.Teams.Count;
        var rosterBefore = fixture.Store.Current.Roster.Count;
        var sandbox = Sandbox(fixture.Store);

        await sandbox.MutateAsync(t =>
        {
            t.Teams.Add(new Team { Id = "ghost", Name = "Team Ghost" });
            t.Roster.Add(new RosterPerson { Id = "ghost", Name = "A Visitor" });
            t.Scores.Add(new ScoreEntry { TeamId = "ghost", Points = 500 });
        }, TripArea.Teams);

        Assert.Equal(teamsBefore + 1, sandbox.Current.Teams.Count);
        Assert.Equal(teamsBefore, fixture.Store.Current.Teams.Count);
        Assert.Equal(rosterBefore, fixture.Store.Current.Roster.Count);
        Assert.NotSame(fixture.Store.Current.Teams, sandbox.Current.Teams);
    }

    [Fact]
    public async Task A_mutation_with_a_verdict_runs_and_reports_it()
    {
        await using var fixture = new StoreFixture();
        var sandbox = Sandbox(fixture.Store);

        var verdict = await sandbox.MutateAsync(t =>
        {
            t.PlannerPxPerHour = 96;
            return "spent";
        }, TripArea.Itinerary);

        Assert.Equal("spent", verdict);
        Assert.Equal(96, sandbox.Current.PlannerPxPerHour);
    }

    /// <summary>
    /// Pages re-render off this event and nothing else, so a sandbox that applied edits silently
    /// would look exactly as broken as one that refused them.
    /// </summary>
    [Fact]
    public async Task Every_edit_announces_itself_so_the_page_redraws()
    {
        await using var fixture = new StoreFixture();
        var sandbox = Sandbox(fixture.Store);
        var heard = new List<TripChanged>();
        sandbox.Changed += c => { heard.Add(c); return Task.CompletedTask; };

        await sandbox.MutateAsync(t => t.Trip.Name = "One", TripArea.Trip);
        await sandbox.MutateAsync(t => t.Trip.Name = "Two", TripArea.Scores);

        Assert.Equal(2, heard.Count);
        Assert.Equal(TripArea.Trip, heard[0].Area);
        Assert.Equal(TripArea.Scores, heard[1].Area);
        Assert.True(heard[1].Revision > heard[0].Revision);
    }

    [Fact]
    public async Task An_import_replaces_the_copy_in_place_and_leaves_the_real_trip_alone()
    {
        await using var fixture = new StoreFixture();
        var realName = fixture.Store.Current.Trip.Name;
        var sandbox = Sandbox(fixture.Store);
        var held = sandbox.Current;

        await sandbox.ReplaceAsync(new TripData { Trip = new TripInfo { Name = "An imported trip" } });

        // Same object, new contents — pages on this circuit are holding the reference.
        Assert.Same(held, sandbox.Current);
        Assert.Equal("An imported trip", sandbox.Current.Trip.Name);
        Assert.Equal(realName, fixture.Store.Current.Trip.Name);
    }

    [Fact]
    public async Task It_says_out_loud_that_it_cannot_persist()
    {
        await using var fixture = new StoreFixture();
        var sandbox = Sandbox(fixture.Store);

        Assert.False(sandbox.Status.CanPersist);
        Assert.Equal(fixture.Store.Status.DataPath, sandbox.Status.DataPath);
    }
}
