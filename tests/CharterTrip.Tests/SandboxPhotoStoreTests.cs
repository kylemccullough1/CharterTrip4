using System.Text;
using CharterTrip.Core.Abstractions;
using CharterTrip.Infrastructure.Photos;
using CharterTrip.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace CharterTrip.Tests;

/// <summary>
/// The showcase's photo store. An upload has to work — a clue editor whose picture button throws
/// is not a demonstration of a clue editor — and it has to stay in memory, because the folder it
/// would otherwise write to is the one holding the real trip's pictures.
/// </summary>
public sealed class SandboxPhotoStoreTests : IDisposable
{
    private readonly string _root;
    private readonly FileSystemPhotoStore _disk;
    private readonly SandboxPhotoStore _sandbox;

    public SandboxPhotoStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "chartertrip-showcase-photos", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
        _disk = new FileSystemPhotoStore(Options.Create(new TripStoreOptions { DataRoot = _root }));
        _sandbox = new SandboxPhotoStore(_disk, new RecordingLogger<SandboxPhotoStore>());
    }

    [Fact]
    public async Task An_upload_comes_back_out_and_never_lands_on_disk()
    {
        var id = await SaveAsync(_sandbox, "the bytes of a photograph", "image/jpeg");

        Assert.True(_sandbox.Exists(id));
        Assert.Equal("the bytes of a photograph", await ReadAsync(_sandbox, id));

        // The whole point: the real store has never heard of it, and the folder it would have
        // been written to does not even exist yet.
        Assert.False(_disk.Exists(id));
        Assert.False(Directory.Exists(PhotoFolder));
    }

    /// <summary>
    /// The id's extension is what tells the serving route which content type to send back, so a
    /// sandbox upload has to be named by the same rule as a real one or a clue video arrives as
    /// a JPEG and plays nowhere.
    /// </summary>
    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("video/mp4", ".mp4")]
    [InlineData("video/quicktime", ".mov")]
    [InlineData("video/something-new", ".mp4")]
    public async Task Ids_are_named_the_same_way_the_real_store_names_them(string contentType, string extension)
    {
        var sandboxId = await SaveAsync(_sandbox, "bytes", contentType);
        var diskId = await SaveAsync(_disk, "bytes", contentType);

        Assert.EndsWith(extension, sandboxId);
        Assert.EndsWith(Path.GetExtension(diskId), sandboxId);
    }

    [Fact]
    public async Task Pictures_that_shipped_with_the_site_still_serve()
    {
        var committed = await SaveAsync(_disk, "a clue from the real trip", "image/jpeg");

        Assert.True(_sandbox.Exists(committed));
        Assert.Equal("a clue from the real trip", await ReadAsync(_sandbox, committed));
    }

    /// <summary>
    /// Releasing a replaced photo is a normal part of editing, so it has to succeed — and it must
    /// not reach the folder where the trip's own pictures live.
    /// </summary>
    [Fact]
    public async Task Deleting_forgets_an_upload_and_spares_the_real_files()
    {
        var mine = await SaveAsync(_sandbox, "mine", "image/jpeg");
        var theirs = await SaveAsync(_disk, "theirs", "image/jpeg");

        await _sandbox.DeleteAsync(mine);
        await _sandbox.DeleteAsync(theirs);

        Assert.False(_sandbox.Exists(mine));
        Assert.True(_disk.Exists(theirs));
        Assert.Equal(0, _sandbox.BytesHeld);
    }

    [Fact]
    public async Task What_it_holds_is_counted_so_the_cap_can_mean_something()
    {
        Assert.Equal(0, _sandbox.BytesHeld);

        await SaveAsync(_sandbox, "12345", "image/jpeg");
        await SaveAsync(_sandbox, "123", "image/jpeg");

        Assert.Equal(8, _sandbox.BytesHeld);
    }

    private string PhotoFolder => Path.Combine(_root, "photos");

    private static async Task<string> SaveAsync(IPhotoStore store, string content, string contentType)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return await store.SaveAsync(stream, contentType);
    }

    private static async Task<string?> ReadAsync(IPhotoStore store, string id)
    {
        await using var stream = await store.OpenAsync(id);
        if (stream is null) return null;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
