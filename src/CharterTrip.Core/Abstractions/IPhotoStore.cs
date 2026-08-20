namespace CharterTrip.Core.Abstractions;

/// <summary>
/// Photos are too big to live inside trip.json, so they get their own store and the JSON
/// only keeps ids. Phase 5 wires this up to team photos and the carousel.
/// </summary>
public interface IPhotoStore
{
    Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct = default);
    Task<Stream?> OpenAsync(string photoId, CancellationToken ct = default);
    Task DeleteAsync(string photoId, CancellationToken ct = default);
    bool Exists(string photoId);
}
