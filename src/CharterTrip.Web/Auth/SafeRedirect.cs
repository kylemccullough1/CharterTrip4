namespace CharterTrip.Web.Auth;

/// <summary>
/// Where a sign-in is allowed to send somebody afterwards.
///
/// A returnUrl taken at face value is an open redirect: a link to
/// /login?returnUrl=https://not-us.example lands a signed-in committee member on somebody else's
/// page wearing our chrome. Only a path on this site is ever honoured.
/// </summary>
public static class SafeRedirect
{
    public const string Home = "/";

    public static string Local(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return Home;

        // "/x" is ours. "//host" is protocol-relative and is not, and anything carrying a colon
        // is either a scheme or strange enough not to be worth arguing with.
        return url.StartsWith('/')
               && !url.StartsWith("//", StringComparison.Ordinal)
               && !url.StartsWith("/\\", StringComparison.Ordinal)
               && !url.Contains(':', StringComparison.Ordinal)
            ? url
            : Home;
    }
}
