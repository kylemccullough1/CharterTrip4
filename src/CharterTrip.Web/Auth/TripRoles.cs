namespace CharterTrip.Web.Auth;

/// <summary>Role names, named rather than typed as a literal in three places.</summary>
public static class TripRoles
{
    public const string Admin = "admin";

    /// <summary>Whoever is holding the Jeopardy host code. A job for one evening, not a person.</summary>
    public const string BuzzerHost = "buzzer-host";

    /// <summary>
    /// Whoever is holding the spelling bee's host code — the one phone in the building with the
    /// word on it. A job, like the Jeopardy host, and for the same reason: it gets handed over.
    /// </summary>
    public const string BeeHost = "bee-host";
}

/// <summary>
/// The claims this app writes into its own cookie.
///
/// Kept as constants because a claim name typed twice with different spellings is a bug that
/// authenticates nobody and reports nothing.
/// </summary>
public static class TripClaims
{
    /// <summary>The signed-in person's <c>RosterPerson.Id</c>. Absent for the committee's
    /// shared username/password session and for a buzzer-code session.</summary>
    public const string PersonId = "trip:person";

    /// <summary>The team whose buzzer this phone is. Set either from a buzzer code or from the
    /// signed-in person's own roster entry.</summary>
    public const string TeamId = "trip:team";
}
