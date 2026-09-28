namespace PolicyManager.Exceptions;

/// <summary>
///     Thrown when a requested resource does not exist, or is not visible to the caller.
/// </summary>
/// <remarks>
///     Mapped to a 404 with a ProblemDetails body by <see cref="Errors.GlobalExceptionHandler" />.
///     Services throw this rather than returning null or completing silently, because a silent
///     completion is indistinguishable at the call site from a successful write — which is how a
///     missing entity came to be reported to an API caller as a 200.
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
