namespace PolicyManager.Middleware;

/// <summary>
///     Assigns every request a correlation identifier and echoes it on the response.
/// </summary>
/// <remarks>
///     Without one, a user reporting "it failed at 14:03" gives an operator nothing to grep for: the
///     request is spread across access logs, EF logs, and any broker messages, none of which share a
///     key. The identifier is taken from an inbound <c>X-Correlation-ID</c> when a caller already has
///     one — so a trace started upstream survives the hop — and otherwise minted from the connection's
///     trace identifier. It is pushed into the logging scope, so every log line written while handling
///     the request carries it without any call site having to pass it along.
/// </remarks>
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    /// <summary>
    ///     The request and response header carrying the correlation identifier.
    /// </summary>
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>
    ///     The <see cref="HttpContext.Items" /> key the identifier is stored under, so middleware and
    ///     the exception handler can read it back.
    /// </summary>
    public const string ItemKey = "CorrelationId";

    /// <summary>
    ///     Longest accepted inbound identifier. Anything longer is ignored rather than echoed.
    /// </summary>
    private const int MaxLength = 128;

    /// <summary>
    ///     Runs the rest of the pipeline with a correlation identifier in scope.
    /// </summary>
    /// <param name="context">The current request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        context.Items[ItemKey] = correlationId;

        // Registered through OnStarting rather than assigned directly, because the exception handler
        // calls Response.Clear() before writing its ProblemDetails body, and Clear() wipes the header
        // collection. OnStarting survives that, so the identifier is still on the response by the time
        // it goes out — which is exactly the failure this header exists to prevent, since the whole
        // point is having a key for the one request that failed.
        context.Response.OnStarting(static state =>
        {
            var (response, id) = ((HttpResponse, string))state;
            response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        }, (context.Response, correlationId));

        System.Diagnostics.Activity.Current?.SetTag(HeaderName, correlationId);

        using (logger.BeginScope(new Dictionary<string, object> { [ItemKey] = correlationId }))
        {
            await next(context);
        }
    }

    /// <summary>
    ///     Reuses the caller's identifier when it is usable, and mints one otherwise.
    /// </summary>
    /// <remarks>
    ///     The inbound value is attacker-controlled and is echoed back and written into a logging
    ///     scope on every line of the request, so it is length-capped and restricted to characters
    ///     that are safe in a log. An unusable value is discarded in favour of the connection's own
    ///     trace identifier rather than sanitised into something that looks legitimate but does not
    ///     match what the caller sent.
    /// </remarks>
    private string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var inbound))
        {
            var value = inbound.ToString().Trim();

            if (value.Length is > 0 and <= MaxLength && value.All(IsSafeCharacter))
            {
                return value;
            }

            logger.LogWarning(
                "Discarded an unusable {Header} header of length {Length}.",
                HeaderName,
                inbound.ToString().Length);
        }

        return context.TraceIdentifier;
    }

    /// <summary>
    ///     Whether a character is safe to echo and to embed in a log line: ASCII letters, digits, and
    ///     the separators that appear in real trace and span identifiers.
    /// </summary>
    private static bool IsSafeCharacter(char c)
        => c is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '-' or '_' or '.' or ':';
}
