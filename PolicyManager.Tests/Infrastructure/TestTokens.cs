using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PolicyManager.Configuration;
using PolicyManager.Services;

namespace PolicyManager.Tests.Infrastructure;

/// <summary>
///     Mints the bearer tokens the integration tests present.
/// </summary>
/// <remarks>
///     The tests authenticate for real rather than stubbing the handler out. A stubbed scheme would
///     keep the suite green if the signature, the issuer check, the audience check, the lifetime or
///     the role claim were all wrong, which is every part of this feature that is easy to get wrong.
///     A token that the production validation pipeline accepts, produced by the same signing key the
///     host was configured with, exercises all of it.
/// </remarks>
public static class TestTokens
{
    /// <summary>
    ///     A signing key used only by the test host. Not a secret: it is in the repository, it signs
    ///     nothing real, and it exists so the tests can produce a token the pipeline accepts.
    /// </summary>
    public const string SigningKey = "test-signing-key-that-is-long-enough-for-hmac-sha256!!";

    /// <summary>
    ///     The issuer the test host is configured with.
    /// </summary>
    public const string Issuer = "policy-manager-tests";

    /// <summary>
    ///     The audience the test host is configured with.
    /// </summary>
    public const string Audience = "policy-manager-tests-api";

    /// <summary>
    ///     Mints a token for a subject carrying the given roles.
    /// </summary>
    /// <param name="subject">The subject the token identifies, and the audit actor it produces.</param>
    /// <param name="roles">The roles to put in the token.</param>
    /// <param name="expiresIn">How long the token is valid for.</param>
    /// <param name="issuer">Overrides the issuer, to mint a token from somewhere else.</param>
    /// <param name="audience">Overrides the audience, to mint a token for something else.</param>
    /// <param name="signingKey">Overrides the key, to mint a token signed by someone else.</param>
    /// <returns>The encoded token.</returns>
    public static string Mint(
        string subject = "test-user",
        IEnumerable<string>? roles = null,
        TimeSpan? expiresIn = null,
        string? issuer = null,
        string? audience = null,
        string? signingKey = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),

            // The name claim as well as the subject. With the framework's inbound claim mapping on,
            // the identity's Name comes from a name-typed claim, and that is what ICurrentUser
            // records in the audit columns.
            new(ClaimTypes.Name, subject)
        };

        claims.AddRange((roles ?? [PolicyRoles.Admin])
            .Select(role => new Claim(PolicyRoles.ClaimType, role)));

        // The validity window is widened to twice the requested lifetime, so notBefore always lands
        // before expires and always lands in the past. A fixed offset cannot do both: an
        // already-expired token with notBefore = now would be rejected by the token constructor, and
        // a "now" notBefore on a token expiring in fifteen minutes would be rejected as not yet
        // valid. Either way the test would be measuring the constructor instead of the validation.
        var lifetime = expiresIn ?? TimeSpan.FromMinutes(15);
        var expires = DateTime.UtcNow.Add(lifetime);

        var token = new JwtSecurityToken(
            issuer ?? Issuer,
            audience ?? Audience,
            claims,
            notBefore: expires - TimeSpan.FromTicks(Math.Abs(lifetime.Ticks) * 2) - TimeSpan.FromMinutes(5),
            expires: expires,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? SigningKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    ///     Adds a bearer token to a request.
    /// </summary>
    /// <param name="request">The request to authenticate.</param>
    /// <param name="roles">The roles the token should carry.</param>
    /// <param name="subject">The subject the token should identify.</param>
    public static void Authenticate(
        this HttpRequestMessage request,
        IEnumerable<string>? roles = null,
        string subject = "test-user")
        => request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Mint(subject, roles));

    /// <summary>
    ///     Reads the signing key the running host was actually configured with.
    /// </summary>
    /// <remarks>
    ///     Read from the host rather than from <see cref="SigningKey" /> so a test cannot pass by
    ///     accident when the configuration has drifted: if the two disagree, the token is rejected
    ///     and the test fails, which is the behaviour being wanted.
    /// </remarks>
    /// <param name="services">The host's service provider.</param>
    public static JwtOptions Options(IServiceProvider services)
        => services.GetRequiredService<IOptions<JwtOptions>>().Value;
}
