using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace CharterTrip.Web.Auth;

/// <summary>
/// The committee's credentials, as they are allowed to exist on disk: PBKDF2-SHA256 hashes and
/// the salt that made them. Nothing here can be turned back into a username or a password.
/// </summary>
public sealed class AdminCredentialOptions
{
    public const string Section = "Admin";

    public string Salt { get; set; } = "";
    public string UsernameHash { get; set; } = "";
    public string PasswordHash { get; set; } = "";

    /// <summary>OWASP's floor for PBKDF2-SHA256. Configurable so it can be raised without a deploy.</summary>
    public int Iterations { get; set; } = 210_000;
}

public interface IAdminSignIn
{
    bool Verify(string? username, string? password);
}

/// <summary>
/// Checks a typed username and password against the stored hashes.
///
/// Both fields are hashed, not just the password. That is more than the usual advice — a username
/// is an identifier, not a secret — but this site has exactly one account and the pair is the
/// whole key to it, so neither half is written down anywhere.
///
/// The two hashes are domain-separated ("username:…" / "password:…") so that a shared salt cannot
/// reveal that the two values happen to be identical, which they are today.
/// </summary>
public sealed class AdminSignIn(IOptions<AdminCredentialOptions> options, ILogger<AdminSignIn> log) : IAdminSignIn
{
    private readonly AdminCredentialOptions _options = options.Value;

    public bool Verify(string? username, string? password)
    {
        if (_options.Salt.Length == 0 || _options.PasswordHash.Length == 0)
        {
            // Refuse rather than let everyone in. A missing configuration section is a deployment
            // mistake, and the safe reading of "no credentials are set" is "nobody is an admin".
            log.LogError("Admin credentials are not configured; refusing every sign-in.");
            return false;
        }

        byte[] salt;
        try
        {
            salt = Convert.FromBase64String(_options.Salt);
        }
        catch (FormatException)
        {
            log.LogError("Admin salt is not valid base64; refusing every sign-in.");
            return false;
        }

        // Both are compared every time, and with a fixed-time comparison, so neither the answer
        // nor how long it took says which half was wrong.
        var userOk = Matches("username", username?.Trim().ToLowerInvariant(), salt, _options.UsernameHash);
        var passOk = Matches("password", password, salt, _options.PasswordHash);

        return userOk && passOk;
    }

    private bool Matches(string label, string? value, byte[] salt, string expectedBase64)
    {
        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(expectedBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            $"{label}:{value ?? string.Empty}", salt, _options.Iterations, HashAlgorithmName.SHA256, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
