using CharterTrip.Core.Abstractions;
using CharterTrip.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace CharterTrip.Infrastructure.Photos;

/// <summary>
/// Clue media as plain files next to trip.json — pictures and, since the board grew video
/// clues, clips as well.
///
/// Images are resized in the browser before upload (the same canvas trick the old static site
/// used), so there's no server-side image library here and nothing to keep patched. Video is
/// stored exactly as it arrives for the same reason: transcoding would mean shipping ffmpeg to
/// hold one weekend's clips.
/// </summary>
public sealed class FileSystemPhotoStore(IOptions<TripStoreOptions> options) : IPhotoStore
{
    private readonly string _root = options.Value.PhotoDirectory;

    public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_root);

        // The extension is what tells the serving route its content type back, and what tells the
        // board whether to render a picture or a player. Guessing wrong on a video means a clue
        // that shows nothing, so an unrecognised video type keeps the video default rather than
        // falling through to the image one.
        var extension = contentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "video/quicktime" => ".mov",
            "video/x-m4v" => ".m4v",
            "video/ogg" => ".ogv",
            _ when contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) => ".mp4",
            _ => ".jpg"
        };

        var id = $"{Guid.NewGuid():n}{extension}";
        var path = Path.Combine(_root, id);

        await using (var file = File.Create(path))
            await content.CopyToAsync(file, ct).ConfigureAwait(false);

        return id;
    }

    public Task<Stream?> OpenAsync(string photoId, CancellationToken ct = default)
    {
        var path = ResolveSafePath(photoId);
        Stream? stream = path is not null && File.Exists(path) ? File.OpenRead(path) : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string photoId, CancellationToken ct = default)
    {
        var path = ResolveSafePath(photoId);
        if (path is not null && File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public bool Exists(string photoId)
    {
        var path = ResolveSafePath(photoId);
        return path is not null && File.Exists(path);
    }

    /// <summary>
    /// An id has to be a bare file name, so a crafted one cannot escape the photo folder.
    ///
    /// "Has no separator" is not enough by itself: <c>Path.GetFileName("..")</c> returns
    /// <c>".."</c>, which would resolve to the data directory itself — where trip.json lives.
    /// </summary>
    private string? ResolveSafePath(string photoId)
    {
        if (string.IsNullOrWhiteSpace(photoId)) return null;
        if (photoId != Path.GetFileName(photoId)) return null;
        if (photoId is "." or "..") return null;
        if (photoId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;

        return Path.Combine(_root, photoId);
    }
}
