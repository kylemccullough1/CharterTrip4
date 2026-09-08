using System.Collections.Concurrent;
using CharterTrip.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace CharterTrip.Infrastructure.Photos;

/// <summary>
/// The photo store in showcase mode: uploads are accepted and served, and never touch the disk.
///
/// Refusing uploads outright was the obvious first answer and the wrong one. A clue editor whose
/// picture button throws an error is not a demonstration of a clue editor, and the showcase's
/// whole claim is that you can use the site rather than look at photographs of it. So an upload
/// here really is stored, really does come back from <c>/photos/{id}</c>, and really does render
/// on the board — it just lives in memory.
///
/// SINGLETON, unlike <see cref="Storage.SandboxTripStore"/>, and the asymmetry is forced rather
/// than chosen. An uploaded picture is fetched by the browser as a separate HTTP request to
/// <c>/photos/{id}</c>, and on Blazor Server an HTTP request is a different scope from the
/// circuit that did the uploading — so a per-circuit photo store would accept the upload and
/// then 404 the img tag pointing at it. One shared cache is the only place both scopes can see.
///
/// Sharing it between visitors is harmless: an id is a fresh guid, nothing links to it but the
/// trip copy of the circuit that made it, and that copy is gone on the next refresh. What is
/// left is an orphan nobody can name, which is what <see cref="Capacity"/> and the oldest-first
/// eviction are for — a public site cannot let strangers fill the server's memory.
/// </summary>
public sealed class SandboxPhotoStore(IPhotoStore inner, ILogger<SandboxPhotoStore> logger) : IPhotoStore
{
    /// <summary>
    /// How much uploaded media the showcase will hold at once, across everybody.
    ///
    /// Sized against what the app already allows through the door: MediaAttachments caps a single
    /// clip at 64 MB, so this is a handful of the largest thing anyone can send. Well within what
    /// the Azure plan has spare, and small enough that the worst case of somebody hammering the
    /// upload button is some evicted pictures rather than a recycled process.
    /// </summary>
    public const long Capacity = 256L * 1024 * 1024;

    private sealed record Upload(byte[] Bytes, long Sequence);

    private readonly ConcurrentDictionary<string, Upload> _uploads = new(StringComparer.Ordinal);
    private long _sequence;
    private long _bytesHeld;

    public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct).ConfigureAwait(false);
        var bytes = buffer.ToArray();

        var id = PhotoContentTypes.NewId(contentType);
        _uploads[id] = new Upload(bytes, Interlocked.Increment(ref _sequence));
        Interlocked.Add(ref _bytesHeld, bytes.Length);

        EvictOldestUntilUnderCapacity();

        logger.LogInformation(
            "Showcase upload {Id} held in memory ({Size:N0} bytes; {Held:N0} of {Capacity:N0} in use).",
            id, bytes.Length, Interlocked.Read(ref _bytesHeld), Capacity);

        return id;
    }

    /// <summary>
    /// Memory first, then the real folder. The order matters only in that it is the cheaper
    /// check; the two id spaces cannot collide, because both are guids.
    /// </summary>
    public Task<Stream?> OpenAsync(string photoId, CancellationToken ct = default) =>
        _uploads.TryGetValue(photoId, out var upload)
            ? Task.FromResult<Stream?>(new MemoryStream(upload.Bytes, writable: false))
            : inner.OpenAsync(photoId, ct);

    /// <summary>
    /// Forgets a sandbox upload and never touches a real file.
    ///
    /// Deleting is a normal part of editing — replacing a clue photo releases the one it replaced
    /// — so this has to succeed rather than throw. It simply must not reach the folder that holds
    /// the actual trip's pictures.
    /// </summary>
    public Task DeleteAsync(string photoId, CancellationToken ct = default)
    {
        if (_uploads.TryRemove(photoId, out var removed))
            Interlocked.Add(ref _bytesHeld, -removed.Bytes.Length);

        return Task.CompletedTask;
    }

    public bool Exists(string photoId) => _uploads.ContainsKey(photoId) || inner.Exists(photoId);

    /// <summary>Bytes currently held in memory. Exposed for the health endpoint and for tests.</summary>
    public long BytesHeld => Interlocked.Read(ref _bytesHeld);

    /// <summary>
    /// Oldest out first, until the cache is back under the cap.
    ///
    /// Insertion order, not least-recently-used: an eviction here costs somebody a broken image
    /// in a sandbox they are about to lose anyway, and LRU would mean tracking a read timestamp
    /// on the one path that has to stay quick. Age is the honest proxy for "whoever uploaded this
    /// has probably closed the tab".
    /// </summary>
    private void EvictOldestUntilUnderCapacity()
    {
        while (Interlocked.Read(ref _bytesHeld) > Capacity)
        {
            var oldest = _uploads
                .OrderBy(pair => pair.Value.Sequence)
                .Select(pair => (KeyValuePair<string, Upload>?)pair)
                .FirstOrDefault();

            // Nothing left to drop. Only reachable if every upload was removed underneath us,
            // in which case the byte count is about to settle anyway.
            if (oldest is not { } entry) return;

            if (_uploads.TryRemove(entry.Key, out var removed))
            {
                Interlocked.Add(ref _bytesHeld, -removed.Bytes.Length);
                logger.LogInformation("Evicted showcase upload {Id} to stay under the memory cap.", entry.Key);
            }
        }
    }
}
