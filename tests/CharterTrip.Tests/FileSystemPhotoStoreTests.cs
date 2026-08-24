using System.Text;
using CharterTrip.Core.Abstractions;
using CharterTrip.Infrastructure.Photos;
using CharterTrip.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace CharterTrip.Tests;

/// <summary>
/// The photo store had no tests because nothing used it. The Jeopardy clue uploader does now,
/// and on Azure it writes to the one directory that survives a deploy.
/// </summary>
public sealed class FileSystemPhotoStoreTests : IDisposable
{
    private readonly string _root;
    private readonly IPhotoStore _photos;

    public FileSystemPhotoStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "chartertrip-photos", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
        _photos = new FileSystemPhotoStore(Options.Create(new TripStoreOptions { DataRoot = _root }));
    }

    [Fact]
    public async Task What_goes_in_comes_back_out()
    {
        var id = await SaveAsync("the bytes of a photograph");

        Assert.True(_photos.Exists(id));

        await using var stream = await _photos.OpenAsync(id);
        Assert.NotNull(stream);
        Assert.Equal("the bytes of a photograph", await new StreamReader(stream!).ReadToEndAsync());
    }

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("image/webp", ".webp")]
    [InlineData("image/gif", ".gif")]
    [InlineData("video/mp4", ".mp4")]
    [InlineData("video/webm", ".webm")]
    [InlineData("video/quicktime", ".mov")]
    [InlineData("video/x-m4v", ".m4v")]
    [InlineData("video/ogg", ".ogv")]
    public async Task The_id_carries_an_extension_matching_the_content(string contentType, string expected)
    {
        var id = await SaveAsync("bytes", contentType);

        Assert.Equal(expected, Path.GetExtension(id));
    }

    /// <summary>
    /// The extension is what tells the board whether to draw a picture or a player, so a video
    /// container nobody listed must not quietly become a .jpg and render as a broken image.
    /// </summary>
    [Fact]
    public async Task An_unlisted_video_container_still_lands_as_a_video()
    {
        var id = await SaveAsync("bytes", "video/x-matroska");

        Assert.Equal(".mp4", Path.GetExtension(id));
    }

    [Fact]
    public async Task Two_uploads_never_collide()
    {
        var first = await SaveAsync("one");
        var second = await SaveAsync("two");

        Assert.NotEqual(first, second);
        Assert.True(_photos.Exists(first));
        Assert.True(_photos.Exists(second));
    }

    [Fact]
    public async Task Deleting_removes_the_file()
    {
        var id = await SaveAsync("bytes");

        await _photos.DeleteAsync(id);

        Assert.False(_photos.Exists(id));
        Assert.Null(await _photos.OpenAsync(id));
    }

    [Fact]
    public async Task Deleting_something_already_gone_is_not_an_error()
    {
        await _photos.DeleteAsync("nothing-here.jpg");
        Assert.False(_photos.Exists("nothing-here.jpg"));
    }

    /// <summary>
    /// The id reaches this store from a URL segment and from a text box someone typed into.
    /// Neither is a reason to be able to read or delete a file outside the photo folder.
    /// </summary>
    [Theory]
    [InlineData("../trip.json")]
    [InlineData("../../trip.json")]
    [InlineData("sub/photo.jpg")]
    [InlineData("sub\\photo.jpg")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_id_cannot_escape_the_photo_folder(string id)
    {
        var outside = Path.Combine(_root, "trip.json");
        await File.WriteAllTextAsync(outside, "the trip");

        Assert.False(_photos.Exists(id));
        Assert.Null(await _photos.OpenAsync(id));

        await _photos.DeleteAsync(id);
        Assert.True(File.Exists(outside));
    }

    private async Task<string> SaveAsync(string content, string contentType = "image/jpeg")
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return await _photos.SaveAsync(stream, contentType);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
