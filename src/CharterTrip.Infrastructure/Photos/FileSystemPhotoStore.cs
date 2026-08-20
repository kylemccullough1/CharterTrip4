using CharterTrip.Core.Abstractions;
using CharterTrip.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace CharterTrip.Infrastructure.Photos;

/// <summary>
/// Photos as plain files next to trip.json. Images are resized in the browser before upload
/// (the same canvas trick the old static site used), so there's no server-side image library
/// here and nothing to keep patched.
/// </summary>
public sealed class FileSystemPhotoStore(IOptions<TripStoreOptions> options) : IPhotoStore
{
    private readonly string _root = options.Value.PhotoDirectory;

    public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_root);

        var extension = contentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
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

    /// <summary>Reject anything with a path separator so a crafted id can't escape the photo folder.</summary>
    private string? ResolveSafePath(string photoId)
    {
        if (string.IsNullOrWhiteSpace(photoId)) return null;
        if (photoId != Path.GetFileName(photoId)) return null;
        return Path.Combine(_root, photoId);
    }
}
