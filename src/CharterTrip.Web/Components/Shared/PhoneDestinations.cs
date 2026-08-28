using CharterTrip.Core.Models;

namespace CharterTrip.Web.Components.Shared;

/// <summary>
/// One place a simulated phone can be sent, the way a QR code would have sent it.
///
/// Standing in a room and holding a camera at a sticker is the one part of these games a laptop
/// cannot do. Driving the frame to the URL that sticker holds is the nearest honest substitute:
/// the page that runs is the real page, and everything downstream of it — the scan record, the
/// trail, whatever the game does about it — is real too. What is faked is the camera, and only the
/// camera.
/// </summary>
/// <param name="Label">What the select says before anything is picked — "scan a clue…".</param>
/// <param name="For">
/// The options this phone should be offered. Takes the person holding it, because a list of badges
/// has to leave out the one in their own pocket.
/// </param>
/// <param name="Route">The URL the chosen option's code holds, or null if there is no such page.</param>
public sealed record PhoneDestinations(
    string Label,
    Func<TripData, string, IReadOnlyList<PhoneDestination>> For,
    Func<TripData, string, string?> Route);

/// <summary>One option in a destination select.</summary>
public readonly record struct PhoneDestination(string Id, string Name);
