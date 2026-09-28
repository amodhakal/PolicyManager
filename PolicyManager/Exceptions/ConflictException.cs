namespace PolicyManager.Exceptions;

/// <summary>
///     Thrown when a write would violate a uniqueness or state constraint that the caller can resolve.
/// </summary>
/// <remarks>
///     For example a policyholder registering an email address that is already taken. The request was
///     well formed and the caller did nothing wrong, so this is a 409 rather than a 400.
///     <para>
///     Not yet thrown by the services. A duplicate email currently surfaces as a
///     <c>DbUpdateException</c>, which the handler also maps to 409 by reading the unique index, so
///     the observable behaviour is already correct — this type is the explicit form to migrate to
///     once the service checks for the conflict itself.
///     </para>
/// </remarks>
public class ConflictException(string message) : Exception(message);
