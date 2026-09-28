namespace PolicyManager.Exceptions;

/// <summary>
///     Thrown when a write would violate a uniqueness or state constraint that the caller can resolve.
/// </summary>
/// <remarks>
///     For example a policyholder registering an email address that is already taken. The request was
///     well formed and the caller did nothing wrong, so this is a 409 rather than a 400.
///     <para>
///     Thrown by the services when they detect the conflict themselves, so the 409 does not
///     depend on the provider enforcing a unique index. The database constraint stays as the
///     backstop for races, and the handler maps its violation to the same 409.
///     </para>
/// </remarks>
public class ConflictException(string message) : Exception(message);
