using CharterTrip.Core.Abstractions;
using CharterTrip.Core.Services;
using Microsoft.AspNetCore.Components.Forms;

namespace CharterTrip.Web.Services;

/// <summary>
/// Turns a file somebody picked on their phone into something trip.json can hold.
///
/// The trip stores a path, never the bytes: a dozen clue photos would be megabytes of base64 in
/// a document that is rewritten on every keystroke, and the whole point of trip.json being
/// readable in a diff would go with it. So the file goes to <see cref="IPhotoStore"/> and the
/// clue keeps <c>/photos/{id}</c> — which is also just a URL, so what was already an
/// <c>&lt;img src&gt;</c> on the board needed nothing but a decision about which element to use.
///
/// Pictures are resized in the browser before a single byte is uploaded. That is what keeps a
/// server-side image library — and the patching it would need — out of this app entirely.
/// Video cannot be handled that way, so it is capped instead and stored exactly as it arrives.
/// </summary>
public sealed class MediaAttachments(IPhotoStore media, ITripStore store, ILogger<MediaAttachments> logger)
{
    /// <summary>
    /// Longest edge after the browser resizes a picture. A clue goes on a TV, not a billboard,
    /// and every pixel past this is upload time on venue wifi.
    /// </summary>
    private const int MaxEdge = 1600;

    /// <summary>
    /// The cap on a picture straight off a phone. Modern camera files land well under this;
    /// anything over it is a video somebody picked through the wrong button.
    /// </summary>
    public const long MaxImageBytes = 30L * 1024 * 1024;

    /// <summary>
    /// The cap on a clip. Upload runs over the SignalR circuit the page is already using, which
    /// is reliable but not fast, so this is set where a minute or so of phone video fits and a
    /// whole recorded episode does not. Past it the honest answer is to trim the clip first,
    /// which is also the answer that makes it play smoothly at the venue.
    /// </summary>
    public const long MaxVideoBytes = 64L * 1024 * 1024;

    /// <summary>Ceiling on the resized picture. Generous — a 1600px JPEG is a fraction of it.</summary>
    private const long MaxStoredImageBytes = 8L * 1024 * 1024;

    /// <summary>How long the browser gets to resize before the file is called unreadable.</summary>
    private static readonly TimeSpan ResizeTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Store the file and return the path to put in the trip.
    /// Throws <see cref="InvalidOperationException"/> with something worth showing a person.
    /// </summary>
    public async Task<string> SaveAsync(IBrowserFile file, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var contentType = file.ContentType ?? string.Empty;

        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return await SaveVideoAsync(file, contentType, ct).ConfigureAwait(false);

        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return await SaveImageAsync(file, contentType, ct).ConfigureAwait(false);

        throw new InvalidOperationException($"{file.Name} is not an image or a video.");
    }

    private async Task<string> SaveImageAsync(IBrowserFile file, string contentType, CancellationToken ct)
    {
        Require(file.Size <= MaxImageBytes, file, MaxImageBytes);

        // An animated GIF resized through a canvas comes out as one still frame, with nothing to
        // say why. It is already small and already a picture, so it goes through untouched.
        if (contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase))
            return await StoreAsync(file, contentType, MaxImageBytes, ct).ConfigureAwait(false);

        var resize = file.RequestImageFileAsync("image/jpeg", MaxEdge, MaxEdge).AsTask();

        // Blazor's resize helper waits on the image's load event and on nothing else. A file the
        // browser cannot decode — a HEIC straight off an iPhone, a truncated download — therefore
        // never completes it and never faults it either, so without this the upload button sits
        // on "Uploading…" until the page is reloaded. Long enough for a big photo on an old
        // phone; short enough to be an answer rather than a hang.
        if (await Task.WhenAny(resize, Task.Delay(ResizeTimeout, ct)).ConfigureAwait(false) != resize)
        {
            logger.LogWarning("Resizing {Name} ({Type}) did not finish in {Timeout}.",
                file.Name, contentType, ResizeTimeout);
            throw Unreadable(file);
        }

