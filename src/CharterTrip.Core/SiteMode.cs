namespace CharterTrip.Core;

/// <summary>
/// Whether this deployment is the live trip or a showcase of it.
///
/// The trip is over and the site now stands as a portfolio piece, embedded in duckdgoose.net with
/// the committee credentials printed in a tutorial so visitors can see the admin view. That only
/// works if nothing they do can stick.
///
/// The obvious way to guarantee that is to refuse every write, and it was the first thing tried.
/// It guaranteed the wrong thing. A visitor handed the committee's password and then a site whose
/// buttons do nothing has not seen the admin view; they have seen a screenshot of it with a
/// working cursor, and the reasonable thing to conclude is that the site is broken. So showcase
/// mode now does what the banner has always claimed: every edit is allowed and every edit is
/// real, on a copy of the trip that belongs to that one browser tab and is discarded when the tab
/// closes or reloads. Nothing reaches trip.json, the photo folder, or the next visitor. See
/// <c>SandboxTripStore</c> and <c>SandboxPhotoStore</c> in Infrastructure, which is where the
/// whole mechanism lives — the pages, the games and the services are unchanged, and none of them
/// know which mode they are running in.
///
/// Deliberately a compile-time switch and not configuration: a setting can be changed from the
/// Azure portal by anyone who gets that far, and then the credentials in the tutorial would be
/// the keys to a writable site. Turning this off means a commit and a deploy, on purpose.
/// </summary>
public static class SiteMode
{
    /// <summary>True: the site is a sandboxed showcase. False: the live trip, edits are saved.</summary>
    public static bool ReadOnly { get; } = true;

    /// <summary>One sentence for banners, tooltips and footers.</summary>
    public const string ReadOnlyMessage =
        "This site is a read-only showcase. Change anything you like, including in the committee " +
        "view — your edits are yours alone and a refresh puts everything back.";
}
