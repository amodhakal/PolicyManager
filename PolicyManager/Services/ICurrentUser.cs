namespace PolicyManager.Services;

/// <summary>
///     Identifies the caller on whose behalf the current unit of work is being done.
/// </summary>
/// <remarks>
///     Introduced for the audit columns, and read from the security context rather than passed down
///     through every service call, so a new write path is audited by default instead of only if its
///     author remembered to pass an actor. Resolves to <see cref="SystemActor" /> when nothing is
///     authenticated, which is what a background worker such as the outbox processor looks like.
/// </remarks>
public interface ICurrentUser
{
    /// <summary>
    ///     The value to write to the audit columns for the current unit of work. Never null.
    /// </summary>
    /// <remarks>
    ///     A fixed, low-cardinality token when unauthenticated rather than null: the column is
    ///     filtered on, and a null would be indistinguishable from a row written before auditing
    ///     existed.
    /// </remarks>
    string Actor { get; }

    /// <summary>
    ///     The roles the current caller holds, empty when nothing is authenticated.
    /// </summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>
    ///     Whether the current caller holds a role.
    /// </summary>
    /// <param name="role">The role to test for.</param>
    bool IsInRole(string role);
}

/// <summary>
///     Resolves the current actor from the ambient <see cref="System.Security.Claims.ClaimsPrincipal" />.
/// </summary>
public class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    /// <summary>
    ///     The value recorded when no caller is authenticated.
    /// </summary>
    public const string SystemActor = "system";

    /// <inheritdoc />
    public string Actor
    {
        get
        {
            var identity = httpContextAccessor.HttpContext?.User.Identity;

            return identity is { IsAuthenticated: true, Name.Length: > 0 } ? identity.Name : SystemActor;
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> Roles
        => httpContextAccessor.HttpContext?.User.FindAll(PolicyRoles.ClaimType)
               .Select(claim => claim.Value)
               .ToArray()
           ?? [];

    /// <inheritdoc />
    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.Ordinal);
}
