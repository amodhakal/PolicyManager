using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PolicyManager.Configuration;

namespace PolicyManager.Services;

/// <summary>
///     The roles this API authorises against.
/// </summary>
/// <remarks>
///     Named constants rather than literals, so a rename is one edit here and the compiler finds every
///     claim that still uses the old name. The three roles are deliberately coarse: they map to what
///     someone does here, not to a fine-grained permission matrix that nobody would be able to
///     remember or audit.
/// </remarks>
public static class PolicyRoles
{
    /// <summary>
    ///     Full access, including cancelling a policy and adjudicating any claim.
    /// </summary>
    public const string Admin = "Admin";

    /// <summary>
    ///     Handles claims: may adjudicate them and create policies. May not cancel a policy, which
    ///     is a commercial decision rather than an operational one.
    /// </summary>
    public const string Adjuster = "Adjuster";

    /// <summary>
    ///     Front-line staff: reads records and files claims. May not adjudicate their own claims and
    ///     may not change or cancel a policy.
    /// </summary>
    public const string Agent = "Agent";

    /// <summary>
    ///     Every role, in the order they are listed in a token.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [Admin, Adjuster, Agent];

    /// <summary>
    ///     The claim type the roles travel in.
    /// </summary>
    /// <remarks>
    ///     The default, rather than a bespoke claim, so a token minted by any standard library is
    ///     understood without this service having to describe its own convention to whoever issues
    ///     them tokens.
    /// </remarks>
    public const string ClaimType = ClaimTypes.Role;

    /// <summary>
    ///     Renders a role list for a log line or an error message.
    /// </summary>
    public static string Describe() => string.Join(", ", All);
}

/// <summary>
///     Authorization policies, one per role, so a controller states a requirement in one attribute.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>
    ///     Requires any of the three roles.
    /// </summary>
    /// <remarks>
    ///     The baseline for every endpoint: unauthenticated access to any of them, including the
    ///     read-only ones, is the thing worth preventing. A leaked identifier is still a disclosure.
    /// </remarks>
    public const string AnyRole = "AnyRole";

    /// <summary>
    ///     Requires <see cref="PolicyRoles.Admin" />.
    /// </summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>
    ///     Requires an <see cref="PolicyRoles.Admin" /> or an <see cref="PolicyRoles.Adjuster" />.
    /// </summary>
    /// <remarks>
    ///     The policy for writes an adjuster legitimately makes: filing details against a policy and
    ///     deciding a claim. An agent files claims but does not decide them — the whole point of an
    ///     adjuster being a different person is that the person who filed it does not also pay it.
    /// </remarks>
    public const string AdminOrAdjuster = "AdminOrAdjuster";

    /// <summary>
    ///     Registers the policies and maps the role claim.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    public static IServiceCollection AddPolicyAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(AnyRole, policy =>
                policy.RequireAssertion(context => context.User
                    .FindAll(PolicyRoles.ClaimType)
                    .Any(claim => PolicyRoles.All.Contains(claim.Value, StringComparer.Ordinal))))

            .AddPolicy(AdminOnly, policy => policy.RequireRole(PolicyRoles.Admin))

            .AddPolicy(AdminOrAdjuster, policy =>
                policy.RequireRole(PolicyRoles.Admin, PolicyRoles.Adjuster));

        return services;
    }
}

/// <summary>
///     Validates bearer tokens, and refuses a configuration that would accept unsigned or foreign ones.
/// </summary>
public static class JwtAuthentication
{
    /// <summary>
    ///     The authentication scheme name.
    /// </summary>
    public const string Scheme = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>
    ///     Adds bearer authentication.
    /// </summary>
    /// <remarks>
    ///     The bearer options are configured through <c>Configure&lt;JwtBearerOptions&gt;</c> taking
    ///     <see cref="IOptions{TOptions}" /> rather than through the
    ///     <c>AddJwtBearer(Action&lt;JwtBearerOptions&gt;)</c> overload. The overload's callback
    ///     receives no service provider, so anything it needs has to have been read from configuration
    ///     before the host was built — which silently ignores any configuration source added later,
    ///     including a test host's. Taking the options by injection makes the binding lazy and the
    ///     override visible.
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    public static IServiceCollection AddBearerAuthentication(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>();
        services.AddAuthentication(Scheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(Scheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) => Configure(bearer, jwt.Value));

        return services;
    }

    /// <summary>
    ///     The claim type the authenticated subject's name arrives in.
    /// </summary>
    /// <remarks>
    ///     Left at the framework default of <see cref="ClaimTypes.Name" />, with inbound claim
    ///     mapping on, so a token minted by any standard library is understood without this service
    ///     publishing a convention that whoever issues the tokens has to know about. Overriding
    ///     <c>NameClaimType</c> to the raw <c>sub</c> looks tidier and silently breaks: the mapping
    ///     renames <c>sub</c> on the way in, the identity finds no claim of the configured type, and
    ///     <c>Identity.Name</c> comes back null — which then lands in the audit columns as
    ///     "system" for every authenticated write.
    /// </remarks>
    public const string NameClaimType = ClaimTypes.Name;

    private static void Configure(JwtBearerOptions bearer, JwtOptions jwt)
    {
        jwt.Validate();

        bearer.RequireHttpsMetadata = true;
        bearer.SaveToken = false;

        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),

            // A little slack, because the issuer and this service will not agree to the second and
            // rejecting a valid token because of that is a support call, not a security control.
            ClockSkew = TimeSpan.FromSeconds(Math.Max(0, jwt.ClockSkewSeconds))
        };

    }
}
