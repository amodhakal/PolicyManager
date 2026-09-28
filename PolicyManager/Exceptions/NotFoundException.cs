namespace PolicyManager.Exceptions;

/// <summary>
///     Thrown when a requested resource does not exist, or is not visible to the caller.
/// </summary>
/// <remarks>
///     The vocabulary the API uses to report a missing entity, mapped to a 404 with a ProblemDetails
///     body by <see cref="Errors.GlobalExceptionHandler" />. The services do not throw it yet — they
///     return null or complete silently, and the controllers decide the status — so today this type
///     is reachable only from tests. It exists so that the failure modes have one representation
///     rather than each endpoint inventing its own.
/// </remarks>
public class NotFoundException(string resource, object key)
    : Exception($"'{resource}' with identifier '{key}' was not found.")
{
    /// <summary>
    ///     Gets the kind of resource that was not found.
    /// </summary>
    public string Resource { get; } = resource;

    /// <summary>
    ///     Gets the identifier that was looked up.
    /// </summary>
    public object Key { get; } = key;
}