        IBrowserFile resized;
        try
        {
            resized = await resize;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The browser could not resize {Name} ({Type}).", file.Name, contentType);
            throw Unreadable(file);
        }

        return await StoreAsync(resized, "image/jpeg", MaxStoredImageBytes, ct).ConfigureAwait(false);
    }

    private async Task<string> SaveVideoAsync(IBrowserFile file, string contentType, CancellationToken ct)
    {
        Require(file.Size <= MaxVideoBytes, file, MaxVideoBytes);

        logger.LogInformation("Uploading {Name} ({Size:N0} bytes, {Type}).", file.Name, file.Size, contentType);
        return await StoreAsync(file, contentType, MaxVideoBytes, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Store a frame the browser's camera handed us, already a JPEG data URL.
    ///
    /// Separate from the IBrowserFile path because there is no file — getUserMedia gives a canvas,
    /// and routing that back through a fake upload would mean encoding it twice. It is already the
    /// right size and format when it arrives; the only thing to check is that somebody has not sent
    /// something enormous.
    /// </summary>
    public async Task<string> SaveDataUrlAsync(string dataUrl, CancellationToken ct = default)
    {
        const string prefix = "data:image/jpeg;base64,";

        if (!dataUrl.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("That photograph did not arrive as a JPEG.");

        var bytes = Convert.FromBase64String(dataUrl[prefix.Length..]);

        if (bytes.Length > MaxImageBytes)
            throw new InvalidOperationException("That photograph is too large.");

        using var stream = new MemoryStream(bytes);
        var id = await media.SaveAsync(stream, "image/jpeg", ct).ConfigureAwait(false);

        logger.LogInformation("Stored a camera frame as {Id} ({Size:N0} bytes).", id, bytes.Length);
        return TripMedia.PathFor(id);
    }

    private async Task<string> StoreAsync(IBrowserFile file, string contentType, long maxBytes, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream(maxBytes, ct);
        var id = await media.SaveAsync(stream, contentType, ct).ConfigureAwait(false);

        logger.LogInformation("Stored {Name} as {Id}.", file.Name, id);
        return TripMedia.PathFor(id);
    }

    /// <summary>
    /// Let go of a file the trip no longer points at — a replaced clue photo, or one somebody
    /// removed.
    ///
    /// Call this only *after* the change has been saved, because the decision is made by looking
    /// at the trip: if nothing references the id any more, the file goes. That check is what makes
    /// this safe when two clues were pointed at the same upload, which is easy to do by pasting a
    /// path from one field into another and would otherwise blank one of them mid-game.
    /// </summary>
    public async Task ReleaseAsync(string? value, CancellationToken ct = default)
    {
        if (TripMedia.IdFrom(value) is not { } id) return;

        if (TripMedia.ReferencedIn(store.Current).Contains(id))
        {
            logger.LogDebug("Kept {Id} — something else still points at it.", id);
            return;
        }

        try
        {
            await media.DeleteAsync(id, ct);
            logger.LogInformation("Deleted {Id}; nothing references it any more.", id);
        }
        catch (Exception ex)
        {
            // A leftover file costs a little disk. Failing the edit over it would cost more.
            logger.LogWarning(ex, "Could not delete {Id}.", id);
        }
    }

    private static void Require(bool withinLimit, IBrowserFile file, long limit)
    {
        if (withinLimit) return;

        throw new InvalidOperationException(
            $"{file.Name} is {file.Size / (1024 * 1024)} MB. The limit is {limit / (1024 * 1024)} MB — " +
            "trim it or export it smaller first.");
    }

    private static InvalidOperationException Unreadable(IBrowserFile file) =>
        new($"Could not read {file.Name}. Try saving it as a JPEG or PNG first.");
}
