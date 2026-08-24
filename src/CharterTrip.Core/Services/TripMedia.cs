using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>What a stored media reference turns out to be when you look at it.</summary>
public enum MediaKind
{
    None,
    Image,
    Video
}

/// <summary>
/// Everything the app needs to know about the media a trip points at: which files are ours,
/// which are still in use, and whether a given one is a picture or a clip.
///
/// A clue keeps one string per slot, and that string is a URL — an uploaded <c>/photos/{id}</c>
/// or a link somebody pasted. Video rides in the same field rather than a new one, because the
/// alternative is renaming a property that every existing clue is stored under, and a rename is
/// a JSON migration: old documents deserialize with the new name empty and the old value already
/// discarded, so the recovery has to happen at the node level before anything is read. That is a
/// real risk to every image reference on the board, and it buys a better field name and nothing
/// else. So <c>ClueImage</c> holds a video when it holds a video, and this decides which.
/// </summary>
public static class TripMedia
{
    /// <summary>What an uploaded file looks like in the trip, and the route that serves it.</summary>
    /// <remarks>
    /// Still <c>/photos/</c> after video joined it. The prefix is written into every clue of
    /// every stored trip and into the folder on Azure, so changing it is a data migration for
    /// the sake of a word.
    /// </remarks>
    public const string UrlPrefix = "/photos/";

    /// <summary>
    /// Containers a browser will play from a plain <c>&lt;video src&gt;</c>. Anything else — a
    /// link to a video *page*, say — is left as an image, which fails visibly rather than
    /// silently rendering an empty player.
    /// </summary>
    private static readonly string[] VideoExtensions = [".mp4", ".webm", ".mov", ".m4v", ".ogv"];

    /// <summary>The path an id is stored as.</summary>
    public static string PathFor(string mediaId) => UrlPrefix + mediaId;

    /// <summary>
    /// Whether a stored value should render as a picture or a clip.
    ///
    /// Unknown extensions come back as <see cref="MediaKind.Image"/> on purpose: that is what
    /// every pasted link was treated as before video existed, and a link with no extension is
    /// far more likely to be an image than a playable video file.
    /// </summary>
    public static MediaKind KindOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return MediaKind.None;

        return VideoExtensions.Contains(ExtensionOf(value), StringComparer.OrdinalIgnoreCase)
            ? MediaKind.Video
            : MediaKind.Image;
    }

    public static bool IsVideo(string? value) => KindOf(value) == MediaKind.Video;

    /// <summary>
    /// The media id inside a stored value, or null if the value is not one of ours — a link to
    /// somebody's shared album is not a file this app may delete.
    /// </summary>
    public static string? IdFrom(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!value.StartsWith(UrlPrefix, StringComparison.Ordinal)) return null;

        var id = value[UrlPrefix.Length..];
        return IsSafeId(id) ? id : null;
    }

    /// <summary>Every media id the trip currently points at, from wherever it points at one.</summary>
    public static HashSet<string> ReferencedIn(TripData trip)
    {
        ArgumentNullException.ThrowIfNull(trip);

        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var clue in trip.Jeopardy.Categories.SelectMany(c => c.Clues))
        {
            if (IdFrom(clue.ClueImage) is { } clueMedia) ids.Add(clueMedia);
            if (IdFrom(clue.ResponseImage) is { } responseMedia) ids.Add(responseMedia);
        }

        // Slides and teams keep a bare id rather than a path. Both shapes are collected here so
        // that whichever one wires up next cannot have its files deleted out from under it.
        foreach (var slide in trip.Slides)
            if (!string.IsNullOrWhiteSpace(slide.PhotoId)) ids.Add(slide.PhotoId);

        foreach (var team in trip.Teams)
            if (!string.IsNullOrWhiteSpace(team.PhotoId)) ids.Add(team.PhotoId);

        return ids;
    }

    /// <summary>
    /// The extension, ignoring anything a URL carries after it. A pasted link routinely ends
    /// <c>.jpg?width=800</c>, and asking the filesystem for the extension of that gets you
    /// <c>.jpg?width=800</c>.
    /// </summary>
    private static string ExtensionOf(string value)
    {
        var end = value.IndexOfAny(['?', '#']);
        var path = end >= 0 ? value[..end] : value;

        var dot = path.LastIndexOf('.');
        var slash = path.LastIndexOfAny(['/', '\\']);

        return dot > slash && dot >= 0 ? path[dot..] : string.Empty;
    }

    /// <summary>
    /// A bare file name and nothing else. Note that <c>Path.GetFileName("..")</c> returns
    /// <c>".."</c>, so "has no separator" is not on its own enough — the relative segments have
    /// to be named and refused.
    /// </summary>
    private static bool IsSafeId(string id) =>
        id.Length > 0
        && id == Path.GetFileName(id)
        && id is not ("." or "..")
        && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
}
