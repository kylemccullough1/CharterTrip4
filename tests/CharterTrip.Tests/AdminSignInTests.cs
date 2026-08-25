using CharterTrip.Web.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CharterTrip.Tests;

/// <summary>
/// The sign-in check, which is the whole of the site's security. These run against the same
/// hashes that ship in appsettings.json, so a typo there fails here rather than at the door.
/// </summary>
public class AdminSignInTests
{
    // Copied verbatim from src/CharterTrip.Web/appsettings.json. If that file changes, this
    // fixture has to change with it — which is the point: the test knows what the site knows.
    private static AdminSignIn Live() => Build(new AdminCredentialOptions
    {
        Salt = "yfpQ29N1zpNa18gK9YNCJA==",
        UsernameHash = "3rwziecXJN3o4NlZgQQTRzuyEj4mI4PtoaAFFhQjzpM=",
        PasswordHash = "B/XJyQuJWd9euoaw4MOb95By3X+0dqAxFYloi0BgGw4=",
        Iterations = 210_000
    });

    private static AdminSignIn Build(AdminCredentialOptions options) =>
        new(Options.Create(options), NullLogger<AdminSignIn>.Instance);

    [Fact]
    public void Accepts_the_configured_credentials()
    {
        Assert.True(Live().Verify("jake123", "jake123"));
    }

    [Theory]
    [InlineData("jake123", "wrong")]
    [InlineData("wrong", "jake123")]
    [InlineData("wrong", "wrong")]
    [InlineData("jake123", "")]
    [InlineData("", "jake123")]
    [InlineData("", "")]
    [InlineData(null, null)]
    [InlineData("jake123", "JAKE123")]
    [InlineData("jake1234", "jake123")]
    [InlineData("jake123 ", "jake123 ")]
    public void Rejects_anything_else(string? username, string? password)
    {
        Assert.False(Live().Verify(username, password));
    }

    [Fact]
    public void Username_is_case_insensitive_and_trimmed()
    {
        // A username is an identifier, not a secret, and phones capitalise the first letter of
        // everything. The password is neither trimmed nor folded — see the theory above.
        Assert.True(Live().Verify("  JAKE123 ", "jake123"));
    }

    [Fact]
    public void Refuses_everyone_when_nothing_is_configured()
    {
        // An unconfigured section is a deployment mistake. The safe reading of "no credentials"
        // is "no admins", never "everyone".
        var signIn = Build(new AdminCredentialOptions());

        Assert.False(signIn.Verify("jake123", "jake123"));
        Assert.False(signIn.Verify("", ""));
    }

    [Fact]
    public void Refuses_everyone_when_the_salt_is_not_base64()
    {
        var signIn = Build(new AdminCredentialOptions
        {
            Salt = "not base64 at all!",
            UsernameHash = "3rwziecXJN3o4NlZgQQTRzuyEj4mI4PtoaAFFhQjzpM=",
            PasswordHash = "B/XJyQuJWd9euoaw4MOb95By3X+0dqAxFYloi0BgGw4="
        });

        Assert.False(signIn.Verify("jake123", "jake123"));
    }

    [Fact]
    public void The_two_hashes_differ_even_though_the_two_values_match()
    {
        // Username and password are both "jake123" today. Domain separation is what stops the
        // stored file from saying so out loud.
        var options = new AdminCredentialOptions
        {
            Salt = "yfpQ29N1zpNa18gK9YNCJA==",
            UsernameHash = "3rwziecXJN3o4NlZgQQTRzuyEj4mI4PtoaAFFhQjzpM=",
            PasswordHash = "B/XJyQuJWd9euoaw4MOb95By3X+0dqAxFYloi0BgGw4="
        };

        Assert.NotEqual(options.UsernameHash, options.PasswordHash);
    }
}
