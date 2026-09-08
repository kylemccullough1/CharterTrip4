namespace CharterTrip.Infrastructure.Photos;

/// <summary>
/// The one place that turns an upload's content type into the extension its id will carry.
///
/// The extension is not decoration. It is what tells the serving route in Program.cs which
/// content type to send back, and what tells the board whether to render a picture or a player.
/// Guessing wrong on a video means a clue that shows nothing, so an unrecognised video type
/// keeps the video default rather than falling through to the image one.
///
/// Shared because there are now two stores minting ids — the real one and the showcase's
/// in-memory one — and an id that means something different depending on which store made it
/// would be a bug that only ever showed up in the deployed showcase.
/// </summary>
internal static class PhotoContentTypes
{
    public static string ExtensionFor(string contentType) => contentType switch
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

    /// <summary>A fresh id: a guid and the extension its bytes deserve.</summary>
    public static string NewId(string contentType) => $"{Guid.NewGuid():n}{ExtensionFor(contentType)}";
}
