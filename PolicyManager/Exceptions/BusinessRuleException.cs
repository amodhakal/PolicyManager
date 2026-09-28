namespace PolicyManager.Exceptions;

/// <summary>
///     Thrown when a syntactically valid request breaks a domain rule, such as filing a claim against
///     a policy that is not active.
/// </summary>
/// <remarks>
///     Mapped to 422 Unprocessable Content, with <see cref="Rule" /> surfaced in the body when set.
///     The distinction from <see cref="ConflictException" /> matters to a client: a conflict is about
///     the current state of a resource, whereas a business rule is about what this request is asking
///     for, and retrying it unchanged will fail again. <see cref="Rule" /> is surfaced in the
///     ProblemDetails body when set, so a caller can tell which rule stopped them.
/// </remarks>
public class BusinessRuleException(string message, string? rule = null) : Exception(message)
{
    /// <summary>
    ///     Gets the identifier of the rule that was broken, when one is known.
    /// </summary>
    public string? Rule { get; } = rule;
}
